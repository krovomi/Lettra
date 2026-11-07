using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Identity.Oidc;

/// <summary>
/// Réglages du compte de service qui crée les cabinets et leurs comptes dans Keycloak (section
/// <c>Lettra:Identity:Provisioning</c>). Sans identifiant ni secret, la plateforme n'inscrit aucun cabinet : ils se
/// déclarent alors dans la configuration, comme avant. Le secret vient du coffre de la cellule, jamais du dépôt.
/// </summary>
public sealed class IdentityProvisioningOptions
{
    public const string SectionName = "Lettra:Identity:Provisioning";

    /// <summary>Realm Keycloak ; par défaut celui de <c>Lettra:Identity:Oidc:Authority</c>.</summary>
    public string? Authority { get; set; }

    /// <summary>Client confidentiel à compte de service, avec les rôles manage-users et manage-realm du realm.</summary>
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>Validité du lien pour choisir son mot de passe.</summary>
    public TimeSpan InvitationLifetime { get; set; } = TimeSpan.FromDays(7);

    internal bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// Adaptateur Keycloak d'<see cref="IIdentityProvisioning"/> (reprise de <c>deploy/keycloak/ajouter-cabinet.sh</c>) :
/// une organisation par cabinet, ses comptes membres de l'organisation, l'e-mail « choisir son mot de passe » envoyé
/// par Keycloak. L'enrôlement du second facteur est demandé par le realm à la première connexion.
/// </summary>
internal sealed partial class KeycloakIdentityProvisioning(
    IHttpClientFactory httpClients,
    IOptions<IdentityProvisioningOptions> options,
    IOptions<OidcOptions> oidc,
    ILogger<KeycloakIdentityProvisioning> logger) : IIdentityProvisioning
{
    public const string HttpClientName = "lettra-keycloak-admin";
    private const int PageSize = 100;
    private const int MaxPages = 1000;
    private static readonly string[] PasswordAction = ["UPDATE_PASSWORD"];

    public bool IsConfigured => options.Value.IsConfigured && Realm() is not null;

    public async Task<Result<OrganisationId>> EnsureOrganisationAsync(string alias, string name, CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient http = await ClientAsync(cancellationToken).ConfigureAwait(false);
            KeycloakOrganisation? existing = await FindOrganisationAsync(http, alias, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return string.Equals(existing.Name, name, StringComparison.Ordinal) ? new OrganisationId(existing.Id) : PlatformErrors.NameTaken;
            }

            using HttpResponseMessage created = await http.PostAsJsonAsync(
                "organizations", new KeycloakOrganisation { Alias = alias, Name = name, Enabled = true }, cancellationToken).ConfigureAwait(false);
            if (created.StatusCode == HttpStatusCode.Conflict)
            {
                return PlatformErrors.NameTaken;
            }

            created.EnsureSuccessStatusCode();
            return new OrganisationId(IdOf(created));
        }
        catch (HttpRequestException exception)
        {
            LogFailed(logger, exception, "organisation");
            return PlatformErrors.IdentityFailed;
        }
    }

