namespace Lettra.Calculations.Compliance;

/// <summary>Nature de la pièce contrôlée.</summary>
public enum CompliancePieceKind
{
    Quote = 1,
    Invoice = 2,
    DownPayment = 3,
    CreditNote = 4,
}

/// <summary>Gravité d'un constat : une erreur bloque la validation.</summary>
public enum ComplianceSeverity
{
    Error = 1,
    Warning = 2,
    Ok = 3,
}

/// <summary>
/// Émetteur ou client tel que le contrôle le voit (texte plat, comme imprimé). <c>Address</c> est l'adresse complète
/// sur une ligne ; <c>CountryCode</c> un code ISO à 2 lettres (FR par défaut) ; <c>IsBusiness</c> dit si le client
/// est un professionnel (B2B).
/// </summary>
public sealed record ComplianceParty(
    string? Name,
    string? Address,
    string? Siren,
    string? Siret,
    string? VatNumber,
    string? CountryCode,
    string? LegalForm = null,
    decimal? ShareCapital = null,
    string? Rcs = null,
    bool IsBusiness = true);

/// <summary>Une ligne de la pièce.</summary>
public sealed record ComplianceLine(string? Designation, decimal? Quantity, decimal? UnitPriceExclTax, string VatCategory = VatCategoryCodes.Standard);

/// <summary>
/// Ce que le contrôle lit d'une pièce. <paramref name="IsDraft"/> : le numéro et la date d'émission seront attribués
/// à la validation, leur absence n'est pas un manque. <paramref name="LegalMentions"/> : les textes imprimés en pied
/// (mentions de TVA, indemnité de 40 €, option sur les débits).
/// </summary>
public sealed record ComplianceDocument(
    CompliancePieceKind Kind,
    bool IsDraft,
    ComplianceParty Seller,
    ComplianceParty Customer,
    bool SellerVatFranchise,
    bool SellerVatOnDebits,
    string? Number,
    DateOnly? IssueDate,
    DateOnly? DeliveryDate,
    string? DeliveryAddress,
    string? OperationCategory,
    DateOnly? DueDate,
    string? PaymentTerms,
    decimal? LatePenaltyRate,
    string? EarlyPaymentDiscount,
    IReadOnlyList<ComplianceLine> Lines,
    IReadOnlyList<string> LegalMentions,
    string? OriginalInvoiceNumber = null);

/// <summary>Un constat : le code de la mention, sa gravité, sa base légale, la ligne concernée (à partir de 1).</summary>
public sealed record ComplianceFinding(string Code, ComplianceSeverity Severity, string? LegalBasis, int? Line = null);

/// <summary>Résultat du contrôle : la checklist affichée en direct, et le verdict de validation.</summary>
public sealed record ComplianceReport(IReadOnlyList<ComplianceFinding> Findings)
{
    public bool BlocksValidation => Findings.Any(f => f.Severity == ComplianceSeverity.Error);

    public IEnumerable<ComplianceFinding> Errors => Findings.Where(f => f.Severity == ComplianceSeverity.Error);
}

/// <summary>
/// Contrôle déterministe des mentions obligatoires d'une pièce : porté de <c>validate-facture.js</c> (Paperasse,
/// commit f2e15c4) et partagé serveur ↔ WASM, pour que la checklist de l'écran et le verdict du serveur soient le
/// même code (ADR-0006 : l'IA explique, ce contrôle décide). Écarts voulus avec la référence, testés et listés dans
/// <see cref="DeviationsFromReference"/> pour la revue de l'expert-comptable.
/// </summary>
public static class ComplianceChecker
{
    /// <summary>Écarts avec le script de référence : chacun corrige un comportement relevé par le POC (annexe H).</summary>
    public static IReadOnlyList<string> DeviationsFromReference { get; } =
    [
        "RCS : exigé des seules sociétés commerciales (forme SAS, SASU, SARL, EURL, SA, SNC, SCA, SCS), plus d'un entrepreneur individuel.",
        "Forme juridique : toujours exigée (« EI » pour un entrepreneur individuel) ; le capital social l'est des sociétés.",
        "Date de livraison : la mention n'est due que si elle diffère de la date d'émission, et elle est alors saisie ; la référence testait l'adresse.",
        "Indemnité forfaitaire de 40 € : exigée sur les pièces entre professionnels (la référence ne la contrôlait pas).",
        "Client professionnel : déclaré par la fiche du client, plus deviné d'après son nom.",
        "Mentions de TVA : une ligne exonérée, autoliquidée ou hors champ exige le texte légal correspondant.",
        "Avoir : le numéro de la facture d'origine est exigé.",
        "Ligne : une quantité nulle est refusée (la référence n'exigeait qu'une quantité renseignée).",
    ];

