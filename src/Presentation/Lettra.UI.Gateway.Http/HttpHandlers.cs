using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

/// <summary>Ajoute l'en-tête <c>X-Dossier</c> du dossier ouvert à chaque appel.</summary>
internal sealed class DossierHeaderHandler(IDossierSelection selection) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (selection.CurrentDossierId is Guid dossier)
        {
            request.Headers.Remove(LettraHeaders.Dossier);
            request.Headers.Add(LettraHeaders.Dossier, dossier.ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// En-tête anti-CSRF exigé par l'API pour les appels authentifiés par le cookie du BFF (annexe F §2). Le
/// navigateur ne l'ajoute jamais de lui-même à une requête venue d'un autre site.
/// </summary>
internal sealed class CsrfHeaderHandler : DelegatingHandler
{
    public const string HeaderName = "X-Lettra-Csrf";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Remove(HeaderName);
        request.Headers.Add(HeaderName, "1");
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Hors ligne (selon <see cref="INetworkStatus"/>, quand l'hôte en fournit un) : la requête n'est pas envoyée et
/// l'appel échoue comme un serveur injoignable. Le navigateur n'inscrit donc pas une erreur réseau dans la console pour
/// chaque appel, et les écrans gardent leur comportement hors ligne (brouillons de l'appareil).
/// </summary>
internal sealed class OfflineShortCircuitHandler(IEnumerable<INetworkStatus> networks) : DelegatingHandler
{
    // Le dernier enregistré l'emporte, comme pour une résolution simple ; aucun quand l'hôte n'en fournit pas.
    private readonly INetworkStatus? _network = networks.LastOrDefault();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_network is { } network && await network.IsOfflineAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new HttpRequestException(HttpRequestError.ConnectionError, "Appareil hors ligne : requête non envoyée.");
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
