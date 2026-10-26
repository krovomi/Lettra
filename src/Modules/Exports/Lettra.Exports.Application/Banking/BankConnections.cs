using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.Domain.Banking;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.Banking;

/// <summary>Les banques connectées du dossier, et celles que l'agrégateur sait relier.</summary>
public sealed record GetBankConnectionsQuery(DossierId Dossier) : IQuery<Result<BankConnectionsResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Relier un compte (ou renouveler son consentement) : rend la page de la banque où consentir.</summary>
public sealed record StartBankConnectionCommand(DossierId Dossier, StartBankConnectionRequest Request)
    : ICommand<Result<BankConsentRedirectResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>Le retour de la banque : le compte est relié et ses 90 derniers jours d'opérations arrivent au rapprochement.</summary>
public sealed record CompleteBankConnectionCommand(DossierId Dossier, Guid ConnectionId, CompleteBankConnectionRequest Request)
    : ICommand<Result<BankConnectionCompletedResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>Lire les nouvelles opérations d'un compte relié, ou de tous (seulement ceux relus il y a plus de 6 heures, au choix).</summary>
public sealed record SyncBankConnectionsCommand(DossierId Dossier, SyncBankConnectionsRequest Request)
    : ICommand<Result<SyncBankConnectionsResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>Déconnecter un compte : le consentement est retiré, les opérations déjà lues restent.</summary>
public sealed record RevokeBankConnectionCommand(DossierId Dossier, Guid ConnectionId)
    : ICommand<Result<BankConnectionResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

internal static class BankConnectionView
{
    public static BankConnectionResponse ToResponse(BankConnection connection, DateTimeOffset now) => new(
        connection.Id,
        connection.InstitutionId,
        connection.InstitutionName,
        connection.AccountDisplay,
        connection.IsExpired(now) ? BankConnectionStatusDto.Expired : (BankConnectionStatusDto)(int)connection.Status,
        connection.ConsentExpiresAt,
        connection.NeedsRenewal(now),
        connection.LastSyncAt,
        connection.LastSyncImported,
        connection.CreatedAt);
}

internal sealed class GetBankConnectionsHandler(IBankAggregator aggregator, IBankConnectionRepository connections, TimeProvider clock)
    : IQueryHandler<GetBankConnectionsQuery, Result<BankConnectionsResponse>>
{
    public async Task<Result<BankConnectionsResponse>> HandleAsync(GetBankConnectionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!aggregator.IsAvailable)
        {
            return new BankConnectionsResponse(false, null, [], []);
        }

        DateTimeOffset now = clock.GetUtcNow();
        IReadOnlyList<BankInstitution> institutions = await aggregator.ListInstitutionsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<BankConnection> all = await connections.ListAsync(cancellationToken).ConfigureAwait(false);
        return new BankConnectionsResponse(
            true,
            aggregator.Name,
            [.. institutions.Select(i => new BankInstitutionResponse(i.Id, i.Name))],
            [.. all.Where(c => c.Status != BankConnectionStatus.Pending).Select(c => BankConnectionView.ToResponse(c, now))]);
    }
}

