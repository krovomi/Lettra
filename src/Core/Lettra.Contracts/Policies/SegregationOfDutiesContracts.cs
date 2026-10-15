namespace Lettra.Contracts.Policies;

/// <summary>
/// Séparation des tâches, option du cabinet (annexe F §3.2). Lue et modifiée par l'administrateur du cabinet.
/// </summary>
/// <param name="Enabled">Active : qui saisit une facture ne l'émet pas ; un avoir au-delà du seuil attend un manager.</param>
/// <param name="CreditNoteApprovalThresholdInclTax">
/// Seuil TTC, en euros, au-delà duquel un avoir doit être approuvé par un manager (permission <c>Avoir.Approuver</c>).
/// <c>null</c> : aucun avoir ne demande d'approbation. <c>0</c> : tous les avoirs en demandent une.
/// </param>
public sealed record SegregationOfDutiesSettings(bool Enabled, decimal? CreditNoteApprovalThresholdInclTax);
