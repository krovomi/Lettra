using Lettra.Contracts.Common;

namespace Lettra.UI.Gateway.Http;

/// <summary>Un fichier remis par l'API : contenu brut, nom dans <c>Content-Disposition</c>, refus en problème JSON.</summary>
internal static class HttpFileDownloads
{
    /// <summary>Télécharge le fichier et le remet sous la forme de la passerelle (<paramref name="map"/> : nom, contenu).</summary>
    public static Task<OperationResult<T>> GetAsync<T>(
        HttpClient http, FormattableString relativeUri, string fallbackName, Func<string, byte[], T> map, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative(relativeUri), ct),
            async (response, ct) =>
            {
                if (!response.IsSuccessStatusCode)
                {
                    return await response.FailureAsync<T>(ct).ConfigureAwait(false);
                }

                string? fileName = response.Content.Headers.ContentDisposition is { } disposition
                    ? (disposition.FileNameStar ?? disposition.FileName)?.Trim('"')
                    : null;
                byte[] content = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                return OperationResult<T>.Success(map(fileName ?? fallbackName, content));
            },
            cancellationToken);
}
