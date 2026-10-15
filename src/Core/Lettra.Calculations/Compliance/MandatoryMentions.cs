namespace Lettra.Calculations.Compliance;

/// <summary>
/// Codes des mentions obligatoires, repris à l'identique dans <c>Lettra.Contracts.ErrorCodes.Mention</c> (vérifié
/// par un test) : l'UI les traduit, le serveur renvoie le même code quand une mention bloque la validation.
/// </summary>
public static class MentionCodes
{
    public const string SellerName = "Mention.SellerName";
    public const string SellerAddress = "Mention.SellerAddress";
    public const string SellerSiren = "Mention.SellerSiren";
    public const string SellerRcs = "Mention.SellerRcs";
    public const string SellerLegalForm = "Mention.SellerLegalForm";
    public const string SellerVatNumber = "Mention.SellerVatNumber";
    public const string CustomerName = "Mention.CustomerName";
    public const string CustomerAddress = "Mention.CustomerAddress";
    public const string CustomerSiren = "Mention.CustomerSiren";
    public const string CustomerVatNumber = "Mention.CustomerVatNumber";
    public const string Number = "Mention.Number";
    public const string IssueDate = "Mention.IssueDate";
    public const string DeliveryDate = "Mention.DeliveryDate";
    public const string OperationCategory = "Mention.OperationCategory";
    public const string DeliveryAddress = "Mention.DeliveryAddress";
    public const string VatOnDebits = "Mention.VatOnDebits";
    public const string Lines = "Mention.Lines";
    public const string LineDesignation = "Mention.LineDesignation";
    public const string LineQuantity = "Mention.LineQuantity";
    public const string LineUnitPrice = "Mention.LineUnitPrice";
    public const string Totals = "Mention.Totals";
    public const string DueDate = "Mention.DueDate";
    public const string EarlyPaymentDiscount = "Mention.EarlyPaymentDiscount";
    public const string LatePenalties = "Mention.LatePenalties";
    public const string RecoveryIndemnity = "Mention.RecoveryIndemnity";
    public const string VatExemptionText = "Mention.VatExemptionText";
    public const string OriginalInvoice = "Mention.OriginalInvoice";
}

/// <summary>Depuis quand une mention est exigée.</summary>
public enum MentionSince
{
    /// <summary>Mention exigée de longue date (« existant » dans le référentiel).</summary>
    Longstanding = 1,

    /// <summary>Mention de la réforme de la facturation électronique, exigée à partir du 1er septembre 2026.</summary>
    Reform2026 = 2,
}

/// <summary>Une mention du référentiel : son code Lettra, l'identifiant Paperasse d'origine, sa base légale.</summary>
public sealed record MandatoryMention(string Code, string PaperasseId, string Section, string LegalBasis, MentionSince Since, string? Condition);

/// <summary>Une mention spéciale imprimée selon le régime de TVA (identifiant Paperasse, texte légal).</summary>
public sealed record SpecialMention(string PaperasseId, string Text);

/// <summary>
/// Référentiel des mentions obligatoires, porté en C# depuis Paperasse
/// (<c>comptable/data/facturation/mentions-obligatoires.json</c>, version 2026-04-15, commit f2e15c4). Un test de parité
/// compare ce tableau au fichier vendu dans <c>Lettra.Ai.Skills</c> : toute mise à jour du référentiel le fait échouer.
/// </summary>
public static class MandatoryMentions
{
    public const string ReferenceVersion = "2026-04-15";

    /// <summary>Entrée en vigueur des mentions de la réforme (référentiel : « depuis 2026-09-01 »).</summary>
    public static readonly DateOnly Reform2026 = new(2026, 9, 1);

