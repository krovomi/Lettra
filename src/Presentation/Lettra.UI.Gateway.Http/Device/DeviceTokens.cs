namespace Lettra.UI.Gateway.Http.Device;

/// <summary>Jetons du poste, gardés dans le coffre de l'OS (DPAPI, Keychain), jamais dans un fichier en clair.</summary>
/// <param name="AccessToken">Jeton d'accès à l'API (5 minutes).</param>
/// <param name="AccessTokenExpiresAt">Échéance du jeton d'accès.</param>
/// <param name="RefreshToken">Jeton de rafraîchissement, à usage unique (rotation) ; <c>null</c> s'il n'en a pas été délivré.</param>
/// <param name="IdToken">Jeton d'identité, gardé pour la déconnexion.</param>
/// <param name="UserId">Identifiant Keycloak de l'utilisateur (<c>sub</c>).</param>
/// <param name="DisplayName">Nom affiché.</param>
public sealed record DeviceTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string? RefreshToken,
    string? IdToken,
    Guid UserId,
    string? DisplayName)
{
    /// <summary>Les jetons ne doivent jamais apparaître dans une trace.</summary>
    public override string ToString() => $"{nameof(DeviceTokens)}({UserId})";
}

/// <summary>Coffre des jetons du poste.</summary>
public interface ITokenVault
{
    Task<DeviceTokens?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Écrit les nouveaux jetons <b>avant</b> qu'ils servent : un jeton de rafraîchissement perdu bloque le poste.</summary>
    Task WriteAsync(DeviceTokens tokens, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

/// <summary>Coffre en mémoire (tests, et repli si le coffre de l'OS est indisponible : il faudra se reconnecter au relancement).</summary>
public sealed class InMemoryTokenVault : ITokenVault
{
    private DeviceTokens? _tokens;

    public Task<DeviceTokens?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(_tokens);

    public Task WriteAsync(DeviceTokens tokens, CancellationToken cancellationToken)
    {
        _tokens = tokens;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        _tokens = null;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Navigateur système pour la page de connexion de Keycloak (RFC 8252 : jamais de vue web intégrée). Sous Windows, une
/// redirection vers une adresse de bouclage (<c>http://127.0.0.1:{port}/callback</c>) ; ailleurs, un schéma propre.
/// </summary>
public interface IAuthorizationBrowser
{
    /// <summary>Adresse de retour que le navigateur atteindra ; à demander avant d'ouvrir la page (le port est choisi ici).</summary>
    Uri PrepareRedirectUri();

    /// <summary>Ouvre <paramref name="authorizeUrl"/> et attend le retour ; renvoie l'adresse de retour complète, ou <c>null</c> si l'utilisateur abandonne.</summary>
    Task<Uri?> AuthorizeAsync(Uri authorizeUrl, Uri redirectUri, CancellationToken cancellationToken);
}
