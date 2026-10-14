using Lettra.Poc.Sync.Contracts;

namespace Lettra.Poc.Sync.Domain;

/// <summary>
/// Facture réduite à ce que le POC doit prouver : un brouillon modifiable,
/// puis un document validé, numéroté, chaîné et immuable.
/// </summary>
public sealed class Invoice
{
    private List<InvoiceLineDto> _lines = [];

    private Invoice(Guid id, Guid dossierId) { Id = id; DossierId = dossierId; }

    public Guid Id { get; }
    public Guid DossierId { get; }
    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;
    public int Version { get; private set; }
    public string CustomerName { get; private set; } = "";
    public DateOnly? DueDate { get; private set; }
    public string? Notes { get; private set; }
    public IReadOnlyList<InvoiceLineDto> Lines => _lines;
    public string? Number { get; private set; }
    public DateOnly? IssueDate { get; private set; }
    public string? ChainHash { get; private set; }

    public decimal TotalExclTax => Totals.ExclTax(_lines);
    public decimal TotalInclTax => Totals.InclTax(_lines);

    public static Invoice NewDraft(Guid id, Guid dossierId, string customerName, IEnumerable<InvoiceLineDto> lines,
        DateOnly? dueDate = null, string? notes = null)
        => new(id, dossierId) { CustomerName = customerName, _lines = [.. lines], DueDate = dueDate, Notes = notes };

    public static Invoice FromDto(InvoiceDto dto) => new(dto.Id, dto.DossierId)
    {
        Status = dto.Status,
        Version = dto.Version,
        CustomerName = dto.CustomerName,
        DueDate = dto.DueDate,
        Notes = dto.Notes,
        _lines = [.. dto.Lines],
        Number = dto.Number,
        IssueDate = dto.IssueDate,
        ChainHash = dto.ChainHash,
    };

    public InvoiceDto ToDto() => new(Id, DossierId, Status, Version, CustomerName, DueDate, Notes, [.. _lines],
        Number, IssueDate, TotalExclTax, TotalInclTax, ChainHash);

    public void SetCustomerName(string value) { EnsureDraft(); CustomerName = value; }
    public void SetDueDate(DateOnly? value) { EnsureDraft(); DueDate = value; }
    public void SetNotes(string? value) { EnsureDraft(); Notes = value; }
    public void SetLines(IEnumerable<InvoiceLineDto> value) { EnsureDraft(); _lines = [.. value]; }

    /// <summary>Version serveur (ETag). Seul le serveur l'incrémente.</summary>
    public void BumpVersion() => Version++;

    /// <summary>Validation : contrôle de conformité, numéro définitif, maillon de chaîne. Irréversible.</summary>
    public void Validate(string number, DateOnly issueDate, string? previousHash)
    {
        EnsureDraft();
        var errors = Compliance.Check(this);
        if (errors.Count > 0) throw new ComplianceException(errors);

        Number = number;
        IssueDate = issueDate;
        Status = InvoiceStatus.Validated;
        ChainHash = HashChain.Link(previousHash, this);
    }

    private void EnsureDraft()
    {
        if (Status != InvoiceStatus.Draft)
            throw new InvalidOperationException($"La facture {Number} est validée : elle est immuable.");
    }
}

public static class Totals
{
    public static decimal ExclTax(IEnumerable<InvoiceLineDto> lines)
        => Math.Round(lines.Sum(l => l.Quantity * l.UnitPriceExclTax), 2, MidpointRounding.AwayFromZero);

    public static decimal InclTax(IEnumerable<InvoiceLineDto> lines)
        => Math.Round(lines.Sum(l => l.Quantity * l.UnitPriceExclTax * (1 + l.VatRate)), 2, MidpointRounding.AwayFromZero);
}
