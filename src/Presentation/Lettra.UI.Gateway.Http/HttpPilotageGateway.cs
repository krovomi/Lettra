using System.Text.Json.Serialization.Metadata;
using Lettra.Contracts.Common;
using Lettra.Contracts.Pilotage;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpPilotageGateway(HttpClient http) : IPilotageGateway
{
    public Task<OperationResult<DossierPilotageResponse>> GetDossierAsync(CancellationToken cancellationToken) =>
        GetAsync("api/v1/pilotage", ContractsJsonContext.Default.DossierPilotageResponse, cancellationToken);

    public Task<OperationResult<CabinetPilotageResponse>> GetCabinetAsync(CancellationToken cancellationToken) =>
        GetAsync("api/v1/dossiers/pilotage", ContractsJsonContext.Default.CabinetPilotageResponse, cancellationToken);

    public Task<OperationResult<AdministrationPilotageResponse>> GetAdministrationAsync(CancellationToken cancellationToken) =>
        GetAsync("api/v1/dossiers/pilotage/administration", ContractsJsonContext.Default.AdministrationPilotageResponse, cancellationToken);

    private async Task<OperationResult<T>> GetAsync<T>(string path, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken).ConfigureAwait(false);
            return await response.ToOperationResultAsync(typeInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return OperationResult<T>.Failure(ErrorCodes.Api.Unreachable);
        }
    }
}
