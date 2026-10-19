using Lettra.Contracts.Common;
using Lettra.Contracts.Tax;

namespace Lettra.UI.Abstractions;

/// <summary>Un fichier de DES remis par le serveur, à déposer soi-même sur le service en ligne des douanes.</summary>
public sealed record DesDocument(string FileName, byte[] Content);

/// <summary>
/// Déclarations fiscales du dossier : brouillon de TVA (CA3 ou CA12 selon le régime de la fiche entreprise), DES,
/// calendrier fiscal et ses réglages. Lecture avec <c>DossierLire</c> ; fichier de DES, réglages et dépôts marqués
/// avec <c>FecExporter</c>. Refus possibles : <c>Tax.ProfileMissing</c>, <c>Tax.VatFranchise</c>,
/// <c>Tax.RegimeMissing</c>, <c>Tax.PeriodInvalid</c>, <c>Tax.FiscalYearEndInvalid</c>, <c>Tax.DeadlineDayInvalid</c>,
/// <c>Tax.DeadlineUnknown</c>, <c>Concurrency.VersionMismatch</c>.
/// </summary>
public interface ITaxGateway
{
    /// <summary>Brouillon de la période de déclaration qui contient <paramref name="date"/>.</summary>
    Task<OperationResult<VatReturnResponse>> GetVatReturnAsync(DateOnly date, CancellationToken cancellationToken);

    Task<OperationResult<DesResponse>> GetDesAsync(int year, int month, CancellationToken cancellationToken);

    Task<OperationResult<DesDocument>> ExportDesAsync(int year, int month, CancellationToken cancellationToken);

    Task<OperationResult<FiscalCalendarResponse>> GetCalendarAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Échéances en retard ou dues d'ici <paramref name="days"/> jours (vue cabinet).</summary>
    Task<OperationResult<IReadOnlyList<FiscalDeadlineResponse>>> GetUpcomingAsync(int days, CancellationToken cancellationToken);

    Task<OperationResult<FiscalDeadlineResponse>> MarkFilingAsync(MarkFiscalFilingRequest request, CancellationToken cancellationToken);

    Task<OperationResult<FiscalSettingsResponse>> GetSettingsAsync(CancellationToken cancellationToken);

    Task<OperationResult<FiscalSettingsResponse>> UpdateSettingsAsync(UpdateFiscalSettingsRequest request, CancellationToken cancellationToken);
}
