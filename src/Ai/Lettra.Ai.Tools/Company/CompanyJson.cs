using System.Globalization;
using System.Text.Json.Nodes;
using Lettra.Application.Abstractions.Ai;
using Lettra.Domain.SharedKernel;

namespace Lettra.Ai.Tools.Company;

/// <summary>
/// Paramètres du dossier utiles à l'assistant : la fiche de l'entreprise (l'émetteur des pièces), ses choix fiscaux et
/// ses séries de numérotation. Tous facultatifs sauf le nom : ce qui manque est signalé au moteur, qui le demande.
/// </summary>
internal sealed record CompanyFacts
{
    public required string Name { get; init; }

    public string? LegalForm { get; init; }

    public decimal? ShareCapital { get; init; }

    public string? Siren { get; init; }

    public string? Siret { get; init; }

    public string? Rcs { get; init; }

    public string? Naf { get; init; }

    public string? VatNumber { get; init; }

    public string? AddressLine1 { get; init; }

    public string? AddressLine2 { get; init; }

    public string? PostalCode { get; init; }

    public string? City { get; init; }

    public string? CountryCode { get; init; }

    /// <summary>Franchise en base (art. 293 B du CGI) ; prime sur <see cref="VatRegime"/>.</summary>
    public bool VatFranchise { get; init; }

    /// <summary>Régime de TVA au format Paperasse (<c>reel_normal</c>, <c>reel_simplifie</c>, <c>mini_reel</c>), s'il est connu.</summary>
    public string? VatRegime { get; init; }

    /// <summary>Régime d'imposition des bénéfices (<c>IS</c> ou <c>IR</c>), s'il est connu.</summary>
    public string? IncomeTaxRegime { get; init; }

    public bool VatOnDebits { get; init; }

    public int? PaymentDays { get; init; }

    public string? PaymentTerms { get; init; }

    /// <summary>Taux annuel des pénalités de retard en pourcentage ; <c>null</c> : trois fois le taux légal.</summary>
    public decimal? LatePenaltyRate { get; init; }

    public string? EarlyPaymentDiscount { get; init; }

    public string? Iban { get; init; }

    public string? Bic { get; init; }

    public string? InvoicePrefix { get; init; }

    public string? CreditNotePrefix { get; init; }

    public string? QuotePrefix { get; init; }

    /// <summary>Valeurs à pseudonymiser avant tout envoi au moteur.</summary>
    public IEnumerable<KnownIdentity> Identities()
    {
        yield return new KnownIdentity(IdentityKind.Company, Name);
        if (Address() is { } address)
        {
            yield return new KnownIdentity(IdentityKind.Address, address);
        }

        if (AddressLine1 is { Length: > 0 } line1)
        {
            yield return new KnownIdentity(IdentityKind.Address, line1);
        }

        foreach ((IdentityKind kind, string? value) in new[]
        {
            (IdentityKind.Siren, Siren),
            (IdentityKind.Siret, Siret),
            (IdentityKind.VatNumber, VatNumber),
            (IdentityKind.Iban, Iban),
        })
        {
            if (value is { Length: > 0 })
            {
                yield return new KnownIdentity(kind, value);
            }
        }
    }

    /// <summary>Adresse sur une ligne, comme le champ <c>address</c> de company.json.</summary>
    public string? Address()
    {
        string town = string.Join(' ', new[] { PostalCode, City }.Where(s => !string.IsNullOrWhiteSpace(s)));
        string[] parts = [.. new[] { AddressLine1, AddressLine2, town }.Where(s => !string.IsNullOrWhiteSpace(s))!];
        if (CountryCode is { Length: 2 } country && !country.Equals("FR", StringComparison.OrdinalIgnoreCase))
        {
            parts = [.. parts, country.ToUpperInvariant()];
        }

        return parts.Length == 0 ? null : string.Join(", ", parts);
    }
}

/// <summary>Source des paramètres du dossier, lue avec les droits de l'utilisateur.</summary>
internal interface ICompanyFactsSource
{
    /// <summary>Les paramètres du dossier, ou <c>null</c> s'ils ne sont pas renseignés (ou pas lisibles par l'utilisateur).</summary>
    Task<CompanyFacts?> GetAsync(DossierId dossier, CancellationToken cancellationToken);
}

/// <summary>Aucun paramètre renseigné : l'assistant le sait et demande ce qui lui manque (tests, hôtes sans module Facturation).</summary>
internal sealed class NoCompanyFacts : ICompanyFactsSource
{
    public Task<CompanyFacts?> GetAsync(DossierId dossier, CancellationToken cancellationToken) => Task.FromResult<CompanyFacts?>(null);
}