    public static IReadOnlyList<MandatoryMention> All { get; } =
    [
        new(MentionCodes.SellerName, "nom", "emetteur", "Art. 242 nonies A, I-1° CGI", MentionSince.Longstanding, null),
        new(MentionCodes.SellerAddress, "adresse", "emetteur", "Art. 242 nonies A, I-1° CGI", MentionSince.Longstanding, null),
        new(MentionCodes.SellerSiren, "siren", "emetteur", "Art. 242 nonies A, I-1° CGI", MentionSince.Longstanding, null),
        new(MentionCodes.SellerRcs, "rcs", "emetteur", "Code de commerce", MentionSince.Longstanding, "sociétés commerciales"),
        new(MentionCodes.SellerLegalForm, "forme_juridique", "emetteur", "Code de commerce", MentionSince.Longstanding, "sociétés"),
        new(MentionCodes.SellerVatNumber, "tva_intracom", "emetteur", "Art. 242 nonies A, I-2° CGI", MentionSince.Longstanding, "assujetti redevable (pas franchise)"),
        new(MentionCodes.CustomerName, "nom_client", "client", "Art. 242 nonies A, I-3° CGI", MentionSince.Longstanding, null),
        new(MentionCodes.CustomerAddress, "adresse_client", "client", "Art. 242 nonies A, I-3° CGI", MentionSince.Longstanding, null),
        new(MentionCodes.CustomerSiren, "siren_client", "client", "Réforme 2026", MentionSince.Reform2026, "B2B domestique"),
        new(MentionCodes.CustomerVatNumber, "tva_intracom_client", "client", "Art. 242 nonies A, I-4° CGI", MentionSince.Longstanding, "opération intra-UE"),
        new(MentionCodes.Number, "numero", "facture", "Art. 242 nonies A, I-5° CGI", MentionSince.Longstanding, null),
        new(MentionCodes.IssueDate, "date_emission", "facture", "Art. 242 nonies A, I-6° CGI", MentionSince.Longstanding, null),
        new(MentionCodes.DeliveryDate, "date_livraison", "facture", "Art. 242 nonies A, I-7° CGI", MentionSince.Longstanding, "si différente de la date d'émission"),
        new(MentionCodes.OperationCategory, "categorie_operation", "facture", "Réforme 2026", MentionSince.Reform2026, null),
        new(MentionCodes.DeliveryAddress, "adresse_livraison", "facture", "Réforme 2026", MentionSince.Reform2026, "si différente de l'adresse de facturation"),
        new(MentionCodes.VatOnDebits, "option_debits", "facture", "Réforme 2026", MentionSince.Reform2026, "si l'entreprise a opté pour les débits"),
        new(MentionCodes.LineDesignation, "designation", "lignes", "Art. 242 nonies A, I-8° CGI", MentionSince.Longstanding, null),
        new(MentionCodes.LineQuantity, "quantite", "lignes", "Art. 242 nonies A, I-9° CGI", MentionSince.Longstanding, null),
        new(MentionCodes.LineUnitPrice, "prix_unitaire_ht", "lignes", "Art. 242 nonies A, I-10° CGI", MentionSince.Longstanding, null),
        new(MentionCodes.Totals, "total_ht", "montants", "Art. 242 nonies A, I-11° CGI", MentionSince.Longstanding, null),
        new(MentionCodes.Totals, "taux_tva", "montants", "Art. 242 nonies A, I-12° CGI", MentionSince.Longstanding, "si redevable TVA"),
        new(MentionCodes.Totals, "montant_tva", "montants", "Art. 242 nonies A, I-12° CGI", MentionSince.Longstanding, "si redevable TVA"),
        new(MentionCodes.Totals, "total_ttc", "montants", "Art. 242 nonies A, I-13° CGI", MentionSince.Longstanding, null),
        new(MentionCodes.DueDate, "date_echeance", "paiement", "Art. L441-9 C.com", MentionSince.Longstanding, null),
        new(MentionCodes.EarlyPaymentDiscount, "conditions_escompte", "paiement", "Art. L441-9 C.com", MentionSince.Longstanding, null),
        new(MentionCodes.LatePenalties, "penalites_retard", "paiement", "Art. L441-10 C.com", MentionSince.Longstanding, null),
        new(MentionCodes.RecoveryIndemnity, "indemnite_recouvrement", "paiement", "Art. L441-10, D441-5 C.com", MentionSince.Longstanding, null),
    ];

    /// <summary>Mentions spéciales du référentiel, imprimées selon le régime de TVA.</summary>
    public static IReadOnlyList<SpecialMention> Special { get; } =
    [
        new("franchise_tva", "TVA non applicable, article 293 B du Code général des impôts"),
        new("autoliquidation_intra_ue", "Autoliquidation - TVA due par le preneur, article 283-2 du CGI"),
        new("export", "Exonération de TVA, article 262 I du CGI"),
        new("livraison_intra_ue", "Exonération de TVA, article 262 ter I du CGI"),
        new("sous_traitance_btp", "Autoliquidation de la TVA par le preneur, article 283 2 nonies du CGI"),
        new("autofacturation", "Autofacturation"),
    ];

    /// <summary>Texte d'une mention spéciale par son identifiant Paperasse.</summary>
    public static string SpecialText(string paperasseId) => Special.First(s => s.PaperasseId == paperasseId).Text;

    public static MandatoryMention ByCode(string code) => All.First(m => m.Code == code);

    /// <summary>Montant de l'indemnité forfaitaire pour frais de recouvrement (art. D441-5 C.com), en euros.</summary>
    public const decimal RecoveryIndemnityAmount = 40m;
}