    /// <summary>Formes de sociétés commerciales immatriculées au RCS.</summary>
    public static IReadOnlySet<string> CommercialCompanyForms { get; } =
        new HashSet<string>(["SAS", "SASU", "SARL", "EURL", "SA", "SNC", "SCA", "SCS", "SELARL", "SELAS"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Pays de l'Union européenne (codes ISO, la Grèce en GR).</summary>
    public static IReadOnlySet<string> EuCountries { get; } = new HashSet<string>(EuropeanUnion.MemberStates, StringComparer.OrdinalIgnoreCase);

    public static ComplianceReport Check(ComplianceDocument document, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<ComplianceFinding> findings = [];
        bool reformActive = today >= MandatoryMentions.Reform2026;
        bool isQuote = document.Kind == CompliancePieceKind.Quote;
        bool isBusinessCustomer = document.Customer.IsBusiness;
        string country = string.IsNullOrWhiteSpace(document.Customer.CountryCode) ? "FR" : document.Customer.CountryCode.Trim().ToUpperInvariant();
        bool isFrench = country == "FR";
        bool isIntraEu = !isFrench && EuCountries.Contains(country);

        // Émetteur.
        Require(findings, MentionCodes.SellerName, document.Seller.Name);
        Require(findings, MentionCodes.SellerAddress, document.Seller.Address);
        Require(findings, MentionCodes.SellerSiren, Present(document.Seller.Siren) ? document.Seller.Siren : document.Seller.Siret);
        bool isCompany = Present(document.Seller.LegalForm) && CommercialCompanyForms.Contains(document.Seller.LegalForm!.Trim());
        if (isCompany)
        {
            Require(findings, MentionCodes.SellerRcs, document.Seller.Rcs);
        }

        Require(findings, MentionCodes.SellerLegalForm, isCompany && document.Seller.ShareCapital is null ? null : document.Seller.LegalForm);
        if (!document.SellerVatFranchise)
        {
            Require(findings, MentionCodes.SellerVatNumber, document.Seller.VatNumber);
        }

        // Client.
        Require(findings, MentionCodes.CustomerName, document.Customer.Name);
        Require(findings, MentionCodes.CustomerAddress, document.Customer.Address);
        if (isFrench && isBusinessCustomer)
        {
            Require(findings, MentionCodes.CustomerSiren, document.Customer.Siren, reform: !reformActive);
        }

        if (isIntraEu && isBusinessCustomer)
        {
            Require(findings, MentionCodes.CustomerVatNumber, document.Customer.VatNumber);
        }

        // Pièce.
        if (document.IsDraft)
        {
            // Attribués par la validation : la checklist les montre comme acquis.
            findings.Add(Ok(MentionCodes.Number));
            findings.Add(Ok(MentionCodes.IssueDate));
        }
        else
        {
            Require(findings, MentionCodes.Number, document.Number);
            Require(findings, MentionCodes.IssueDate, document.IssueDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (document.DeliveryDate is not null && document.DeliveryDate != document.IssueDate)
        {
            findings.Add(Ok(MentionCodes.DeliveryDate));
        }

        if (!isQuote)
        {
            Require(findings, MentionCodes.OperationCategory, document.OperationCategory, reform: !reformActive);
            if (Present(document.DeliveryAddress))
            {
                findings.Add(Ok(MentionCodes.DeliveryAddress));
            }

            if (document.SellerVatOnDebits)
            {
                Require(findings, MentionCodes.VatOnDebits, document.LegalMentions.FirstOrDefault(m => m.Contains("débits", StringComparison.OrdinalIgnoreCase)), reform: !reformActive);
            }
        }

        // Lignes.
        if (document.Lines.Count == 0)
        {
            findings.Add(new(MentionCodes.Lines, ComplianceSeverity.Error, "Art. 242 nonies A CGI"));
        }
        else
        {
            int before = findings.Count;
            for (int i = 0; i < document.Lines.Count; i++)
            {
                ComplianceLine line = document.Lines[i];
                if (!Present(line.Designation))
                {
                    findings.Add(new(MentionCodes.LineDesignation, ComplianceSeverity.Error, LegalBasis(MentionCodes.LineDesignation), i + 1));
                }

                if (line.Quantity is null or 0m)
                {
                    findings.Add(new(MentionCodes.LineQuantity, ComplianceSeverity.Error, LegalBasis(MentionCodes.LineQuantity), i + 1));
                }

                if (line.UnitPriceExclTax is null)
                {
                    findings.Add(new(MentionCodes.LineUnitPrice, ComplianceSeverity.Error, LegalBasis(MentionCodes.LineUnitPrice), i + 1));
                }
            }

            if (findings.Count == before)
            {
                findings.Add(Ok(MentionCodes.Lines));
            }
        }

        findings.Add(Ok(MentionCodes.Totals));

        // Mentions de TVA : chaque catégorie sans taux a son texte légal.
        bool needsVatText = document.Lines.Any(l => !VatCategoryCodes.IsTaxed(l.VatCategory)) || document.SellerVatFranchise;
        if (needsVatText)
        {
            bool printed = document.LegalMentions.Any(m => m.Contains("CGI", StringComparison.Ordinal) || m.Contains("Code général des impôts", StringComparison.Ordinal));
            findings.Add(printed
                ? Ok(MentionCodes.VatExemptionText)
                : new(MentionCodes.VatExemptionText, ComplianceSeverity.Error, "Art. 242 nonies A, I-13° CGI"));
        }

        if (document.Kind == CompliancePieceKind.CreditNote)
        {
            findings.Add(Present(document.OriginalInvoiceNumber)
                ? Ok(MentionCodes.OriginalInvoice)
                : new(MentionCodes.OriginalInvoice, ComplianceSeverity.Error, "Art. 242 nonies A, I-11° CGI"));
        }

        // Paiement : sans objet pour un devis et un avoir.
        if (document.Kind is CompliancePieceKind.Invoice or CompliancePieceKind.DownPayment)
        {
            findings.Add(document.DueDate is null && !Present(document.PaymentTerms)
                ? new(MentionCodes.DueDate, ComplianceSeverity.Error, LegalBasis(MentionCodes.DueDate))
                : Ok(MentionCodes.DueDate));

            findings.Add(document.LatePenaltyRate is null
                ? new(MentionCodes.LatePenalties, ComplianceSeverity.Warning, LegalBasis(MentionCodes.LatePenalties))
                : Ok(MentionCodes.LatePenalties));

            findings.Add(!Present(document.EarlyPaymentDiscount)
                ? new(MentionCodes.EarlyPaymentDiscount, ComplianceSeverity.Warning, LegalBasis(MentionCodes.EarlyPaymentDiscount))
                : Ok(MentionCodes.EarlyPaymentDiscount));

            if (isBusinessCustomer)
            {
                bool indemnity = document.LegalMentions.Any(m => m.Contains("40", StringComparison.Ordinal) && m.Contains("recouvrement", StringComparison.OrdinalIgnoreCase));
                findings.Add(indemnity
                    ? Ok(MentionCodes.RecoveryIndemnity)
                    : new(MentionCodes.RecoveryIndemnity, ComplianceSeverity.Error, LegalBasis(MentionCodes.RecoveryIndemnity)));
            }
        }

        return new ComplianceReport(findings);
    }

    private static void Require(List<ComplianceFinding> findings, string code, string? value, bool reform = false)
    {
        MandatoryMention mention = MandatoryMentions.ByCode(code);
        if (Present(value))
        {
            findings.Add(Ok(code));
        }
        else if (reform && mention.Since == MentionSince.Reform2026)
        {
            // Mention de la réforme, pas encore en vigueur : on prévient sans bloquer.
            findings.Add(new(code, ComplianceSeverity.Warning, mention.LegalBasis));
        }
        else
        {
            findings.Add(new(code, ComplianceSeverity.Error, mention.LegalBasis));
        }
    }

    private static ComplianceFinding Ok(string code) => new(code, ComplianceSeverity.Ok, null);

    private static string LegalBasis(string code) => MandatoryMentions.ByCode(code).LegalBasis;

    private static bool Present(string? value) => !string.IsNullOrWhiteSpace(value);
}
