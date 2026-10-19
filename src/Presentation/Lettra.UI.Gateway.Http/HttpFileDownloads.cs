using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;

namespace Lettra.UI.Gateway.Http;

/// <summary>Un fichier remis par l'API : contenu brut, nom dans <c>Content-Disposition</c>, refus en problème JSON.</summary>
internal static class HttpFileDownloads
{
    /// <summary>Télécharge le fichier et le remet sous la forme de la passerelle (<paramref name="map"/> : nom, contenu).</summary>
    public static async Task<OperationResult<T>> GetAsync<T>(
        HttpClient http, FormattableString relativeUri, string fallbackName, Func<string, byte[], T> map, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(HttpCalls.Relative(relativeUri), cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                string? fileName = response.Content.Headers.ContentDisposition is { } disposition
                    ? (disposition.FileNameStar ?? disposition.FileName)?.Trim('"')
                    : null;
                byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                return OperationResult<T>.Success(map(fileName ?? fallbackName, content));
            }

            OperationResult<ProblemResponse> problem = await response.ToOperationResultAsync(ContractsJsonContext.Default.ProblemResponse, cancellationToken)
                .ConfigureAwait(false);
            return OperationResult<T>.Failure(problem.Errors);
        }
        catch (HttpRequestException)
        {
            return OperationResult<T>.Failure(ErrorCodes.Api.Unreachable);
        }
    }
}
