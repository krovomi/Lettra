using System.Net;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Text.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.Contracts.Serialization;

namespace Lettra.UI.Gateway.Http.Device;

/// <summary>
/// Le serveur commun retenu par l'utilisateur : l'adresse saisie (ramenée à sa racine) et ce que le serveur publie pour
/// s'y connecter. L'application installée le garde sur l'appareil et travaille ensuite sur ce serveur, comme le navigateur.
/// </summary>
public sealed record SharedServerSettings(Uri Address, Uri Authority, string ClientId)
{
    /// <summary>Réglages de connexion (PKCE) au realm du cabinet, pour l'API de ce serveur.</summary>
    public DeviceSignInOptions SignIn() => new() { BaseAddress = Address, Authority = Authority, ClientId = ClientId };
}

/// <summary>
/// Vérifie l'adresse saisie par l'utilisateur, sans connexion : c'est bien un serveur Lettra, joignable, avec un
/// certificat approuvé par l'appareil. Une adresse sans protocole est essayée en https, puis en http pour localhost.
/// </summary>
public sealed class SharedServerProbe(HttpClient http)
{
    /// <summary>Attente maximale d'une réponse : au-delà, l'adresse est jugée injoignable.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<OperationResult<SharedServerSettings>> CheckAsync(string? typed, CancellationToken cancellationToken)
    {
        IReadOnlyList<Uri> candidates = Candidates(typed);
        if (candidates.Count == 0)
        {
            return OperationResult<SharedServerSettings>.Failure(ErrorCodes.SharedServer.AddressInvalid);
        }

        if (candidates.All(c => !IsAllowed(c)))
        {
            return OperationResult<SharedServerSettings>.Failure(ErrorCodes.SharedServer.HttpsRequired);
        }

        string failure = ErrorCodes.SharedServer.Unreachable;
        foreach (Uri address in candidates.Where(IsAllowed))
        {
            OperationResult<SharedServerSettings> checkedServer = await CheckOneAsync(address, cancellationToken).ConfigureAwait(false);
            if (checkedServer.IsSuccess)
            {
                return checkedServer;
            }

            // Le refus le plus parlant l'emporte : un serveur qui répond (certificat, pas Lettra) plutôt que « injoignable ».
            string code = checkedServer.Errors[0].Code;
            if (failure == ErrorCodes.SharedServer.Unreachable)
            {
                failure = code;
            }
        }

        return OperationResult<SharedServerSettings>.Failure(failure);
    }

    /// <summary>
    /// Les adresses à essayer pour ce que l'utilisateur a tapé, ramenées à leur racine (chemin, requête et fragment
    /// retirés) : https d'abord quand le protocole manque. Liste vide si ce n'est pas une adresse de serveur.
    /// </summary>
    public static IReadOnlyList<Uri> Candidates(string? typed)
    {
        string text = (typed ?? string.Empty).Trim();
        if (text.Length == 0 || text.Any(char.IsWhiteSpace))
        {
            return [];
        }

        bool hasScheme = text.Contains("://", StringComparison.Ordinal);
        string[] attempts = hasScheme ? [text] : ["https://" + text, "http://" + text];
        List<Uri> candidates = [];
        foreach (string attempt in attempts)
        {
            if (Uri.TryCreate(attempt, UriKind.Absolute, out Uri? uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                && uri.Host.Length > 0)
            {
                candidates.Add(new Uri(uri.GetLeftPart(UriPartial.Authority) + "/"));
            }
        }

        return candidates;
    }

    /// <summary>
    /// En http, seul cet appareil est accepté (serveur de démonstration sur le même ordinateur, ou relayé par câble
    /// USB) : ailleurs, les jetons de connexion passeraient en clair sur le réseau.
    /// </summary>
    public static bool IsAllowed(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.Scheme == Uri.UriSchemeHttps || address.IsLoopback;
    }

    private async Task<OperationResult<SharedServerSettings>> CheckOneAsync(Uri address, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            using HttpResponseMessage response = await http
                .GetAsync(new Uri(address, ServerDiscoveryResponse.Path), timeout.Token)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                // Serveur Lettra dont le service de connexion (Keycloak) ne répond pas encore.
                return OperationResult<SharedServerSettings>.Failure(ErrorCodes.SharedServer.Unreachable);
            }

            ServerDiscoveryResponse? discovery = response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ServerDiscoveryResponse, timeout.Token).ConfigureAwait(false)
                : null;
            if (discovery is not { Product: ServerDiscoveryResponse.LettraProduct }
                || !Uri.TryCreate(discovery.Authority, UriKind.Absolute, out Uri? authority)
                || string.IsNullOrWhiteSpace(discovery.ClientId))
            {
                return OperationResult<SharedServerSettings>.Failure(ErrorCodes.SharedServer.NotLettra);
            }

            // Même règle pour la page de connexion que pour le serveur : pas de mot de passe en clair sur le réseau.
            return IsAllowed(authority)
                ? OperationResult<SharedServerSettings>.Success(new SharedServerSettings(address, authority, discovery.ClientId))
                : OperationResult<SharedServerSettings>.Failure(ErrorCodes.SharedServer.HttpsRequired);
        }
        catch (HttpRequestException e) when (IsCertificateFailure(e))
        {
            return OperationResult<SharedServerSettings>.Failure(ErrorCodes.SharedServer.CertificateUntrusted);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return OperationResult<SharedServerSettings>.Failure(ErrorCodes.SharedServer.Unreachable);
        }
        catch (JsonException)
        {
            return OperationResult<SharedServerSettings>.Failure(ErrorCodes.SharedServer.NotLettra);
        }
    }

    private static bool IsCertificateFailure(HttpRequestException e) =>
        e.HttpRequestError == HttpRequestError.SecureConnectionError
        || e.InnerException is AuthenticationException
        // Android et iOS (gestionnaires natifs) : l'erreur TLS arrive sous leur propre type, le message la nomme.
        || (e.InnerException?.GetType().Name.Contains("SSL", StringComparison.OrdinalIgnoreCase) ?? false)
        || (e.InnerException?.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) ?? false);
}
