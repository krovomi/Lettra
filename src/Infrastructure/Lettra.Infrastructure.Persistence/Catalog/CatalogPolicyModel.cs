namespace Lettra.Infrastructure.Persistence.Catalog;

/// <summary>
/// Politiques d'un cabinet réglées par son administrateur (annexe F §3.2). Une ligne par cabinet, créée au premier
/// réglage : sans ligne, les options sont à leur valeur par défaut (séparation des tâches inactive).
/// </summary>
public sealed class CatalogOrganisationPolicy
{
    public Guid OrganisationId { get; set; }

    /// <summary>Qui saisit une facture ne l'émet pas ; un avoir au-delà du seuil attend un manager.</summary>
    public bool SegregationOfDutiesEnabled { get; set; }

    /// <summary>Seuil TTC en euros ; <c>null</c> : aucun avoir n'attend d'approbation.</summary>
    public decimal? CreditNoteApprovalThresholdInclTax { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Administrateur qui a fait le dernier réglage (trace d'audit minimale, le détail va au journal d'audit).</summary>
    public Guid UpdatedBy { get; set; }
}
