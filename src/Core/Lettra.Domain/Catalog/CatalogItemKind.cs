namespace Lettra.Domain.Catalog;

/// <summary>
/// Nature de l'article. Elle décide de la « nature de l'opération » (livraison de biens ou prestation
/// de services), mention obligatoire des factures de la réforme 2026-2027, et de l'exigibilité de la TVA.
/// </summary>
public enum CatalogItemKind
{
    /// <summary>Bien (marchandise, fourniture) : livraison de biens.</summary>
    Good = 1,

    /// <summary>Service (main-d'œuvre, conseil, abonnement) : prestation de services.</summary>
    Service = 2,
}
