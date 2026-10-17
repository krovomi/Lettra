namespace Lettra.Contracts.Exports;

/// <summary>Une facture échue et pas encore entièrement encaissée (reste dû TTC, avoirs et encaissements déduits).</summary>
public sealed record OverdueInvoiceResponse(
    Guid InvoiceId,
    string Number,
    string CustomerName,
    DateOnly DueDate,
    int DaysLate,
    decimal OutstandingInclTax);

/// <summary>
/// Le point des créances du dossier pour le tableau de bord : factures en retard (les plus anciennes d'abord) et état
/// des transmissions à la plateforme agréée de toutes les pièces validées. Les encaissements sont ceux déclarés à la
/// plateforme ; une pièce jamais transmise n'a donc aucun encaissement connu.
/// </summary>
/// <param name="Today">Date du jour (Paris) du calcul.</param>
/// <param name="OverdueCount">Factures et acomptes échus avec un reste dû.</param>
/// <param name="OverdueInclTax">Total des restes dus de ces pièces.</param>
/// <param name="Oldest">Les plus en retard, au plus <see cref="ReceivablesSummaryResponse.OldestCount"/>.</param>
/// <param name="NotTransmitted">Pièces validées jamais transmises.</param>
/// <param name="InProgress">Transmissions en cours de cycle (déposée, émise, reçue, approuvée…).</param>
/// <param name="NeedsAttention">Transmissions rejetées, refusées, en litige ou suspendues : une action est attendue.</param>
/// <param name="Collected">Pièces entièrement encaissées.</param>
public sealed record ReceivablesSummaryResponse(
    DateOnly Today,
    int OverdueCount,
    decimal OverdueInclTax,
    IReadOnlyList<OverdueInvoiceResponse> Oldest,
    int NotTransmitted,
    int InProgress,
    int NeedsAttention,
    int Collected)
{
    public const int OldestCount = 5;
}
