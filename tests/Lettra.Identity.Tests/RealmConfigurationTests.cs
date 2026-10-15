using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Lettra.Identity.Tests;

/// <summary>
/// Le realm versionné (deploy/keycloak/realm-lettra.json) porte les règles de l'annexe F §2 : MFA obligatoire,
/// jetons courts, rotation du refresh token avec une réutilisation tolérée (grâce de la synchro hors ligne),
/// pas de flux implicite ni de mot de passe direct, PKCE, redirections exactes.
/// </summary>
public sealed class RealmConfigurationTests
{
    internal static readonly string RealmPath = Path.Combine(AppContext.BaseDirectory, "keycloak", "realm-lettra.json");

    private static readonly JsonElement Realm = JsonDocument.Parse(File.ReadAllText(RealmPath)).RootElement;

    private static IEnumerable<JsonElement> Clients => Realm.GetProperty("clients").EnumerateArray();

    private static JsonElement Client(string id) => Clients.Single(c => c.GetProperty("clientId").GetString() == id);

    private static JsonElement Flow(string alias) =>
        Realm.GetProperty("authenticationFlows").EnumerateArray().Single(f => f.GetProperty("alias").GetString() == alias);

    private static string Requirement(JsonElement flow, string step) =>
        flow.GetProperty("authenticationExecutions").EnumerateArray()
            .Single(e => (e.TryGetProperty("authenticator", out JsonElement a) ? a.GetString() : e.GetProperty("flowAlias").GetString()) == step)
            .GetProperty("requirement").GetString()!;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.GetBoolean();

    [Fact]
    public void Les_organisations_sont_activees()
    {
        Bool(Realm, "organizationsEnabled").Should().BeTrue();
        Bool(Realm, "registrationAllowed").Should().BeFalse("un compte naît d'une invitation du cabinet");
    }

    [Fact]
    public void Les_jetons_sont_courts_et_le_refresh_token_tourne_avec_une_reutilisation_toleree()
    {
        Realm.GetProperty("accessTokenLifespan").GetInt32().Should().BeLessThanOrEqualTo(300);
        Realm.GetProperty("ssoSessionIdleTimeout").GetInt32().Should().BeLessThanOrEqualTo(1800);
        Bool(Realm, "revokeRefreshToken").Should().BeTrue();
        Realm.GetProperty("refreshTokenMaxReuse").GetInt32().Should().Be(1,
            "l'ancien refresh token reste valable jusqu'à l'usage du nouveau (POC synchro hors ligne)");
    }

    [Fact]
    public void La_force_brute_est_bloquee()
    {
        Bool(Realm, "bruteForceProtected").Should().BeTrue();
        Realm.GetProperty("failureFactor").GetInt32().Should().BeLessThanOrEqualTo(5);
        Realm.GetProperty("passwordPolicy").GetString().Should().Contain("length(12)");
    }

    [Fact]
    public void Le_second_facteur_est_obligatoire_dans_le_flux_navigateur()
    {
        Realm.GetProperty("browserFlow").GetString().Should().Be("lettra-browser-mfa");
        Requirement(Flow("lettra-browser-forms"), "auth-username-password-form").Should().Be("REQUIRED");
        Requirement(Flow("lettra-browser-forms"), "lettra-second-factor").Should().Be("REQUIRED");
        Requirement(Flow("lettra-post-broker-mfa"), "lettra-second-factor-broker").Should().Be("REQUIRED",
            "le SSO du cabinet ne dispense pas du second facteur");

        Realm.GetProperty("requiredActions").EnumerateArray()
            .Single(a => a.GetProperty("alias").GetString() == "CONFIGURE_TOTP")
            .GetProperty("defaultAction").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("lettra-second-factor")]
    [InlineData("lettra-second-factor-broker")]
    public void Sans_facteur_enrole_le_formulaire_TOTP_obligatoire_impose_l_enrolement(string alias)
    {
        // Des alternatives seules échouent pour un utilisateur sans facteur : Keycloak n'enrôle que sur une étape REQUIRED.
        Requirement(Flow(alias), $"{alias}-enrole").Should().Be("CONDITIONAL");
        Requirement(Flow(alias), $"{alias}-enrolement").Should().Be("CONDITIONAL");
        Requirement(Flow($"{alias}-enrole"), "conditional-user-configured").Should().Be("REQUIRED");
        Requirement(Flow($"{alias}-enrole"), "auth-otp-form").Should().Be("ALTERNATIVE");
        Requirement(Flow($"{alias}-enrole"), "webauthn-authenticator").Should().Be("ALTERNATIVE");
        Requirement(Flow($"{alias}-enrolement"), "conditional-credential").Should().Be("REQUIRED");
        Requirement(Flow($"{alias}-enrolement"), "auth-otp-form").Should().Be("REQUIRED");
    }

    [Fact]
    public void Les_valeurs_amr_restent_valables_toute_la_session()
    {
        // Sans maxAge, Keycloak n'émet une valeur amr que dans la seconde de l'authentification : l'API refuserait tout jeton.
        int session = Realm.GetProperty("ssoSessionMaxLifespan").GetInt32();
        IEnumerable<JsonElement> amr = Realm.GetProperty("authenticatorConfig").EnumerateArray()
            .Where(c => c.GetProperty("config").TryGetProperty("default.reference.value", out _));

        amr.Should().HaveCount(3).And.OnlyContain(c =>
            int.Parse(c.GetProperty("config").GetProperty("default.reference.maxAge").GetString()!, CultureInfo.InvariantCulture) >= session);
    }