/// <summary>
/// Le <c>company.json</c> attendu par le skill <c>comptable</c> de Paperasse (« company.json virtuel », annexe B §3.2 et
/// surcouche <c>company-json-mapping.md</c>). Seuls les champs connus sont écrits ; <c>_a_completer</c> liste ceux qui
/// manquent pour qu'une facture soit conforme, afin que le moteur les demande au lieu de les inventer.
/// </summary>
internal static class CompanyJsonBuilder
{
    /// <summary>Indemnité forfaitaire de recouvrement (art. D441-5 du Code de commerce).</summary>
    public const decimal RecoveryFee = 40m;

    public const string NotFilledNote = "Paramètres de l'entreprise non encore renseignés dans Lettra : demander à l'utilisateur ce qui manque.";

    public static JsonObject Build(CompanyFacts? facts)
    {
        if (facts is null)
        {
            return new JsonObject { ["_note"] = NotFilledNote };
        }

        JsonObject json = new() { ["name"] = facts.Name };
        Set(json, "legal_form", facts.LegalForm);
        if (facts.ShareCapital is { } capital)
        {
            json["capital"] = capital;
        }

        Set(json, "address", facts.Address());
        Set(json, "siren", facts.Siren);
        Set(json, "siret", facts.Siret);
        Set(json, "rcs", facts.Rcs);
        Set(json, "naf", facts.Naf);
        Set(json, "tva_intracom", facts.VatNumber);

        JsonObject tax = new() { ["option_debits"] = facts.VatOnDebits };
        Set(tax, "regime_tva", facts.VatFranchise ? "franchise" : facts.VatRegime);
        Set(tax, "regime_is", facts.IncomeTaxRegime);
        json["tax"] = tax;

        JsonObject payment = new() { ["recovery_fee"] = RecoveryFee };
        if (facts.PaymentDays is { } days)
        {
            payment["default_terms"] = days == 0 ? "net_0" : string.Create(CultureInfo.InvariantCulture, $"net_{days}");
            payment["default_terms_label"] = facts.PaymentTerms is { Length: > 0 } terms
                ? terms
                : days == 0 ? "À réception" : string.Create(CultureInfo.InvariantCulture, $"{days} jours date de facture");
        }
        else
        {
            Set(payment, "default_terms_label", facts.PaymentTerms);
        }

        if (facts.LatePenaltyRate is { } rate)
        {
            payment["late_penalty_rate"] = rate;
        }
        else
        {
            payment["late_penalty_rate"] = "3x_legal";
        }

        Set(payment, "escompte_label", facts.EarlyPaymentDiscount);
        if (facts.Iban is { Length: > 0 } || facts.Bic is { Length: > 0 })
        {
            JsonObject bank = [];
            Set(bank, "iban", facts.Iban);
            Set(bank, "bic", facts.Bic);
            payment["bank_details"] = bank;
        }

        json["payment"] = payment;

        JsonObject invoicing = [];
        Set(invoicing, "prefix", facts.InvoicePrefix);
        Set(invoicing, "avoir_prefix", facts.CreditNotePrefix);
        Set(invoicing, "devis_prefix", facts.QuotePrefix);
        if (invoicing.Count > 0)
        {
            json["invoicing"] = invoicing;
        }

        List<string> missing = Missing(facts);
        if (missing.Count > 0)
        {
            json["_a_completer"] = new JsonArray([.. missing.Select(m => JsonValue.Create(m))]);
        }

        return json;
    }

    /// <summary>Mentions obligatoires de l'émetteur absentes de la fiche (les autres champs sont facultatifs).</summary>
    private static List<string> Missing(CompanyFacts facts)
    {
        List<string> missing = [];
        if (string.IsNullOrWhiteSpace(facts.LegalForm))
        {
            missing.Add("legal_form");
        }

        if (string.IsNullOrWhiteSpace(facts.Siren) && string.IsNullOrWhiteSpace(facts.Siret))
        {
            missing.Add("siren");
        }

        if (facts.Address() is null)
        {
            missing.Add("address");
        }

        if (!facts.VatFranchise && string.IsNullOrWhiteSpace(facts.VatNumber))
        {
            missing.Add("tva_intracom");
        }

        if (!facts.VatFranchise && string.IsNullOrWhiteSpace(facts.VatRegime))
        {
            missing.Add("tax.regime_tva");
        }

        return missing;
    }

    private static void Set(JsonObject json, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            json[key] = value.Trim();
        }
    }
}
