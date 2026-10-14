using System.Text.Json;
using Lettra.Poc.Sync.Contracts;
using Lettra.Poc.Sync.Domain;

namespace Lettra.Poc.Sync.Client;

public enum ConflictChoice { Mine, Server, Custom }

public sealed record FieldResolution(ConflictChoice Choice, JsonElement? CustomValue = null);

/// <summary>
/// Cas d'usage côté poste, appelés par l'UI que le réseau soit là ou non.
/// Chaque cas d'usage écrit la donnée locale et son entrée d'outbox dans la même transaction.
/// </summary>
public sealed class OfflineInvoicing(ClientProfile profile, LocalStore store, TimeProvider clock)
{
    /// <summary>Un brouillon créé hors ligne naît en version 1 côté serveur (contrat de CreateDraft).</summary>
    private const int CreatedDraftServerVersion = 1;

    public Guid CreateDraft(string customerName, IEnumerable<InvoiceLineDto> lines, DateOnly? dueDate = null, string? notes = null)
    {
        var draft = Invoice.NewDraft(Guid.NewGuid(), profile.DossierId, customerName, lines, dueDate, notes).ToDto();
        store.InTransaction(tx =>
        {
            store.SaveInvoice(tx, draft, serverVersion: null, pendingValidation: false);
            var key = Guid.NewGuid();
            store.Enqueue(tx, OutboxKind.Intent, key, draft.Id, new CreateDraftCommand(key, draft), clock.GetUtcNow());
        });
        return draft.Id;
    }

    public void EditDraft(Guid id, Action<Invoice> edit)
    {
        store.InTransaction(tx =>
        {
            var local = store.FindInvoice(id, tx) ?? throw new KeyNotFoundException($"Brouillon {id} introuvable.");
            if (local.PendingValidation)
                throw new InvalidOperationException("Validation demandée : le brouillon attend sa numérotation à la synchronisation.");

            var invoice = Invoice.FromDto(local.Invoice);
            var before = DraftFields.All.ToDictionary(f => f, f => DraftFieldAccessor.Get(invoice, f));
            edit(invoice);

            var changes = DraftFields.All
                .Select(f => (Field: f, Base: before[f], Mine: DraftFieldAccessor.Get(invoice, f)))
                .Where(x => !JsonElement.DeepEquals(x.Base, x.Mine))
                .ToDictionary(x => x.Field, x => new FieldChange(x.Base, x.Mine));
            if (changes.Count == 0) return;

            var key = Guid.NewGuid();
            var command = new UpdateDraftCommand(key, id, local.ServerVersion ?? CreatedDraftServerVersion, changes);
            store.SaveInvoice(tx, invoice.ToDto(), local.ServerVersion, pendingValidation: false);
            store.Enqueue(tx, OutboxKind.Intent, key, id, command, clock.GetUtcNow());
        });
    }

    /// <summary>
    /// MAUI : valide tout de suite avec la série du poste (fait). Navigateur : demande la validation,
    /// le serveur numérotera à la synchronisation (intention). Retourne le numéro s'il est attribué.
    /// </summary>
    public string? Validate(Guid id) => profile.Kind == ClientKind.Maui ? ValidateOnDevice(id) : RequestValidation(id);

    private string ValidateOnDevice(Guid id) => store.InTransaction(tx =>
    {
        var local = store.FindInvoice(id, tx) ?? throw new KeyNotFoundException($"Brouillon {id} introuvable.");
        var series = profile.SeriesCode!;
        var (last, lastHash) = store.Series(tx, series);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var invoice = Invoice.FromDto(local.Invoice);
        invoice.Validate(InvoiceNumber.Format(today.Year, series, last + 1), today, lastHash);   // conformité locale
        var dto = invoice.ToDto();

        var key = Guid.NewGuid();
        var fact = new ValidatedInvoiceFact(key, series, last + 1, lastHash, dto, DraftBaseVersion: local.ServerVersion);
        store.SaveInvoice(tx, dto, local.ServerVersion, pendingValidation: false);
        store.SaveSeries(tx, series, last + 1, dto.ChainHash);
        store.Enqueue(tx, OutboxKind.Fact, key, id, fact, clock.GetUtcNow());
        return dto.Number!;
    });

    private string? RequestValidation(Guid id)
    {
        store.InTransaction(tx =>
        {
            var local = store.FindInvoice(id, tx) ?? throw new KeyNotFoundException($"Brouillon {id} introuvable.");
            var invoice = Invoice.FromDto(local.Invoice);
            var errors = Compliance.Check(invoice);                  // l'utilisateur est prévenu tout de suite
            if (errors.Count > 0) throw new ComplianceException(errors);

            var key = Guid.NewGuid();
            store.SaveInvoice(tx, local.Invoice, local.ServerVersion, pendingValidation: true);
            store.Enqueue(tx, OutboxKind.Intent, key, id, new ValidateDraftCommand(key, id, ContentFingerprint.Of(invoice)), clock.GetUtcNow());
        });
        return null;
    }

    /// <summary>Écran de résolution : pour chaque champ, ma version, la version serveur ou une fusion saisie.</summary>
    public void ResolveConflict(Guid id, IReadOnlyDictionary<string, FieldResolution> resolutions)
    {
        store.InTransaction(tx =>
        {
            var conflicts = store.Conflicts(id, tx);
            var local = store.FindInvoice(id, tx) ?? throw new KeyNotFoundException($"Brouillon {id} introuvable.");
            var invoice = Invoice.FromDto(local.Invoice);
            var changes = new Dictionary<string, FieldChange>();

            foreach (var c in conflicts)
            {
                var resolution = resolutions.GetValueOrDefault(c.Field)
                    ?? throw new InvalidOperationException($"Le champ {c.Field} n'est pas résolu.");
                var chosen = resolution.Choice switch
                {
                    ConflictChoice.Mine => c.Mine,
                    ConflictChoice.Server => c.Server,
                    _ => resolution.CustomValue ?? throw new ArgumentException($"Valeur fusionnée manquante pour {c.Field}."),
                };
                DraftFieldAccessor.Set(invoice, c.Field, chosen);
                if (!JsonElement.DeepEquals(chosen, c.Server)) changes[c.Field] = new FieldChange(c.Server, chosen);
            }

            store.DeleteConflicts(tx, id);
            store.SaveInvoice(tx, invoice.ToDto(), local.ServerVersion, local.PendingValidation);
            if (changes.Count == 0) return;   // tout « version serveur » : rien à renvoyer

            var key = Guid.NewGuid();
            store.Enqueue(tx, OutboxKind.Intent, key, id,
                new UpdateDraftCommand(key, id, local.ServerVersion ?? CreatedDraftServerVersion, changes), clock.GetUtcNow());
        });
    }
}
