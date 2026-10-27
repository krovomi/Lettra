using System.Text.Json.Serialization;

namespace Lettra.Contracts.Tax;

/// <summary>Nature des sommes versées à un bénéficiaire, au sens de la déclaration des honoraires (DAS 2).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FeeNatureDto>))]
public enum FeeNatureDto
{
    Fees = 1,
    Commissions = 2,
    Brokerage = 3,
    Rebates = 4,
    Copyright = 5,
    InventorRoyalties = 6,
    Other = 7,
}

/// <summary>
/// Un bénéficiaire suivi et ce qu'il a reçu pendant l'exercice : <paramref name="PaidByBank"/> lu sur les relevés
/// (virements vers son IBAN), <paramref name="PaidOutside"/> noté à la main. <paramref name="ToDeclare"/> : le total
/// dépasse le seuil de l'exercice (<c>Threshold</c> de la réponse). <paramref name="SiretMissing"/> : bénéficiaire établi en France sans SIRET, que la déclaration demande.
/// </summary>
public sealed record FeeBeneficiaryRow(
    Guid SupplierId,
    string Name,
    string? Siret,
    string Address,
    FeeNatureDto Nature,
    string Profession,
    decimal PaidByBank,
    decimal PaidOutside,
    decimal Total,
    bool ToDeclare,
    bool SiretMissing,
    bool HasIban,
    long Version);

/// <summary>Un fournisseur du dossier pas encore suivi pour la déclaration des honoraires.</summary>
public sealed record FeeReturnCandidate(Guid SupplierId, string Name);

/// <summary>
/// La déclaration des honoraires d'un exercice : son échéance dans le calendrier fiscal (vide tant qu'aucun bénéficiaire
/// n'est suivi), les bénéficiaires suivis du plus payé au moins payé, et les fournisseurs qu'on peut ajouter.
/// <paramref name="BankLinesUntil"/> : la date de la dernière opération des relevés importés, pour savoir jusqu'où les virements sont connus.
/// </summary>
public sealed record FeeReturnResponse(
    DateOnly FiscalYearStart,
    DateOnly FiscalYearEnd,
    FiscalDeadlineResponse? Deadline,
    decimal Threshold,
    IReadOnlyList<FeeBeneficiaryRow> Rows,
    IReadOnlyList<FeeReturnCandidate> Candidates,
    DateOnly? BankLinesUntil);

/// <summary>Suivre un fournisseur ou mettre à jour son suivi, et noter ce qui lui a été versé hors relevés pendant l'exercice.</summary>
public sealed record SaveFeeBeneficiaryRequest
{
    public FeeNatureDto Nature { get; set; } = FeeNatureDto.Fees;

    public string Profession { get; set; } = string.Empty;

    /// <summary>Le premier jour de l'exercice affiché.</summary>
    public DateOnly FiscalYearStart { get; set; }

    /// <summary>Versé hors relevés (chèque, espèces, compensation) pendant cet exercice, TTC ; zéro si rien.</summary>
    public decimal PaidOutside { get; set; }
}