internal sealed class StartBankConnectionHandler(
    IBankAggregator aggregator,
    IBankConnectionRepository connections,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<StartBankConnectionCommand, Result<BankConsentRedirectResponse>>
{
    public async Task<Result<BankConsentRedirectResponse>> HandleAsync(StartBankConnectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!aggregator.IsAvailable)
        {
            return BankConnectionErrors.Unavailable;
        }

        StartBankConnectionRequest request = command.Request ?? new StartBankConnectionRequest();
        IReadOnlyList<BankInstitution> institutions = await aggregator.ListInstitutionsAsync(cancellationToken).ConfigureAwait(false);
        if (institutions.FirstOrDefault(i => string.Equals(i.Id, request.InstitutionId, StringComparison.Ordinal)) is not { } institution)
        {
            return BankConnectionErrors.InstitutionUnknown;
        }

        if (request.ReplacesId is Guid replaced && await connections.FindAsync(replaced, cancellationToken).ConfigureAwait(false) is not { Status: BankConnectionStatus.Active })
        {
            return BankConnectionErrors.NotFound;
        }

        // L'identifiant de la connexion part chez l'agrégateur avant qu'elle existe : il revient avec la banque.
        Guid connectionId = Guid.CreateVersion7();
        BankConsentStart consent = await aggregator.StartConsentAsync(institution, connectionId, cancellationToken).ConfigureAwait(false);
        BankConnection connection = BankConnection.Start(
            aggregator.Name, institution.Id, institution.Name, consent.ProviderReference, request.ReplacesId, user.UserId, clock.GetUtcNow(), connectionId);
        await connections.AddAsync(connection, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new BankConsentRedirectResponse(connection.Id, consent.RedirectUrl);
    }
}

internal sealed class CompleteBankConnectionHandler(
    IBankAggregator aggregator,
    IBankConnectionRepository connections,
    BankConnectionSync sync,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<CompleteBankConnectionCommand, Result<BankConnectionCompletedResponse>>
{
    public async Task<Result<BankConnectionCompletedResponse>> HandleAsync(CompleteBankConnectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!aggregator.IsAvailable)
        {
            return BankConnectionErrors.Unavailable;
        }

        BankConnection? connection = await connections.FindAsync(command.ConnectionId, cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return BankConnectionErrors.NotFound;
        }

        if (connection.Status != BankConnectionStatus.Pending)
        {
            return BankConnectionErrors.AlreadyCompleted;
        }

        Result<BankConsentGrant> grant = await aggregator.CompleteConsentAsync(connection.ProviderReference, command.Request?.Code, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Errors.Any(e => e.Code == BankConnectionErrors.ConsentRefusedCode)
                ? new BankConnectionCompletedResponse(
                    false, BankConnectionView.ToResponse(connection, clock.GetUtcNow()), new BankConnectionSyncOutcome(connection.Id, connection.InstitutionName, 0, 0, 0, []))
                : Result<BankConnectionCompletedResponse>.Failure(grant.Errors);
        }

        DateTimeOffset now = clock.GetUtcNow();
        Result activated = connection.Activate(grant.Value.AccountIban, grant.Value.ExpiresAt, now);
        if (activated.IsFailure)
        {
            return Result<BankConnectionCompletedResponse>.Failure(activated.Errors);
        }

        // Un renouvellement remplace l'ancienne connexion du même compte.
        if (connection.ReplacesId is Guid replaced && await connections.FindAsync(replaced, cancellationToken).ConfigureAwait(false) is { Status: BankConnectionStatus.Active } old)
        {
            old.Revoke(user.UserId, now);
            await aggregator.RevokeAsync(old.ProviderReference, cancellationToken).ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        BankConnectionSyncOutcome first = await sync.RunAsync(connection, cancellationToken).ConfigureAwait(false);
        return new BankConnectionCompletedResponse(true, BankConnectionView.ToResponse(connection, clock.GetUtcNow()), first);
    }
}

internal sealed class SyncBankConnectionsHandler(IBankAggregator aggregator, IBankConnectionRepository connections, BankConnectionSync sync, TimeProvider clock)
    : ICommandHandler<SyncBankConnectionsCommand, Result<SyncBankConnectionsResponse>>
{
    public async Task<Result<SyncBankConnectionsResponse>> HandleAsync(SyncBankConnectionsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!aggregator.IsAvailable)
        {
            return BankConnectionErrors.Unavailable;
        }

        SyncBankConnectionsRequest request = command.Request ?? new SyncBankConnectionsRequest();
        DateTimeOffset now = clock.GetUtcNow();
        List<BankConnection> targets;
        if (request.ConnectionId is Guid id)
        {
            if (await connections.FindAsync(id, cancellationToken).ConfigureAwait(false) is not { } one || one.Status == BankConnectionStatus.Pending)
            {
                return BankConnectionErrors.NotFound;
            }

            targets = [one];
        }
        else
        {
            targets = [.. (await connections.ListAsync(cancellationToken).ConfigureAwait(false)).Where(c => request.OnlyDue ? c.IsSyncDue(now) : c.IsActive(now))];
        }

        List<BankConnectionSyncOutcome> outcomes = [];
        foreach (BankConnection connection in targets)
        {
            BankConnectionSyncOutcome outcome = await sync.RunAsync(connection, cancellationToken).ConfigureAwait(false);
            outcomes.Add(outcome);
            if (outcome.Errors.Any(e => e.Code == ConcurrencyErrors.VersionMismatchCode))
            {
                // Le contexte garde l'écriture refusée : les comptes suivants attendront la prochaine synchronisation.
                break;
            }
        }

        return new SyncBankConnectionsResponse(outcomes);
    }
}

internal sealed class RevokeBankConnectionHandler(
    IBankAggregator aggregator,
    IBankConnectionRepository connections,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<RevokeBankConnectionCommand, Result<BankConnectionResponse>>
{
    public async Task<Result<BankConnectionResponse>> HandleAsync(RevokeBankConnectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        BankConnection? connection = await connections.FindAsync(command.ConnectionId, cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return BankConnectionErrors.NotFound;
        }

        DateTimeOffset now = clock.GetUtcNow();
        Result revoked = connection.Revoke(user.UserId, now);
        if (revoked.IsFailure)
        {
            return Result<BankConnectionResponse>.Failure(revoked.Errors);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (aggregator.IsAvailable)
        {
            await aggregator.RevokeAsync(connection.ProviderReference, cancellationToken).ConfigureAwait(false);
        }

        return BankConnectionView.ToResponse(connection, now);
    }
}

/// <summary>
/// La synchronisation d'un compte relié : réservée d'abord (une seule à la fois, même entre plusieurs serveurs), puis
/// les opérations depuis la dernière lecture (avec 7 jours de recouvrement, la banque comptabilisant parfois en retard)
/// ou les 90 derniers jours la première fois. Elles passent par le même import qu'un relevé : les doublons sont écartés.
/// </summary>
internal sealed class BankConnectionSync(IBankAggregator aggregator, BankLineImporter importer, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public const int HistoryDays = 90;

    public const int OverlapDays = 7;

    public async Task<BankConnectionSyncOutcome> RunAsync(BankConnection connection, CancellationToken cancellationToken)
    {
        Result begun = connection.BeginSync(clock.GetUtcNow());
        if (begun.IsFailure)
        {
            return Failed(connection, begun.Errors);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ConcurrencyConflictException)
        {
            return Failed(connection, [ConcurrencyErrors.VersionMismatch]);
        }

        DateOnly today = LegalCalendar.Today(clock);
        DateOnly earliest = today.AddDays(-HistoryDays);
        DateOnly from = connection.LastSyncAt is { } last ? LegalCalendar.DateOf(last).AddDays(-OverlapDays) : earliest;
        from = from < earliest ? earliest : from;
        Result<ParsedBankStatement> fetched = await aggregator.FetchAsync(connection.ProviderReference, from, today, cancellationToken).ConfigureAwait(false);
        if (fetched.IsFailure)
        {
            connection.SyncFailed();
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Failed(connection, fetched.Errors);
        }

        Result<BankImportResponse> imported = await importer.StoreAsync(
            $"{connection.InstitutionName} {connection.AccountDisplay}".Trim(), fetched.Value, cancellationToken).ConfigureAwait(false);
        if (imported.IsFailure)
        {
            connection.SyncFailed();
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Failed(connection, imported.Errors);
        }

        connection.Synced(imported.Value.Imported, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new BankConnectionSyncOutcome(
            connection.Id, connection.InstitutionName, imported.Value.Imported, imported.Value.Duplicates, imported.Value.Proposals, []);
    }

    private static BankConnectionSyncOutcome Failed(BankConnection connection, IEnumerable<Error> errors) =>
        new(connection.Id, connection.InstitutionName, 0, 0, 0, [.. errors.Select(e => new ErrorDetail(e.Code, e.Target))]);
}
