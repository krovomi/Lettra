namespace Lettra.Contracts.Identity;

/// <summary>
/// Ce qu'une application installée (Windows, macOS, Android, iOS) doit savoir pour travailler sur ce serveur : publié
/// sans connexion à <see cref="Path"/>, lu quand l'utilisateur saisit l'adresse du serveur du cabinet. Rien de secret.
/// </summary>
/// <param name="Product">Toujours <see cref="LettraProduct"/> : l'adresse saisie est bien celle d'un serveur Lettra.</param>
/// <param name="Authority">Realm Keycloak tel que l'appareil le voit (émetteur des jetons), où se fait la connexion.</param>
/// <param name="ClientId">Client public de l'application installée (code + PKCE).</param>
public sealed record ServerDiscoveryResponse(string Product, string Authority, string ClientId)
{
    public const string Path = ".well-known/lettra-server";

    public const string LettraProduct = "Lettra";
}
