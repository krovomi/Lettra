namespace Lettra.Poc.Sync.Contracts;

public enum InvoiceStatus { Draft, Validated }

public sealed record InvoiceLineDto(string Label, decimal Quantity, decimal UnitPriceExclTax, decimal VatRate);

/// <summary>Instantané complet d'une facture, tel qu'il circule entre client et serveur.</summary>
public sealed record InvoiceDto(
    Guid Id,
    Guid DossierId,
    InvoiceStatus Status,
    int Version,
    string CustomerName,
    DateOnly? DueDate,
    string? Notes,
    IReadOnlyList<InvoiceLineDto> Lines,
    string? Number,
    DateOnly? IssueDate,
    decimal TotalExclTax,
    decimal TotalInclTax,
    string? ChainHash);

/// <summary>Champs d'un brouillon que la fusion traite un par un.</summary>
public static class DraftFields
{
    public const string CustomerName = nameof(CustomerName);
    public const string DueDate = nameof(DueDate);
    public const string Notes = nameof(Notes);
    public const string Lines = nameof(Lines);

    public static readonly IReadOnlyList<string> All = [CustomerName, DueDate, Notes, Lines];
}