    [Theory]
    [InlineData("lettra-bff")]
    [InlineData("lettra-maui")]
    public void Les_clients_interactifs_utilisent_le_code_avec_PKCE_et_des_redirections_exactes(string clientId)
    {
        JsonElement client = Client(clientId);

        Bool(client, "standardFlowEnabled").Should().BeTrue();
        Bool(client, "implicitFlowEnabled").Should().BeFalse();
        Bool(client, "directAccessGrantsEnabled").Should().BeFalse();
        client.GetProperty("attributes").GetProperty("pkce.code.challenge.method").GetString().Should().Be("S256");
        client.GetProperty("redirectUris").EnumerateArray().Select(u => u.GetString())
            .Should().NotBeEmpty().And.OnlyContain(u => !u!.Contains('*'));
    }

    [Fact]
    public void L_application_de_bureau_peut_demander_un_jeton_hors_ligne()
    {
        JsonElement maui = Client("lettra-maui");

        Bool(maui, "publicClient").Should().BeTrue();
        maui.GetProperty("optionalClientScopes").EnumerateArray().Select(s => s.GetString()).Should().Contain("offline_access");
        Realm.GetProperty("scopeMappings").EnumerateArray()
            .Single(m => m.GetProperty("client").GetString() == "lettra-maui")
            .GetProperty("roles").EnumerateArray().Select(r => r.GetString())
            .Should().Contain("offline_access", "sans portée complète, le rôle offline_access doit être explicitement permis au client");
    }

    [Fact]
    public void Le_BFF_est_confidentiel_et_son_secret_vient_de_l_environnement()
    {
        JsonElement bff = Client("lettra-bff");

        Bool(bff, "publicClient").Should().BeFalse();
        bff.GetProperty("secret").GetString().Should().Be("${LETTRA_BFF_CLIENT_SECRET}");
    }

    [Fact]
    public void L_API_ne_fait_que_verifier_des_jetons()
    {
        Bool(Client("lettra-api"), "bearerOnly").Should().BeTrue();
    }

    [Theory]
    [InlineData("lettra-bff")]
    [InlineData("lettra-maui")]
    public void Les_jetons_portent_amr_organisation_et_audience_de_l_API(string clientId)
    {
        string[] mappers = Client(clientId).GetProperty("protocolMappers").EnumerateArray()
            .Select(m => m.GetProperty("protocolMapper").GetString()!).ToArray();

        mappers.Should().Contain("oidc-amr-mapper")
            .And.Contain("oidc-organization-membership-mapper")
            .And.Contain("oidc-audience-mapper");
    }
}

/// <summary>
/// Import réel du realm dans Keycloak 26. Ignoré sans Docker en local ; toujours exécuté en CI.
/// </summary>
public sealed class KeycloakRealmImportTests
{
    private static readonly bool DockerAvailable =
        Environment.GetEnvironmentVariable("CI") == "true"
        || File.Exists("/var/run/docker.sock")
        || OperatingSystem.IsWindows();

    [SkippableFact]
    public async Task Keycloak_importe_le_realm_et_publie_sa_decouverte()
    {
        Skip.IfNot(DockerAvailable, "Docker absent");

        await using IContainer keycloak = new ContainerBuilder("quay.io/keycloak/keycloak:26.4")
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

        using var http = new HttpClient { BaseAddress = new Uri($"http://{keycloak.Hostname}:{keycloak.GetMappedPublicPort(8080)}") };
        using HttpResponseMessage discovery = await http.GetAsync(new Uri("/realms/lettra/.well-known/openid-configuration", UriKind.Relative));
        discovery.StatusCode.Should().Be(HttpStatusCode.OK);

        using HttpResponseMessage tokenResponse = await http.PostAsync(
            new Uri("/realms/master/protocol/openid-connect/token", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = "admin",
                ["password"] = "admin",
            }));
        tokenResponse.EnsureSuccessStatusCode();
        JsonElement token = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/admin/realms/lettra", UriKind.Relative));
        request.Headers.Authorization = new("Bearer", token.GetProperty("access_token").GetString());
        using HttpResponseMessage realmResponse = await http.SendAsync(request);
        realmResponse.EnsureSuccessStatusCode();
        JsonElement realm = await realmResponse.Content.ReadFromJsonAsync<JsonElement>();

        realm.GetProperty("organizationsEnabled").GetBoolean().Should().BeTrue();
        realm.GetProperty("refreshTokenMaxReuse").GetInt32().Should().Be(1);
        realm.GetProperty("browserFlow").GetString().Should().Be("lettra-browser-mfa");

        // Le client de bureau peut demander offline_access (jeton de rafraîchissement qui survit à la session SSO).
        using var clients = new HttpRequestMessage(HttpMethod.Get, new Uri("/admin/realms/lettra/clients?clientId=lettra-maui", UriKind.Relative));
        clients.Headers.Authorization = new("Bearer", token.GetProperty("access_token").GetString());
        using HttpResponseMessage clientsResponse = await http.SendAsync(clients);
        clientsResponse.EnsureSuccessStatusCode();
        string mauiId = (await clientsResponse.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("id").GetString()!;
        using var scopes = new HttpRequestMessage(HttpMethod.Get, new Uri($"/admin/realms/lettra/clients/{mauiId}/optional-client-scopes", UriKind.Relative));
        scopes.Headers.Authorization = new("Bearer", token.GetProperty("access_token").GetString());
        using HttpResponseMessage scopesResponse = await http.SendAsync(scopes);
        scopesResponse.EnsureSuccessStatusCode();
        (await scopesResponse.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .Select(scope => scope.GetProperty("name").GetString()).Should().Contain("offline_access");
    }
}
