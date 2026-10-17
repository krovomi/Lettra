using Lettra.Contracts.Common;
using Lettra.Contracts.Demo;
using Lettra.Contracts.Exports;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Design;

/// <summary>Point des créances et chargement de démonstration simulés pour l'accueil (vague 5, lane E).</summary>
internal sealed class FakeReceivablesGateway(ReceivablesSummaryResponse? summary) : IReceivablesGateway
{
    public Task<OperationResult<ReceivablesSummaryResponse>> GetSummaryAsync(CancellationToken cancellationToken) =>
        Task.FromResult(summary is null
            ? OperationResult<ReceivablesSummaryResponse>.Failure([new ErrorDetail("Security.Forbidden")])
            : OperationResult<ReceivablesSummaryResponse>.Success(summary));
}

internal sealed class FakeDemoDataGateway(bool available, Action seed, DemoSeedStatusDto status = DemoSeedStatusDto.Seeded) : IDemoDataGateway
{
    public int Seeds { get; private set; }

    public Task<OperationResult<DemoDataStatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<DemoDataStatusResponse>.Success(new DemoDataStatusResponse(available)));

    public Task<OperationResult<DemoSeedResponse>> SeedAsync(CancellationToken cancellationToken)
    {
        Seeds++;
        if (status == DemoSeedStatusDto.Seeded)
        {
            seed();
        }

        return Task.FromResult(OperationResult<DemoSeedResponse>.Success(new DemoSeedResponse(status, 1, 12, 30, 1)));
    }
}
