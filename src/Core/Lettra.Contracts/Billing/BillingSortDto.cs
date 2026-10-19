using System.Text.Json.Serialization;

namespace Lettra.Contracts.Billing;

/// <summary>
/// Colonne de tri des listes de devis et de factures (paramètre de requête <c>sort</c>). Le tri par défaut reste la date
/// d'émission, la plus récente en tête ; le numéro puis l'identifiant départagent toujours les égalités.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BillingSortDto>))]
public enum BillingSortDto
{
    IssueDate = 1,
    Number = 2,
    Customer = 3,

    /// <summary>Échéance d'une facture, fin de validité d'un devis.</summary>
    DueDate = 4,

    Status = 5,
    TotalExclTax = 6,
    TotalInclTax = 7,
}
