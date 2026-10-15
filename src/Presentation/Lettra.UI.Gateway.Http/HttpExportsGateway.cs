using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

/// <summary>Routes <c>/exports/fec</c> du dossier : le fichier arrive brut, son nom dans <c>Content-Disposition</c>.</summary>
internal sealed class HttpFecGateway(HttpClient http) : IFecGateway
{
    public async Task<OperationResult<FecDocument>> ExportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(HttpCalls.Relative($"api/v1/exports/fec?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"), cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                string? fileName = response.Content.Headers.ContentDisposition is { } disposition
                    ? (disposition.FileNameStar ?? disposition.FileName)?.Trim('"')
                    : null;
                byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                return OperationResult<FecDocument>.Success(new FecDocument(fileName ?? "FEC.txt", content));
            }

            OperationResult<ProblemResponse> problem = await response.ToOperationResultAsync(ContractsJsonContext.Default.ProblemResponse, cancellationToken)
                .ConfigureAwait(false);
            return OperationResult<FecDocument>.Failure(problem.Errors);
        }
        catch (HttpRequestException)
        {
            return OperationResult<FecDocument>.Failure(ErrorCodes.Api.Unreachable);
        }
    }

    // Corps brut, comme le fichier sorti du logiciel comptable.
    public Task<OperationResult<FecValidationResponse>> ValidateAsync(
        string fileName, byte[] content, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        Uri uri = from is { } start && to is { } end
            ? HttpCalls.Relative($"api/v1/exports/fec/validation?fileName={Uri.EscapeDataString(fileName)}&from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}")
            : HttpCalls.Relative($"api/v1/exports/fec/validation?fileName={Uri.EscapeDataString(fileName)}");
        return HttpCalls.SendAsync(
            async ct =>
            {
                using ByteArrayContent body = new(content);
                return await http.PostAsync(uri, body, ct).ConfigureAwait(false);
            },
            ContractsJsonContext.Default.FecValidationResponse,
            cancellationToken);
    }
}