    public async Task<Result<ProvisionedAccount>> EnsureMemberAsync(OrganisationId organisation, string email, string displayName, CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient http = await ClientAsync(cancellationToken).ConfigureAwait(false);
            Guid userId = await FindUserAsync(http, email, cancellationToken).ConfigureAwait(false)
                ?? await CreateUserAsync(http, email, displayName, cancellationToken).ConfigureAwait(false);

            // Rattachement au cabinet : 409 si le compte en est déjà membre.
            using HttpResponseMessage member = await http.PostAsJsonAsync(
                $"organizations/{organisation.Value}/members", userId.ToString(), cancellationToken).ConfigureAwait(false);
            if (member.StatusCode != HttpStatusCode.Conflict)
            {
                member.EnsureSuccessStatusCode();
            }

            // L'e-mail « choisir son mot de passe » : sans serveur d'e-mail dans le realm, le compte existe quand même.
            int lifespan = (int)options.Value.InvitationLifetime.TotalSeconds;
            using HttpResponseMessage invitation = await http.PutAsJsonAsync(
                $"users/{userId}/execute-actions-email?lifespan={lifespan}", PasswordAction, cancellationToken).ConfigureAwait(false);
            if (!invitation.IsSuccessStatusCode)
            {
                LogInvitationNotSent(logger, (int)invitation.StatusCode);
            }

            return new ProvisionedAccount(userId, invitation.IsSuccessStatusCode);
        }
        catch (HttpRequestException exception)
        {
            LogFailed(logger, exception, "compte");
            return PlatformErrors.IdentityFailed;
        }
    }

    public async Task DeleteOrganisationAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        using HttpClient http = await ClientAsync(cancellationToken).ConfigureAwait(false);
        using HttpResponseMessage found = await http.GetAsync($"organizations/{organisation.Value}", cancellationToken).ConfigureAwait(false);
        if (found.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        found.EnsureSuccessStatusCode();

        // Les comptes d'abord (un compte sans cabinet ne doit pas rester), puis l'organisation.
        List<KeycloakUser> members;
        int pages = 0;
        do
        {
            if (++pages > MaxPages)
            {
                throw new HttpRequestException("Keycloak garde des comptes du cabinet après leur suppression.");
            }

            members = await http.GetFromJsonAsync<List<KeycloakUser>>(
                $"organizations/{organisation.Value}/members?first=0&max={PageSize}", cancellationToken).ConfigureAwait(false) ?? [];
            foreach (KeycloakUser user in members)
            {
                using HttpResponseMessage deleted = await http.DeleteAsync($"users/{user.Id}", cancellationToken).ConfigureAwait(false);
                if (deleted.StatusCode != HttpStatusCode.NotFound)
                {
                    deleted.EnsureSuccessStatusCode();
                }
            }
        }
        while (members.Count > 0);

        using HttpResponseMessage removed = await http.DeleteAsync($"organizations/{organisation.Value}", cancellationToken).ConfigureAwait(false);
        if (removed.StatusCode != HttpStatusCode.NotFound)
        {
            removed.EnsureSuccessStatusCode();
        }
    }

    /// <summary>La recherche de Keycloak porte sur le nom : on parcourt les organisations et on compare l'alias.</summary>
    private static async Task<KeycloakOrganisation?> FindOrganisationAsync(HttpClient http, string alias, CancellationToken cancellationToken)
    {
        for (int first = 0; ; first += PageSize)
        {
            List<KeycloakOrganisation> page = await http.GetFromJsonAsync<List<KeycloakOrganisation>>(
                $"organizations?briefRepresentation=true&first={first}&max={PageSize}", cancellationToken).ConfigureAwait(false) ?? [];
            KeycloakOrganisation? match = page.Find(o => string.Equals(o.Alias, alias, StringComparison.Ordinal));
            if (match is not null || page.Count < PageSize)
            {
                return match;
            }
        }
    }

    private static async Task<Guid?> FindUserAsync(HttpClient http, string email, CancellationToken cancellationToken)
    {
        List<KeycloakUser> users = await http.GetFromJsonAsync<List<KeycloakUser>>(
            $"users?email={Uri.EscapeDataString(email)}&exact=true&briefRepresentation=true", cancellationToken).ConfigureAwait(false) ?? [];
        return users.Count == 1 ? users[0].Id : null;
    }

    private static async Task<Guid> CreateUserAsync(HttpClient http, string email, string displayName, CancellationToken cancellationToken)
    {
        (string first, string last) = SplitName(displayName);
        using HttpResponseMessage created = await http.PostAsJsonAsync("users", new KeycloakUser
        {
            UserName = email,
            Email = email,
            FirstName = first,
            LastName = last,
            Enabled = true,
            RequiredActions = PasswordAction,
        }, cancellationToken).ConfigureAwait(false);
        created.EnsureSuccessStatusCode();
        return IdOf(created);
    }

    internal static (string First, string Last) SplitName(string displayName)
    {
        string name = displayName.Trim();
        int space = name.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? (name, string.Empty) : (name[..space], name[(space + 1)..].Trim());
    }

    private static Guid IdOf(HttpResponseMessage created) =>
        Guid.TryParse(created.Headers.Location?.Segments[^1], out Guid id)
            ? id
            : throw new HttpRequestException("Keycloak n'a pas renvoyé l'adresse de l'objet créé.");

    /// <summary>Un client authentifié par le compte de service, pointé sur <c>/admin/realms/{realm}/</c>.</summary>
    private async Task<HttpClient> ClientAsync(CancellationToken cancellationToken)
    {
        (Uri server, string realm) = Realm() ?? throw new InvalidOperationException("Fournisseur d'identité de la plateforme non réglé.");
        string accessToken;
        using (HttpClient tokens = httpClients.CreateClient(HttpClientName))
        {
            using FormUrlEncodedContent form = new(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = options.Value.ClientId!,
                ["client_secret"] = options.Value.ClientSecret!,
            });
            using HttpResponseMessage token = await tokens.PostAsync(
                new Uri(server, $"realms/{realm}/protocol/openid-connect/token"), form, cancellationToken).ConfigureAwait(false);
            token.EnsureSuccessStatusCode();
            KeycloakToken? issued = await token.Content.ReadFromJsonAsync<KeycloakToken>(cancellationToken).ConfigureAwait(false);
            accessToken = issued?.AccessToken ?? throw new HttpRequestException("Keycloak n'a pas délivré de jeton au compte de service.");
        }

        HttpClient http = httpClients.CreateClient(HttpClientName);
        http.BaseAddress = new Uri(server, $"admin/realms/{realm}/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return http;
    }

    /// <summary>Le serveur Keycloak et le nom du realm, tirés de l'adresse du realm (<c>…/realms/{realm}</c>).</summary>
    internal (Uri Server, string Realm)? Realm()
    {
        string authority = options.Value.Authority is { Length: > 0 } own ? own : oidc.Value.Authority;
        if (!Uri.TryCreate(authority.TrimEnd('/'), UriKind.Absolute, out Uri? realmUri))
        {
            return null;
        }

        string path = realmUri.AbsolutePath;
        int marker = path.LastIndexOf("/realms/", StringComparison.Ordinal);
        if (marker < 0 || marker + "/realms/".Length >= path.Length)
        {
            return null;
        }

        Uri server = new(realmUri, path[..(marker + 1)]);
        return (server, path[(marker + "/realms/".Length)..]);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Keycloak : création de l'{What} en échec")]
    private static partial void LogFailed(ILogger logger, Exception exception, string what);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak : e-mail pour choisir le mot de passe non envoyé (HTTP {Status}) ; le compte existe")]
    private static partial void LogInvitationNotSent(ILogger logger, int status);

    private sealed class KeycloakToken
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }

    private sealed class KeycloakOrganisation
    {
        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public Guid Id { get; set; }

        [JsonPropertyName("alias")]
        public string? Alias { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }
    }

    private sealed class KeycloakUser
    {
        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public Guid Id { get; set; }

        [JsonPropertyName("username")]
        public string? UserName { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("firstName")]
        public string? FirstName { get; set; }

        [JsonPropertyName("lastName")]
        public string? LastName { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("requiredActions")]
        public IReadOnlyList<string>? RequiredActions { get; set; }
    }
}
