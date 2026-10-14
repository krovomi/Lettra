using System.Text.RegularExpressions;
using Lettra.Compliance.Invoices;

namespace Lettra.Compliance.Mentions;

/// <summary>
/// Port C# de <c>comptable/scripts/validate-facture.js</c> (Paperasse, commit f2e15c4).
/// Le comportement est volontairement identique, écarts compris : les tests de parité le vérifient
/// et les écarts connus de la référence sont listés dans <see cref="KnownReferenceQuirks"/>.
/// </summary>
public sealed partial class InvoiceComplianceValidator(MandatoryMentionCatalog catalog)
{
    /// <summary>Entrée en vigueur des mentions 2026 dans le script de référence.</summary>
    public static readonly DateOnly Reform2026 = new(2026, 9, 1);

    public InvoiceComplianceValidator() : this(MandatoryMentionCatalog.Default) { }

    /// <summary>Comportements du script de référence conservés pour la parité, à arbitrer avec l'expert-comptable.</summary>
    public static readonly IReadOnlyList<string> KnownReferenceQuirks =
    [
        "Les conditions des mentions émetteur (RCS « sociétés commerciales », forme juridique « sociétés ») ne sont pas évaluées : RCS et forme juridique sont exigés aussi d'un entrepreneur individuel.",
        "La condition de la date de livraison (« si différente de la date d'émission ») est évaluée sur l'adresse de livraison, pas sur la date.",
        "L'indemnité forfaitaire de recouvrement (40 EUR) n'est pas contrôlée ; pénalités et escompte absents ne donnent qu'un avertissement.",
        "Le caractère B2B du client est deviné : SIREN présent, ou forme juridique dans le nom (SAS, SARL…).",
        "Les montants ne sont pas recalculés ni contrôlés (« calculés depuis les lignes »).",
    ];

    public ComplianceReport Validate(CompanyProfile company, InvoiceDocument invoice, DateOnly today, bool strict = false)
    {
        var errors = new List<ComplianceFinding>();
        var warnings = new List<ComplianceFinding>();
        var ok = new List<ComplianceFinding>();
        var is2026Active = today >= Reform2026;

        // --- Émetteur (les conditions du référentiel ne sont pas évaluées, comme dans la référence) ---
        foreach (var field in catalog.Section("emetteur"))
        {
            var value = field.Id switch
            {
                "nom" => company.Name,
                "adresse" => company.Address,
                "siren" => Truthy(company.Siren) ? company.Siren : company.Siret,
                "rcs" => company.Rcs,
                "forme_juridique" => company.LegalForm,
                "tva_intracom" => company.IsVatFranchise ? "franchise" : company.TvaIntracom,
                _ => Unmapped,
            };
            if (ReferenceEquals(value, Unmapped)) continue;

            if (Truthy(value)) ok.Add(Present(field, value!));
            else if (field.IsLongstanding) errors.Add(new(field.Id, field.Label, field.LegalBasis));
            else if (is2026Active || strict) errors.Add(new(field.Id, field.Label, field.LegalBasis, field.Since));
            else warnings.Add(new(field.Id, field.Label, Since: field.Since));
        }

        // --- Client ---
        foreach (var field in catalog.Section("client"))
        {
            var value = field.Id switch
            {
                "nom_client" => invoice.Client?.Name,
                "adresse_client" => invoice.Client?.Address,
                "siren_client" => invoice.Client?.Siren,
                "tva_intracom_client" => invoice.Client?.TvaIntracom,
                _ => Unmapped,
            };
            if (ReferenceEquals(value, Unmapped)) continue;
            if (field.Condition is not null && !ConditionApplies(field.Condition, company, invoice)) continue;

            if (Truthy(value)) ok.Add(Present(field, value!));
            else if (!field.IsLongstanding && !is2026Active && !strict) warnings.Add(new(field.Id, field.Label, Since: field.Since));
            else errors.Add(new(field.Id, field.Label, field.LegalBasis));
        }

        // --- Facture ---
        foreach (var field in catalog.Section("facture"))
        {
            var value = field.Id switch
            {
                "numero" => invoice.Number,
                "date_emission" => invoice.Date,
                "date_livraison" => Truthy(invoice.DeliveryDate) ? invoice.DeliveryDate : invoice.Date,
                "categorie_operation" => invoice.Category,
                "adresse_livraison" => invoice.DeliveryAddress,
                "option_debits" => company.Tax?.OptionDebits == true ? "oui" : null,
                _ => Unmapped,
            };
            if (ReferenceEquals(value, Unmapped)) continue;
            if (field.Condition is not null && !ConditionApplies(field.Condition, company, invoice)) continue;

            if (Truthy(value)) ok.Add(Present(field, value!));
            else if (!field.IsLongstanding && !is2026Active && !strict) warnings.Add(new(field.Id, field.Label, Since: field.Since));
            else if (field.Mandatory) errors.Add(new(field.Id, field.Label, field.LegalBasis));
        }

        // --- Lignes ---
        if (invoice.Lines is null || invoice.Lines.Count == 0)
        {
            errors.Add(new("lines", "Aucune ligne de facture", "Art. 242 nonies A CGI"));
        }
        else
        {
            var lineErrors = 0;
            for (var i = 0; i < invoice.Lines.Count; i++)
            {
                var line = invoice.Lines[i];
                if (!Truthy(line.Description)) { errors.Add(new($"line_{i}_desc", $"Ligne {i + 1} : designation manquante")); lineErrors++; }
                if (line.Quantity is null) { errors.Add(new($"line_{i}_qty", $"Ligne {i + 1} : quantite manquante")); lineErrors++; }
                if (line.UnitPrice is null) { errors.Add(new($"line_{i}_pu", $"Ligne {i + 1} : prix unitaire manquant")); lineErrors++; }
            }
            if (lineErrors == 0) ok.Add(new("lines", "Lignes de facture", Value: $"{invoice.Lines.Count} ligne(s)"));
        }

        // --- Montants (non recalculés par la référence) ---
        ok.Add(new("montants", "Montants HT/TVA/TTC", Value: "calcules depuis les lignes"));

        // --- Paiement ---
        if (!Truthy(invoice.DueDate) && !Truthy(invoice.Payment?.Terms))
            errors.Add(new("date_echeance", "Date d'echeance ou conditions de paiement", "Art. L441-9 C.com"));
        else
            ok.Add(new("paiement", "Conditions de paiement", Value: Truthy(invoice.Payment?.Terms) ? invoice.Payment!.Terms : invoice.DueDate));

        // --- Pénalités ---
        if (company.Payment?.LatePenaltyRate is null && invoice.Payment?.LatePenaltyRate is null)
            warnings.Add(new("penalites", "Taux de penalites de retard non specifie (defaut : taux legal)", "Art. L441-10 C.com"));

        // --- Franchise en base ---
        if (company.IsVatFranchise)
            ok.Add(new("mention_franchise", "Mention franchise TVA", Value: "art. 293 B du CGI"));

        // --- Escompte ---
        if (!Truthy(company.Payment?.EscompteLabel) && !Truthy(invoice.Payment?.EscompteLabel))
            warnings.Add(new("escompte", "Conditions d'escompte non specifiees (\"Pas d'escompte...\" si non applicable)", "Art. L441-9 C.com"));
        else
            ok.Add(new("escompte", "Mention escompte", Value: "presente"));

        return new ComplianceReport(errors, warnings, ok);
    }

