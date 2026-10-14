using System.Text.Json.Serialization;

namespace Lettra.Compliance.Invoices;

/// <summary>
/// Vue d'une facture au format d'échange Paperasse (data/invoices/*.json).
/// Dans Lettra, le domaine projette sa facture vers cette vue pour la contrôler.
/// </summary>
public sealed record InvoiceDocument
{
    [JsonPropertyName("number")] public string? Number { get; init; }
    [JsonPropertyName("date")] public string? Date { get; init; }
    [JsonPropertyName("due_date")] public string? DueDate { get; init; }
    [JsonPropertyName("delivery_date")] public string? DeliveryDate { get; init; }
    [JsonPropertyName("delivery_address")] public string? DeliveryAddress { get; init; }
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("category")] public string? Category { get; init; }
    [JsonPropertyName("client")] public InvoiceClient? Client { get; init; }
    [JsonPropertyName("lines")] public IReadOnlyList<InvoiceLine>? Lines { get; init; }
    [JsonPropertyName("payment")] public InvoicePayment? Payment { get; init; }
}

public sealed record InvoiceClient
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("email")] public string? Email { get; init; }
    [JsonPropertyName("address")] public string? Address { get; init; }
    [JsonPropertyName("postcode")] public string? Postcode { get; init; }
    [JsonPropertyName("city")] public string? City { get; init; }
    [JsonPropertyName("country")] public string? Country { get; init; }
    [JsonPropertyName("siren")] public string? Siren { get; init; }
    [JsonPropertyName("siret")] public string? Siret { get; init; }
    [JsonPropertyName("tva_intracom")] public string? TvaIntracom { get; init; }
}

public sealed record InvoiceLine
{
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("quantity")] public decimal? Quantity { get; init; }
    [JsonPropertyName("unit")] public string? Unit { get; init; }
    [JsonPropertyName("unit_price")] public decimal? UnitPrice { get; init; }
    /// <summary>Extension Lettra : taux par ligne (Paperasse n'a qu'un taux par entreprise).</summary>
    [JsonPropertyName("vat_rate")] public decimal? VatRate { get; init; }
}

public sealed record InvoicePayment
{
    [JsonPropertyName("terms")] public string? Terms { get; init; }
    [JsonPropertyName("method")] public string? Method { get; init; }
    [JsonPropertyName("late_penalty_rate")] public string? LatePenaltyRate { get; init; }
    [JsonPropertyName("escompte_label")] public string? EscompteLabel { get; init; }
}
