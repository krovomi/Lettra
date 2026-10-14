using Lettra.Compliance.Invoices;

namespace Lettra.Poc.Application;

// Modèle minimal d'un dossier, le temps du POC. Le vrai domaine vient du socle (thread « Socle de la solution Lettra »).

public enum ActorKind { Human, AssistantIA }

/// <summary>Qui agit : un humain, ou l'assistant IA au nom d'un humain.</summary>
public sealed record Actor(Guid UserId, ActorKind Kind)
{
    public string Origin => Kind == ActorKind.AssistantIA ? "AssistantIA" : "Utilisateur";
}

public sealed record DossierClient(Guid Id, string Name, string? Address, string? Country, string? Siren, string? VatNumber, string? Email);

public sealed record StoredInvoice(Guid Id, InvoiceDocument Document);

public enum DraftStatus { Brouillon, Valide }

public sealed record QuoteDraftLine(string Designation, decimal Quantity, decimal UnitPriceExcludingVat, decimal? VatRate);

public sealed class QuoteDraft
{
    public required Guid Id { get; init; }
    public required Guid DossierId { get; init; }
    public required Guid ClientId { get; init; }
    public required string Subject { get; init; }
    public required IReadOnlyList<QuoteDraftLine> Lines { get; init; }
    public required InvoiceTotals Totals { get; init; }
    public required string CreatedBy { get; init; }
    public DraftStatus Status { get; internal set; } = DraftStatus.Brouillon;
    public Guid? ValidatedBy { get; internal set; }
}

public sealed class Dossier
{
    public required Guid Id { get; init; }
    public required Guid CabinetId { get; init; }
    public required CompanyProfile Company { get; init; }
    public List<DossierClient> Clients { get; } = [];
    public List<StoredInvoice> Invoices { get; } = [];
    public List<QuoteDraft> Drafts { get; } = [];
}

public sealed record AuditRecord(DateTimeOffset At, Guid DossierId, Guid UserId, string Origin, string Action, string Detail);
