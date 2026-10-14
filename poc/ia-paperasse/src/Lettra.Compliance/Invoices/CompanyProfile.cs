using System.Text.Json.Serialization;

namespace Lettra.Compliance.Invoices;

/// <summary>
/// Paramètres de l'émetteur au format company.json de Paperasse.
/// Dans Lettra, ils sont générés à partir des paramètres du dossier (« company.json virtuel », annexe B §3.2).
/// </summary>
public sealed record CompanyProfile
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("legal_form")] public string? LegalForm { get; init; }
    [JsonPropertyName("capital")] public decimal? Capital { get; init; }
    [JsonPropertyName("address")] public string? Address { get; init; }
    [JsonPropertyName("siren")] public string? Siren { get; init; }
    [JsonPropertyName("siret")] public string? Siret { get; init; }
    [JsonPropertyName("rcs")] public string? Rcs { get; init; }
    [JsonPropertyName("naf")] public string? Naf { get; init; }
    [JsonPropertyName("tva_intracom")] public string? TvaIntracom { get; init; }
    [JsonPropertyName("tax")] public CompanyTax? Tax { get; init; }
    [JsonPropertyName("payment")] public CompanyPayment? Payment { get; init; }
    [JsonPropertyName("invoicing")] public CompanyInvoicing? Invoicing { get; init; }
    [JsonPropertyName("einvoicing")] public CompanyEInvoicing? EInvoicing { get; init; }

    public bool IsVatFranchise => Tax?.RegimeTva == "franchise";
}

public sealed record CompanyTax
{
    [JsonPropertyName("regime_tva")] public string? RegimeTva { get; init; }
    [JsonPropertyName("regime_is")] public string? RegimeIs { get; init; }
    [JsonPropertyName("tva_rate")] public decimal? TvaRate { get; init; }
    [JsonPropertyName("option_debits")] public bool? OptionDebits { get; init; }
}

public sealed record CompanyPayment
{
    [JsonPropertyName("default_terms_label")] public string? DefaultTermsLabel { get; init; }
    [JsonPropertyName("late_penalty_rate")] public string? LatePenaltyRate { get; init; }
    [JsonPropertyName("late_penalty_label")] public string? LatePenaltyLabel { get; init; }
    [JsonPropertyName("escompte_label")] public string? EscompteLabel { get; init; }
    [JsonPropertyName("recovery_fee")] public decimal? RecoveryFee { get; init; }
    [JsonPropertyName("bank_details")] public BankDetails? BankDetails { get; init; }
}

public sealed record BankDetails
{
    [JsonPropertyName("iban")] public string? Iban { get; init; }
    [JsonPropertyName("bic")] public string? Bic { get; init; }
}

public sealed record CompanyInvoicing
{
    [JsonPropertyName("prefix")] public string? Prefix { get; init; }
    [JsonPropertyName("avoir_prefix")] public string? AvoirPrefix { get; init; }
}

public sealed record CompanyEInvoicing
{
    [JsonPropertyName("pa")] public string? Pa { get; init; }
    [JsonPropertyName("pa_name")] public string? PaName { get; init; }
    [JsonPropertyName("reception_ready")] public bool? ReceptionReady { get; init; }
    [JsonPropertyName("emission_ready")] public bool? EmissionReady { get; init; }
    [JsonPropertyName("ereporting_ready")] public bool? EReportingReady { get; init; }
}
