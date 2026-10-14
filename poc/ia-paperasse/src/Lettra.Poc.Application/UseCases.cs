using Lettra.Compliance.Invoices;
using Lettra.Compliance.Mentions;

namespace Lettra.Poc.Application;

public sealed record ComplianceCheck(string InvoiceNumber, ComplianceReport Report, InvoiceTotals Totals);

public sealed record CreateQuoteDraftCommand(Guid DossierId, Guid ClientId, string Subject, IReadOnlyList<QuoteDraftLine> Lines);

/// <summary>Cas d'usage du POC. Les outils IA les appellent avec l'acteur « AssistantIA » : mêmes règles, même audit que l'écran.</summary>
public sealed class DossierUseCases(InMemoryDossierStore store, IClock clock)
{
    private readonly InvoiceComplianceValidator _validator = new();

    public CompanyProfile GetCompany(Guid dossierId) => store.Get(dossierId).Company;

    public IReadOnlyList<DossierClient> GetClients(Guid dossierId) => store.Get(dossierId).Clients;

    public IReadOnlyList<DossierClient> SearchClients(Guid dossierId, string text)
    {
        var needle = text.Trim();
        return store.Get(dossierId).Clients
            .Where(c => c.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) || needle.Contains(c.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public StoredInvoice GetInvoice(Guid dossierId, Guid invoiceId) =>
        store.Get(dossierId).Invoices.FirstOrDefault(i => i.Id == invoiceId) ?? throw new DocumentNotFoundException(invoiceId);

    public ComplianceCheck CheckInvoiceCompliance(Guid dossierId, Guid invoiceId)
    {
        var dossier = store.Get(dossierId);
        var invoice = GetInvoice(dossierId, invoiceId).Document;
        var report = _validator.Validate(dossier.Company, invoice, clock.Today);
        return new ComplianceCheck(invoice.Number ?? "(sans numéro)", report, InvoiceTotalsCalculator.Compute(dossier.Company, invoice));
    }

    public QuoteDraft CreateQuoteDraft(CreateQuoteDraftCommand command, Actor actor)
    {
        var dossier = store.Get(command.DossierId);
        if (dossier.Clients.All(c => c.Id != command.ClientId)) throw new DocumentNotFoundException(command.ClientId);
        if (command.Lines.Count == 0) throw new ArgumentException("Un devis contient au moins une ligne.");
        if (command.Lines.Any(l => l.Quantity <= 0 || l.UnitPriceExcludingVat < 0 || string.IsNullOrWhiteSpace(l.Designation)))
            throw new ArgumentException("Chaque ligne a une désignation, une quantité positive et un prix unitaire HT.");

        var document = new InvoiceDocument
        {
            Lines = command.Lines.Select(l => new InvoiceLine
            {
                Description = l.Designation, Quantity = l.Quantity, UnitPrice = l.UnitPriceExcludingVat, VatRate = l.VatRate,
            }).ToList(),
        };
        var draft = new QuoteDraft
        {
            Id = Guid.NewGuid(),
            DossierId = dossier.Id,
            ClientId = command.ClientId,
            Subject = command.Subject,
            Lines = command.Lines,
            Totals = InvoiceTotalsCalculator.Compute(dossier.Company, document),
            CreatedBy = actor.Origin,
        };
        dossier.Drafts.Add(draft);
        store.Audit(new AuditRecord(clock.Now, dossier.Id, actor.UserId, actor.Origin, "Devis.CreerBrouillon", $"{draft.Id} {command.Subject}"));
        return draft;
    }

    /// <summary>Validation d'un brouillon : réservée à un humain. Aucun outil IA n'appelle ce cas d'usage.</summary>
    public QuoteDraft ValidateDraft(Guid dossierId, Guid draftId, Actor actor)
    {
        if (actor.Kind != ActorKind.Human) throw new HumanActionRequiredException("Valider un devis");
        var draft = store.Get(dossierId).Drafts.FirstOrDefault(d => d.Id == draftId) ?? throw new DocumentNotFoundException(draftId);
        draft.Status = DraftStatus.Valide;
        draft.ValidatedBy = actor.UserId;
        store.Audit(new AuditRecord(clock.Now, dossierId, actor.UserId, actor.Origin, "Devis.Valider", draftId.ToString()));
        return draft;
    }
}
