using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Banking;

/// <summary>Banques connectées en mémoire. Sans <see cref="Available"/>, l'écran se comporte comme sans agrégateur.</summary>
internal sealed class FakeBankConnectionsGateway : IBankConnectionsGateway
{
    public static readonly BankInstitutionResponse Lumiere = new("sim-lumiere", "Banque Lumière (simulée)");

    public bool Available { get; set; }

    public List<BankConnectionResponse> Connections { get; } = [];

    public List<StartBankConnectionRequest> Started { get; } = [];

    public List<(Guid Id, string? Code)> Completed { get; } = [];

    public List<SyncBankConnectionsRequest> Syncs { get; } = [];

    public List<Guid> Revoked { get; } = [];

    public int NextImported { get; set; } = 3;

    public string? NextError { get; set; }

    public string RedirectUrl { get; set; } = "bank/simulated-consent?connection=00000000-0000-0000-0000-000000000001&bank=Banque";

    public static BankConnectionResponse Connection(
        BankConnectionStatusDto status = BankConnectionStatusDto.Active, DateTimeOffset? lastSync = null, bool needsRenewal = false, DateTimeOffset? expires = null) =>
        new(Guid.CreateVersion7(), Lumiere.Id, Lumiere.Name, "FR76 •••• 0142", status, expires ?? new DateTimeOffset(2027, 4, 1, 0, 0, 0, TimeSpan.Zero),
            needsRenewal, lastSync, 2, new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));

    public Task<OperationResult<BankConnectionsResponse>> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<BankConnectionsResponse>.Success(Available
            ? new BankConnectionsResponse(true, "Agrégateur simulé", [Lumiere], [.. Connections])
            : new BankConnectionsResponse(false, null, [], [])));

    public Task<OperationResult<BankConsentRedirectResponse>> StartAsync(StartBankConnectionRequest request, CancellationToken cancellationToken)
    {
        if (TakeError() is { } error)
        {
            return Task.FromResult(OperationResult<BankConsentRedirectResponse>.Failure(error));
        }

        Started.Add(request);
        return Task.FromResult(OperationResult<BankConsentRedirectResponse>.Success(new BankConsentRedirectResponse(Guid.CreateVersion7(), RedirectUrl)));
    }

    public Task<OperationResult<BankConnectionCompletedResponse>> CompleteAsync(Guid connectionId, CompleteBankConnectionRequest request, CancellationToken cancellationToken)
    {
        Completed.Add((connectionId, request.Code));
        if (TakeError() is { } error)
        {
            return Task.FromResult(OperationResult<BankConnectionCompletedResponse>.Failure(error));
        }

        BankConnectionResponse connection = Connection();
        Connections.Add(connection);
        return Task.FromResult(OperationResult<BankConnectionCompletedResponse>.Success(new BankConnectionCompletedResponse(
            connection, new BankConnectionSyncOutcome(connection.Id, connection.InstitutionName, NextImported, 0, 1, []))));
    }

    public Task<OperationResult<SyncBankConnectionsResponse>> SyncAsync(SyncBankConnectionsRequest request, CancellationToken cancellationToken)
    {
        Syncs.Add(request);
        string? error = TakeError();
        BankConnectionSyncOutcome[] outcomes =
        [
            .. Connections.Where(c => c.Status == BankConnectionStatusDto.Active && (request.ConnectionId is null || c.Id == request.ConnectionId))
                .Select(c => error is null
                    ? new BankConnectionSyncOutcome(c.Id, c.InstitutionName, NextImported, 1, 1, [])
                    : new BankConnectionSyncOutcome(c.Id, c.InstitutionName, 0, 0, 0, [new ErrorDetail(error)])),
        ];
        return Task.FromResult(OperationResult<SyncBankConnectionsResponse>.Success(new SyncBankConnectionsResponse(outcomes)));
    }

    public Task<OperationResult<BankConnectionResponse>> RevokeAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        Revoked.Add(connectionId);
        int index = Connections.FindIndex(c => c.Id == connectionId);
        BankConnectionResponse revoked = Connections[index] with { Status = BankConnectionStatusDto.Revoked };
        Connections[index] = revoked;
        return Task.FromResult(OperationResult<BankConnectionResponse>.Success(revoked));
    }

    private string? TakeError()
    {
        string? error = NextError;
        NextError = null;
        return error;
    }
}
