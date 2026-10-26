using System.Text.Json.Serialization;

namespace Lettra.Contracts.Insights;

/// <summary>Un écart entre deux sociétés d'un même groupe, à régler avant la clôture.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyGapDto>))]
public enum IntercompanyGapDto
{
    /// <summary>L'acheteur n'a pas le vendeur dans ses fournisseurs (même SIREN) : ses factures n'ont nulle part où arriver.</summary>
    SupplierMissing = 1,

    /// <summary>La fiche fournisseur de l'acheteur porte un autre IBAN que celui du vendeur : un virement partirait ailleurs.</summary>
    SupplierIbanDiffers = 2,

    /// <summary>Le vendeur dit la facture encaissée, mais la banque de l'acheteur ne montre aucun virement qui lui corresponde.</summary>
    PaidNotSeen = 3,

    /// <summary>La banque de l'acheteur montre un virement pour la facture, mais le vendeur l'attend encore.</summary>
    SeenNotPaid = 4,

    /// <summary>Ce que le vendeur dit encaissé et ce que la banque de l'acheteur a payé ne font pas le même total.</summary>
    TotalsDiffer = 5,
}

/// <summary>Une facture du vendeur à l'acheteur sur la période, et ce que chacun en sait.</summary>
/// <param name="InvoiceId">La facture, dans le dossier du vendeur.</param>
/// <param name="Number">Son numéro.</param>
/// <param name="IssueDate">Sa date.</param>
/// <param name="NetInclTax">Son montant TTC, avoirs déduits.</param>
/// <param name="PaidBySeller">Encaissé selon le vendeur (rapprochement ou plateforme).</param>
/// <param name="SeenAtBuyer">Un virement de l'acheteur vers le vendeur lui correspond (numéro dans le libellé, sinon même montant).</param>
/// <param name="Gap">L'écart de la facture, s'il y en a un.</param>
public sealed record IntercompanyInvoiceResponse(
    Guid InvoiceId,
    string Number,
    DateOnly IssueDate,
    decimal NetInclTax,
    decimal PaidBySeller,
    bool SeenAtBuyer,
    IntercompanyGapDto? Gap);

/// <summary>
/// Ce qu'une société du groupe facture à une autre (le vendeur, l'acheteur), et ce que l'acheteur en montre : sa fiche
/// fournisseur et les virements de sa banque vers l'IBAN du vendeur. <paramref name="BuyerBankLinked"/> est faux quand
/// l'acheteur n'a aucune opération bancaire sur la période : les paiements ne sont alors pas comparés.
/// </summary>
public sealed record IntercompanyPairResponse(
    Guid SellerDossierId,
    string SellerName,
    Guid BuyerDossierId,
    string BuyerName,
    decimal InvoicedInclTax,
    decimal PaidBySeller,
    decimal PaidByBuyer,
    bool BuyerBankLinked,
    IReadOnlyList<IntercompanyGapDto> Gaps,
    IReadOnlyList<IntercompanyInvoiceResponse> Invoices)
{
    /// <summary>Ce que le vendeur dit encaissé moins ce que la banque de l'acheteur a payé.</summary>
    public decimal Difference => PaidBySeller - PaidByBuyer;
}

/// <summary>
/// Les comptes entre les sociétés d'un même groupe suivies par le cabinet, sur les douze derniers mois. Deux dossiers
/// forment un couple quand l'un a pour client le SIREN de l'autre ; seuls les dossiers que l'utilisateur peut lire
/// sont rapprochés. Les factures reçues ne sont pas encore enregistrées : le côté de l'acheteur, c'est sa fiche
/// fournisseur et sa banque.
/// </summary>
public sealed record IntercompanyResponse(DateOnly Today, DateOnly Since, int Dossiers, IReadOnlyList<IntercompanyPairResponse> Pairs)
{
    /// <summary>La période rapprochée, en mois avant aujourd'hui.</summary>
    public const int Months = 12;

    /// <summary>En dessous d'un centime, deux montants sont égaux.</summary>
    public const decimal Tolerance = 0.01m;
}
