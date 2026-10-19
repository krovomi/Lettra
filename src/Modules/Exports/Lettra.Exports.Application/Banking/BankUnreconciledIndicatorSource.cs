using Lettra.Application.Abstractions.Cabinet;
using Lettra.Contracts.Cabinet;

namespace Lettra.Exports.Application.Banking;

/// <summary>
/// Opérations du relevé encore à rapprocher, pour la vue cabinet : leur nombre et la date de la plus ancienne. À traiter
/// quand la plus ancienne a plus de 15 jours, urgent au-delà de 30 ; en attente sinon. Aucun montant.
/// </summary>
internal sealed class BankUnreconciledIndicatorSource(IBankStatementRepository statements) : IDossierIndicatorSource
{
    public const int WarningAfterDays = 15;
    public const int CriticalAfterDays = 30;

    public async Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        BankReconciliationStatus status = await statements.StatusAsync(cancellationToken).ConfigureAwait(false);
        return [Of(status, today)];
    }

    internal static DossierIndicator Of(BankReconciliationStatus status, DateOnly today)
    {
        if (status.PendingLines == 0)
        {
            return new DossierIndicator(DossierIndicatorKeys.BankUnreconciled, 0, IndicatorLevel.Ok);
        }

        DateOnly oldest = status.OldestPendingDate ?? today;
        IndicatorLevel level = oldest < today.AddDays(-CriticalAfterDays) ? IndicatorLevel.Critical
            : oldest < today.AddDays(-WarningAfterDays) ? IndicatorLevel.Warning
            : IndicatorLevel.Info;
        return new DossierIndicator(DossierIndicatorKeys.BankUnreconciled, status.PendingLines, level, oldest);
    }
}
