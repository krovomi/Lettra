namespace Lettra.UI.Gateway.Http.Device;

/// <summary>
/// Connexion de l'application de bureau à Keycloak (annexe F §2) : client public <c>lettra-maui</c>, code
/// d'autorisation avec PKCE (S256) par le navigateur système, jamais de mot de passe saisi dans l'application.
/// </summary>
public sealed class DeviceSignInOptions
{
    public const string SectionName = "Lettra:Server";

    /// <summary>Adresse de l'API Lettra du cabinet ou de la cellule SaaS.</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>Realm Keycloak, par exemple <c>https://auth.lettra.fr/realms/lettra</c>.</summary>
    public Uri? Authority { get; set; }

    public string ClientId { get; set; } = "lettra-maui";

    /// <summary>
    /// <c>offline_access</c> : le jeton de rafraîchissement survit à la session SSO, pour reprendre le travail après
    /// une coupure (durée bornée par le realm, 7 jours d'inactivité).
    /// </summary>
    public string Scope { get; set; } = "openid profile offline_access";

    /// <summary>Marge avant l'échéance du jeton d'accès à laquelle il est renouvelé.</summary>
    public TimeSpan RefreshMargin { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Le poste est relié à un serveur (modes 2, 3 et 5) ; sinon il reste un poste isolé (mode 1).</summary>
    public bool IsConfigured => BaseAddress is not null && Authority is not null;
}
