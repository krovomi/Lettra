using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Identity.Oidc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lettra.Identity.Tests;

/// <summary>Le nom du realm et du serveur, tirés de l'adresse du realm ; le découpage d'un nom affiché.</summary>
public sealed class KeycloakProvisioningSettingsTests
{
    [Theory]
    [InlineData("https://auth.lettra.fr/realms/lettra", "https://auth.lettra.fr/", "lettra")]
    [InlineData("http://keycloak:8080/realms/lettra/", "http://keycloak:8080/", "lettra")]
    [InlineData("https://lettra.fr/auth/realms/cabinets", "https://lettra.fr/auth/", "cabinets")]
    public void Le_realm_vient_de_l_adresse_du_realm(string authority, string server, string realm)
    {
        (Uri Server, string Realm)? parsed = Adapter(authority, own: null).Realm();

        parsed.Should().NotBeNull();
        parsed!.Value.Server.Should().Be(new Uri(server));
        parsed.Value.Realm.Should().Be(realm);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pas une adresse")]
    [InlineData("https://auth.lettra.fr/")]
    [InlineData("https://auth.lettra.fr/realms/")]
    public void Sans_adresse_de_realm_la_plateforme_n_inscrit_aucun_cabinet(string authority)
    {
        KeycloakIdentityProvisioning adapter = Adapter(authority, own: null);

        adapter.Realm().Should().BeNull();
        adapter.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void L_adresse_propre_au_compte_de_service_l_emporte_et_le_secret_est_requis()
    {
        Adapter("https://auth.lettra.fr/realms/lettra", own: "https://interne:8443/realms/plateforme").Realm()!.Value.Realm.Should().Be("plateforme");
        Adapter("https://auth.lettra.fr/realms/lettra", own: null).IsConfigured.Should().BeTrue();
        Adapter("https://auth.lettra.fr/realms/lettra", own: null, secret: null).IsConfigured.Should().BeFalse();
    }

    [Theory]
    [InlineData("Camille Martin", "Camille", "Martin")]
    [InlineData("  Jean  de la Fontaine ", "Jean", "de la Fontaine")]
    [InlineData("Prince", "Prince", "")]
    public void Le_nom_affiche_se_partage_en_prenom_et_nom(string displayName, string first, string last) =>
        KeycloakIdentityProvisioning.SplitName(displayName).Should().Be((first, last));

    [Fact]
    public void Sans_compte_de_service_l_adaptateur_n_est_pas_enregistre()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddOidcIdentity(new ConfigurationBuilder().Build());

        services.Should().NotContain(d => d.ImplementationType == typeof(KeycloakIdentityProvisioning));
    }

    private static KeycloakIdentityProvisioning Adapter(string authority, string? own, string? secret = "secret") => new(
        new NoHttp(),
        Options.Create(new IdentityProvisioningOptions { Authority = own, ClientId = "lettra-provisioning", ClientSecret = secret }),
        Options.Create(new OidcOptions { Authority = authority }),
        NullLogger<KeycloakIdentityProvisioning>.Instance);

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("aucun appel attendu");
    }
}

/// <summary>
/// L'adaptateur contre un vrai Keycloak 26 avec le realm versionné : organisation, compte membre, suppression, et
/// idempotence de chaque étape. Le compte de service a les rôles documentés (manage-users, view-users, query-users,
/// manage-realm, view-realm du client realm-management). Ignoré sans Docker en local ; toujours exécuté en CI.
/// </summary>
public sealed class KeycloakProvisioningTests
{
    private const string Secret = "secret-du-compte-de-service";

    private static readonly bool DockerAvailable =
        Environment.GetEnvironmentVariable("CI") == "true"
        || File.Exists("/var/run/docker.sock")
        || OperatingSystem.IsWindows();

    /// <summary>Le bac à sable n'atteint pas quay.io : il passe par un miroir (même image).</summary>
    private static readonly string Image = Environment.GetEnvironmentVariable("LETTRA_KEYCLOAK_IMAGE") ?? "quay.io/keycloak/keycloak:26.4";

    [SkippableFact]
    public async Task Organisation_compte_et_suppression_chez_Keycloak()
    {
        Skip.IfNot(DockerAvailable, "Docker absent");
        await using IContainer keycloak = new ContainerBuilder(Image)
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
            .WithEnvironment("LETTRA_PUBLIC_URL", "https://localhost:5001")
            .WithEnvironment("LETTRA_BFF_CLIENT_SECRET", "secret-de-test")
            .WithResourceMapping(new FileInfo(RealmConfigurationTests.RealmPath), "/opt/keycloak/data/import/")
            .WithCommand("start-dev", "--import-realm", "--features=organization")
            .WithPortBinding(8080, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPath("/realms/lettra").ForPort(8080)))
            .Build();
        await keycloak.StartAsync();
        Uri server = new($"http://{keycloak.Hostname}:{keycloak.GetMappedPublicPort(8080)}/");
        using HttpClient admin = await AdminAsync(server);
        await CreateServiceAccountAsync(admin);

