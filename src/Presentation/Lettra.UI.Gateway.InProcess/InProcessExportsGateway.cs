using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Exports.Application.Fec;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes requêtes et même permission que les routes <c>/exports/fec</c> de l'API.</summary>
internal sealed class InProcessFecGateway(IServiceScopeFactory scopes) : IFecGateway
{
    public Task<OperationResult<FecDocument>> ExportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, async (sp, dossier, ct) =>
        {
            OperationResult<FecFile> file = await InProcessScope.QueryAsync(sp, new ExportFecQuery(dossier, from, to), ct).ConfigureAwait(false);
            return file is { IsSuccess: true, Value: { } value }
                ? OperationResult<FecDocument>.Success(new FecDocument(value.FileName, value.Content))
                : OperationResult<FecDocument>.Failure(file.Errors);
        }, cancellationToken);

    public Task<OperationResult<FecValidationResponse>> ValidateAsync(
        string fileName, byte[] content, DateOnly? from, DateOnly? to, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new ValidateFecQuery(dossier, fileName, content, from, to), ct), cancellationToken);
}
