using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

/// <summary>Route <c>/exports/audit-kit</c> du dossier : l'archive arrive brute, son nom dans <c>Content-Disposition</c>.</summary>
internal sealed class HttpAuditKitGateway(HttpClient http) : IAuditKitGateway
{
    public Task<OperationResult<AuditKitDocument>> PrepareAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        HttpFileDownloads.GetAsync(
            http, $"api/v1/exports/audit-kit?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", "kit-controle-fiscal.zip",
            (name, content) => new AuditKitDocument(name, content), cancellationToken);
}
