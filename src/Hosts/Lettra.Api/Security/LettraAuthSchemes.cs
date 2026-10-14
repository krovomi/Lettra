namespace Lettra.Api.Security;

/// <summary>Schémas d'authentification de l'API.</summary>
public static class LettraAuthSchemes
{
    /// <summary>Aiguillage : jeton porteur (MAUI, synchro, services) ou cookie du BFF (navigateur).</summary>
    public const string Selector = "Lettra";

    /// <summary>Jeton d'accès Keycloak (<c>Authorization: Bearer</c>).</summary>
    public const string Bearer = "Bearer";

    /// <summary>Cookie de session du BFF ; les jetons restent côté serveur.</summary>
    public const string Cookie = "LettraBff";

    /// <summary>Connexion OpenID Connect du BFF (code + PKCE).</summary>
    public const string Oidc = "LettraOidc";

    /// <summary>En-tête anti-CSRF exigé sur tout appel authentifié par cookie (le navigateur ne l'ajoute jamais seul).</summary>
    public const string CsrfHeader = "X-Lettra-Csrf";
}

/// <summary>Politiques d'autorisation.</summary>
public static class LettraPolicies
{
    /// <summary>Utilisateur authentifié, second facteur prouvé si la politique l'exige. Les permissions viennent ensuite.</summary>
    public const string User = "LettraUser";
}
