namespace Lettra.Domain;

public enum InvoiceStatus { Draft = 0, Validated = 1 }

/// Facture minimale : brouillon, puis validation qui attribue un numéro de la série (sans trou).
public sealed class Invoice
{
    private readonly List<InvoiceLine> _lines = [];

    private Invoice() { }

    public Invoice(Guid customerId, string reference)
    {
        Id = Guid.CreateVersion7();
        CustomerId = customerId;
        Reference = reference;
        IssueDate = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    /// Référence libre du brouillon (le POC y écrit le dossier émetteur pour vérifier l'isolation).
    public string Reference { get; private set; } = "";
    public InvoiceStatus Status { get; private set; }
    public string? Number { get; private set; }
    public DateTimeOffset IssueDate { get; private set; }
    /// Ajoutée par la migration 2 (preuve d'une montée de schéma sur N bases).
    public DateOnly? DueDate { get; private set; }
    public decimal TotalExclTax { get; private set; }
    public IReadOnlyList<InvoiceLine> Lines => _lines;

    public void AddLine(string label, decimal quantity, decimal unitPrice)
    {
        if (Status != InvoiceStatus.Draft)
            throw new InvalidOperationException("Une facture validée ne se modifie pas.");
        _lines.Add(new InvoiceLine(Id, _lines.Count + 1, label, quantity, unitPrice));
        TotalExclTax = _lines.Sum(l => l.Amount);
    }

    public void SetDueDate(DateOnly dueDate) => DueDate = dueDate;

    public void Validate(string number)
    {
        if (Status != InvoiceStatus.Draft)
            throw new InvalidOperationException("Facture déjà validée.");
        if (_lines.Count == 0 && TotalExclTax == 0)
            throw new InvalidOperationException("Une facture sans ligne ne se valide pas.");
        Number = number;
        Status = InvoiceStatus.Validated;
    }
}

public sealed class InvoiceLine
{
    private InvoiceLine() { }

    internal InvoiceLine(Guid invoiceId, int position, string label, decimal quantity, decimal unitPrice)
    {
        Id = Guid.CreateVersion7();
        InvoiceId = invoiceId;
        Position = position;
        Label = label;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Amount = decimal.Round(quantity * unitPrice, 4, MidpointRounding.ToEven);
    }

    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public int Position { get; private set; }
    public string Label { get; private set; } = "";
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Amount { get; private set; }
}