        ServiceCollection services = new();
        services.AddLogging();
        services.AddOidcIdentity(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lettra:Identity:Oidc:Authority"] = new Uri(server, "realms/lettra").ToString(),
            ["Lettra:Identity:Provisioning:ClientId"] = "lettra-provisioning",
            ["Lettra:Identity:Provisioning:ClientSecret"] = Secret,
        }).Build());
        await using ServiceProvider provider = services.BuildServiceProvider();
        IIdentityProvisioning identity = provider.GetRequiredService<IIdentityProvisioning>();
        identity.IsConfigured.Should().BeTrue();

        // L'organisation : créée, puis retrouvée à l'identique ; un autre nom sous le même nom technique est refusé.
        OrganisationId cabinet = (await identity.EnsureOrganisationAsync("cabinet-essai", "Cabinet d'essai", default)).Value;
        (await identity.EnsureOrganisationAsync("cabinet-essai", "Cabinet d'essai", default)).Value.Should().Be(cabinet);
        (await identity.EnsureOrganisationAsync("cabinet-essai", "Autre cabinet", default)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(PlatformErrors.NameTakenCode);

        // Le compte : créé membre du cabinet, avec le changement de mot de passe exigé ; sans serveur d'e-mail, pas d'envoi.
        ProvisionedAccount account = (await identity.EnsureMemberAsync(cabinet, "camille@cabinet-essai.example", "Camille Martin", default)).Value;
        account.InvitationSent.Should().BeFalse("le realm d'essai n'a pas de serveur d'e-mail");
        (await identity.EnsureMemberAsync(cabinet, "camille@cabinet-essai.example", "Camille Martin", default)).Value.UserId.Should().Be(account.UserId);
        JsonElement user = await admin.GetFromJsonAsync<JsonElement>($"users/{account.UserId}");
        user.GetProperty("firstName").GetString().Should().Be("Camille");
        user.GetProperty("requiredActions").EnumerateArray().Select(a => a.GetString()).Should().Contain("UPDATE_PASSWORD");
        JsonElement members = await admin.GetFromJsonAsync<JsonElement>($"organizations/{cabinet.Value}/members");
        members.EnumerateArray().Select(m => m.GetProperty("id").GetGuid()).Should().Equal(account.UserId);

        // Le départ : comptes puis organisation supprimés ; une seconde fois ne fait rien.
        await identity.DeleteOrganisationAsync(cabinet, default);
        (await admin.GetAsync(new Uri($"organizations/{cabinet.Value}", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.GetAsync(new Uri($"users/{account.UserId}", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await identity.DeleteOrganisationAsync(cabinet, default);
    }

    /// <summary>Un client sur l'API d'administration du realm lettra, avec le compte d'amorçage.</summary>
    private static async Task<HttpClient> AdminAsync(Uri server)
    {
        HttpClient http = new() { BaseAddress = new Uri(server, "admin/realms/lettra/") };
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = "admin",
            ["password"] = "admin",
        });
        using HttpResponseMessage token = await http.PostAsync(new Uri(server, "realms/master/protocol/openid-connect/token"), form);
        token.EnsureSuccessStatusCode();
        JsonElement issued = await token.Content.ReadFromJsonAsync<JsonElement>();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.GetProperty("access_token").GetString());
        return http;
    }

    /// <summary>Le compte de service de la plateforme, comme l'installe la cellule (rôles du client realm-management).</summary>
    private static async Task CreateServiceAccountAsync(HttpClient admin)
    {
        (await admin.PostAsJsonAsync("clients", new
        {
            clientId = "lettra-provisioning",
            enabled = true,
            publicClient = false,
            serviceAccountsEnabled = true,
            standardFlowEnabled = false,
            directAccessGrantsEnabled = false,
            secret = Secret,
        })).EnsureSuccessStatusCode();
        string client = (await admin.GetFromJsonAsync<JsonElement>("clients?clientId=lettra-provisioning"))[0].GetProperty("id").GetString()!;
        string account = (await admin.GetFromJsonAsync<JsonElement>($"clients/{client}/service-account-user")).GetProperty("id").GetString()!;
        string management = (await admin.GetFromJsonAsync<JsonElement>("clients?clientId=realm-management"))[0].GetProperty("id").GetString()!;
        List<JsonElement> roles = [];
        foreach (string role in new[] { "manage-users", "view-users", "query-users", "manage-realm", "view-realm" })
        {
            roles.Add(await admin.GetFromJsonAsync<JsonElement>($"clients/{management}/roles/{role}"));
        }

        (await admin.PostAsJsonAsync($"users/{account}/role-mappings/clients/{management}", roles)).EnsureSuccessStatusCode();
    }
}
