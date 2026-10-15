using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Microsoft.Extensions.Logging;

namespace Lettra.Application.Features.Integrity;

/// <summary>
/// Contrôle d'intégrité et clôtures du dossier courant, partagés par les cas d'usage (avec les droits de
/// l'utilisateur) et par le Worker (passe de nuit sur tous les dossiers). Chaque contrôle publie les deux règles
/// d'alerte de dossier : déclenchée si le constat existe, levée sinon.
/// </summary>
public sealed partial class DossierIntegrityService(
    ITenantContext tenant,
    IIntegrityStore store,
    IIntegrityKeyring keyring,
    IJournaledDocumentReader documents,
    IDossierAlertPublisher alerts,
    ClosingSchedule schedule,
    TimeProvider clock,
    ILogger<DossierIntegrityService> logger)
{
    public async Task<Result<IntegrityReport>> VerifyAsync(CancellationToken cancellationToken)
    {
        TenantDescriptor dossier = tenant.Current;
        Result<IIntegrityKeyVerifier> keys = await keyring.VerifierAsync(cancellationToken).ConfigureAwait(false);
        if (keys.IsFailure)
        {
            return Result<IntegrityReport>.Failure(keys.Errors);
        }

        IReadOnlyList<PeriodClosing> closings = await store.ListClosingsAsync(cancellationToken).ConfigureAwait(false);
        IntegrityVerification verification = new(dossier.DossierId, closings, keys.Value);
        await foreach (JournalEntry entry in store.ReadEntriesAsync(cancellationToken).ConfigureAwait(false))
        {
            verification.Check(entry);
        }

        IReadOnlyDictionary<string, long> series = await store.NextNumbersAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<Guid, string> current = documents.IsConnected && verification.JournaledDocuments.Count > 0
            ? await documents.ComputeContentHashesAsync([.. verification.JournaledDocuments.Keys], cancellationToken).ConfigureAwait(false)
            : new Dictionary<Guid, string>();
        IntegrityReport report = verification.Complete(series, current, documents.IsConnected, clock.GetUtcNow());

        LogChecked(logger, dossier.DossierId.Value, report.EntriesChecked, report.ClosingsChecked, report.Findings.Count,
            string.Join(',', report.Findings.Select(f => f.Kind).Distinct()));

        await alerts.PublishAsync(
            new DossierAlertSignal(DossierAlertRule.IntegrityChainCompromised, dossier.OrganisationId, dossier.DossierId, report.ChainCompromised, report.CheckedAt),
            cancellationToken).ConfigureAwait(false);
        await alerts.PublishAsync(
            new DossierAlertSignal(DossierAlertRule.NumberingGap, dossier.OrganisationId, dossier.DossierId, report.HasNumberingGap, report.CheckedAt),
            cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>Clôture, dans l'ordre, chaque mois écoulé et pas encore clôturé.</summary>
    public async Task<Result<IReadOnlyList<PeriodClosing>>> CloseDuePeriodsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PeriodClosing> existing = await store.ListClosingsAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset? firstEntry = await store.FirstEntryAtAsync(cancellationToken).ConfigureAwait(false);
        PeriodClosing? last = existing.MaxBy(c => c.Number);
        List<PeriodClosing> created = [];
        foreach (ClosingPeriod period in schedule.DueMonths(last?.PeriodEnd, firstEntry, clock.GetUtcNow()))
        {
            Result<PeriodClosing> closing = await store.CloseAsync(period, cancellationToken).ConfigureAwait(false);
            if (closing.IsFailure)
            {
                return Result<IReadOnlyList<PeriodClosing>>.Failure(closing.Errors);
            }

            created.Add(closing.Value);
        }

        if (created.Count > 0)
        {
            LogClosed(logger, tenant.Current.DossierId.Value, created.Count);
        }

        return created;
    }

    /// <summary>
    /// Fait tourner les clés si la version en vigueur a plus de <paramref name="maxAge"/> (passe de nuit). Rien tant
    /// que le dossier n'a pas de clé : elle naît avec la première entrée.
    /// </summary>
    public async Task<Result<IntegrityKeyVersion?>> RotateKeysIfDueAsync(TimeSpan maxAge, CancellationToken cancellationToken)
    {
        IReadOnlyList<IntegrityKeyVersion> versions = await keyring.ListAsync(cancellationToken).ConfigureAwait(false);
        if (versions.Count == 0 || versions[^1].CreatedAt + maxAge > clock.GetUtcNow())
        {
            return Result<IntegrityKeyVersion?>.Success(null);
        }

        Result<IntegrityKeyVersion> rotated = await store.RotateKeysAsync(cancellationToken).ConfigureAwait(false);
        if (rotated.IsFailure)
        {
            return Result<IntegrityKeyVersion?>.Failure(rotated.Errors);
        }

        LogRotated(logger, tenant.Current.DossierId.Value, rotated.Value.Version);
        return rotated.Value;
    }

    [LoggerMessage(EventId = 5000, Level = LogLevel.Information,
        Message = "Intégrité du dossier {DossierId} : {Entries} entrées, {Closings} clôtures, {Findings} constat(s) [{Kinds}]")]
    private static partial void LogChecked(ILogger logger, Guid dossierId, long entries, int closings, int findings, string kinds);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Dossier {DossierId} : {Count} période(s) clôturée(s)")]
    private static partial void LogClosed(ILogger logger, Guid dossierId, int count);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "Dossier {DossierId} : clés de la chaîne passées en version {Version}")]
    private static partial void LogRotated(ILogger logger, Guid dossierId, int version);
}

/// <summary>Tant que le module des pièces n'est pas branché, le contrôle ne compare pas les pièces.</summary>
internal sealed class NotConnectedDocumentReader : IJournaledDocumentReader
{
    public bool IsConnected => false;

    public Task<IReadOnlyDictionary<Guid, string>> ComputeContentHashesAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

/// <summary>Sans module Supervision composé, les signaux ne vont nulle part (hôtes de test).</summary>
internal sealed class NoDossierAlertPublisher : IDossierAlertPublisher
{
    public Task PublishAsync(DossierAlertSignal signal, CancellationToken cancellationToken) => Task.CompletedTask;
}