    private static readonly string Unmapped = new('\0', 1);

    private static readonly HashSet<string> EuCountries =
    [
        "BE", "BG", "CZ", "DK", "DE", "EE", "IE", "GR", "ES", "FR", "HR", "IT", "CY", "LV", "LT", "LU",
        "HU", "MT", "NL", "AT", "PL", "PT", "RO", "SI", "SK", "FI", "SE",
    ];

    private static readonly HashSet<string> CompanyLegalForms = ["SASU", "SAS", "SARL", "EURL", "SA", "SCI"];

    // ECMAScript : \b et \w ASCII, comme le moteur JavaScript de la référence.
    [GeneratedRegex(@"\b(SAS|SASU|SARL|EURL|SA|SCI|GmbH|Ltd|LLC|Inc|Corp|BV|AB|AG|SpA)\b",
        RegexOptions.IgnoreCase | RegexOptions.ECMAScript)]
    private static partial Regex BusinessFormInName();

    /// <summary>Port de <c>shouldApplyCondition</c> : détection par mots-clés dans le libellé de la condition.</summary>
    public static bool ConditionApplies(string condition, CompanyProfile company, InvoiceDocument invoice)
    {
        var c = condition.ToLowerInvariant();
        var client = invoice.Client ?? new InvoiceClient();
        var country = (Truthy(client.Country) ? client.Country! : "FR").ToUpperInvariant();
        var isFr = country == "FR";
        var isIntraEu = EuCountries.Contains(country) && !isFr;
        var looksLikeBusiness = Truthy(client.Name) && BusinessFormInName().IsMatch(client.Name!);
        var isB2B = Truthy(client.Siren) || looksLikeBusiness;

        if (c.Contains("intra-ue") || c.Contains("intracommunautaire")) return isIntraEu && isB2B;
        if (c.Contains("b2b domestique")) return isFr && isB2B;
        if (c.Contains("b2b")) return isB2B;
        if (c.Contains("differente") || c.Contains("différente"))
            return Truthy(invoice.DeliveryAddress) && invoice.DeliveryAddress != client.Address;
        if (c.Contains("debits") || c.Contains("débits")) return company.Tax?.OptionDebits == true;
        if (c.Contains("societe") || c.Contains("société"))
            return Truthy(company.LegalForm) && CompanyLegalForms.Contains(company.LegalForm!);
        return true;
    }

    private static bool Truthy(string? value) => !string.IsNullOrEmpty(value);

    private static ComplianceFinding Present(MandatoryMention field, string value) =>
        new(field.Id, field.Label, Value: JsSubstring50(value));

    /// <summary>Équivalent de <c>String(val).substring(0, 50)</c> (unités UTF-16, comme JavaScript).</summary>
    private static string JsSubstring50(string value) => value.Length <= 50 ? value : value[..50];
}
