namespace Lettra.Supervision.Domain;

/// <summary>Échelle unique de gravité (annexe G §5). La valeur la plus basse est la plus grave.</summary>
public enum Severity
{
    /// <summary>Critique : service arrêté pour au moins un cabinet, conformité ou sécurité compromise. 15 minutes, 24 h sur 24.</summary>
    S1 = 1,

    /// <summary>Majeure : une fonction importante bloquée ou dégradée. 4 heures ouvrées.</summary>
    S2 = 2,

    /// <summary>Mineure : gêne limitée ou risque à échéance. 2 jours ouvrés, résumé quotidien.</summary>
    S3 = 3,

    /// <summary>Information : rien à faire. Résumé hebdomadaire.</summary>
    S4 = 4,
}

/// <summary>Catégorie d'une alerte (annexe G §4).</summary>
public enum AlertCategory
{
    Availability = 1,
    Performance = 2,
    Compliance = 3,
    Security = 4,
    Functional = 5,
    Cost = 6,
}

/// <summary>Cycle de vie : Nouvelle → Prise en charge → Résolue → Clôturée (annexe G §8).</summary>
public enum AlertState
{
    New = 1,
    Acknowledged = 2,
    Resolved = 3,
    Closed = 4,
}
