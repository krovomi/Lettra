using System.Globalization;
using System.Text;
using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Domain.Integrity;
using Lettra.Exports.Domain.Platform;

namespace Lettra.Exports.Application.AuditKit;

/// <summary>
/// Les tableaux de la piste d'audit fiable, en CSV lisible par un tableur français : séparateur <c>;</c>, décimales à
/// la virgule, dates ISO 8601, horodatages en UTC, UTF-8 avec BOM. Une personne est écrite « Nom (identifiant) » ;
/// sans registre des personnes (poste isolé), par son seul identifiant.
/// </summary>
public static class AuditTrailCsv
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Une ligne par pièce : qui l'a créée, validée, émise (et approuvée pour un avoir), son empreinte figée.</summary>
    public static byte[] Pieces(IReadOnlyList<Invoice> invoices, IReadOnlyList<PlatformTransmission> transmissions, IReadOnlyDictionary<Guid, string> people)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(transmissions);
        Dictionary<Guid, PlatformTransmission> latest = transmissions
            .GroupBy(t => t.InvoiceId)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.SubmittedAt).ThenBy(t => t.Id).Last());

        StringBuilder csv = Header(
            "numero", "nature", "date", "client", "total_ht", "tva", "total_ttc", "creee_le", "creee_par", "validee_le", "validee_par",
            "emise_le", "emise_par", "avoir_approuve_le", "avoir_approuve_par", "empreinte_contenu", "statut_plateforme", "fichier_xml");
        foreach (Invoice invoice in invoices)
        {
            Row(csv,
                invoice.Number,
                KindLabel(invoice.Kind),
                Day(invoice.IssueDate),
                invoice.Customer.Name,
                Amount(invoice.TotalExclTax),
                Amount(invoice.TotalVat),
                Amount(invoice.TotalInclTax),
                Instant(invoice.CreatedAt),
                Person(invoice.CreatedBy, people),
                Instant(invoice.ValidatedAt),
                Person(invoice.ValidatedBy, people),
                Instant(invoice.IssuedAt),
                Person(invoice.IssuedBy, people),
                Instant(invoice.CreditApprovedAt),
                Person(invoice.CreditApprovedBy, people),
                invoice.ContentHash,
                latest.TryGetValue(invoice.Id.Value, out PlatformTransmission? transmission) ? StatusLabel(transmission.Status) : "Non transmise",
                AuditKitLayout.XmlFile(invoice.Number!));
        }

        return Encode(csv);
    }

    /// <summary>Une ligne par statut de chaque transmission à la plateforme agréée, dans l'ordre reçu.</summary>
    public static byte[] Transmissions(IReadOnlyList<PlatformTransmission> transmissions)
    {
        ArgumentNullException.ThrowIfNull(transmissions);
        StringBuilder csv = Header("numero_piece", "transmission", "plateforme", "canal", "format", "reference", "soumise_le", "statut", "code_statut", "statut_le", "motif", "montant");
        foreach (PlatformTransmission transmission in transmissions.OrderBy(t => t.SubmittedAt).ThenBy(t => t.Id))
        {
            foreach (PlatformStatusEvent status in transmission.History)
            {
                Row(csv,
                    transmission.InvoiceNumber,
                    transmission.Id.ToString(),
                    transmission.Platform,
                    transmission.Channel == PlatformChannel.EInvoice ? "Facture électronique" : "E-reporting",
                    transmission.Format?.ToString(),
                    transmission.Reference,
                    Instant(transmission.SubmittedAt),
                    StatusLabel(status.Status),
                    ((int)status.Status).ToString(CultureInfo.InvariantCulture),
                    Instant(status.At),
                    status.Reason,
                    status.Amount is { } amount ? Amount(amount) : null);
            }
        }

        return Encode(csv);
    }

    /// <summary>Une ligne par encaissement : qui l'a saisi, et qui l'a contre-passé (rien n'est jamais supprimé).</summary>
    public static byte[] Payments(IReadOnlyList<Payment> payments, IReadOnlyDictionary<Guid, string> people)
    {
        ArgumentNullException.ThrowIfNull(payments);
        StringBuilder csv = Header(
            "numero", "date", "client", "montant", "mode", "reference", "statut", "saisi_le", "saisi_par", "contre_passe_le", "contre_passe_par", "motif");
        foreach (Payment payment in payments)
        {
            Row(csv,
                payment.Number,
                Day(payment.Date),
                payment.CustomerName,
                Amount(payment.Amount),
                MethodLabel(payment.Method),
                payment.Reference,
                payment.IsReversed ? "Contre-passé" : "Comptabilisé",
                Instant(payment.CreatedAt),
                Person(payment.CreatedBy, people),
                Instant(payment.ReversedAt),
                Person(payment.ReversedBy, people),
                payment.ReversalReason);
        }

        return Encode(csv);
    }

    /// <summary>Les entrées de la chaîne anti-fraude de la période, dans l'ordre : ce que l'attestation a vérifié.</summary>
    public static byte[] Journal(IReadOnlyList<JournalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        StringBuilder csv = Header(
            "sequence", "horodatage", "evenement", "serie", "numero", "date", "total_ht", "tva", "total_ttc", "empreinte_contenu", "empreinte_precedente", "empreinte", "cle");
        foreach (JournalEntry entry in entries)
        {
            Row(csv,
                entry.Sequence.ToString(CultureInfo.InvariantCulture),
                Instant(entry.OccurredAt),
                EventLabel(entry.Kind),
                entry.SeriesCode,
                entry.Number,
                Day(entry.IssueDate),
                Amount(entry.TotalExclTax),
                Amount(entry.TotalVat),
                Amount(entry.TotalInclTax),
                entry.ContentHash,
                entry.PreviousHash,
                entry.Hash,
                entry.KeyId);
        }

        return Encode(csv);
    }

    public static string KindLabel(InvoiceKind kind) => kind switch
    {
        InvoiceKind.DownPayment => "Facture d'acompte",
        InvoiceKind.CreditNote => "Avoir",
        _ => "Facture",
    };

    public static string EventLabel(JournalEntryKind kind) => kind switch
    {
        JournalEntryKind.QuoteValidated => "Devis validé",
        JournalEntryKind.InvoiceValidated => "Facture validée",
        JournalEntryKind.InvoiceIssued => "Facture émise",
        JournalEntryKind.CreditNoteValidated => "Avoir validé",
        JournalEntryKind.CreditNoteIssued => "Avoir émis",
        JournalEntryKind.DocumentCancelled => "Pièce annulée",
        JournalEntryKind.PlatformTransmitted => "Dépôt à la plateforme",
        JournalEntryKind.PlatformStatusReceived => "Statut de la plateforme",
        JournalEntryKind.PlatformPaymentReported => "Encaissement déclaré à la plateforme",
        JournalEntryKind.PaymentRecorded => "Encaissement enregistré",
        JournalEntryKind.PaymentReversed => "Encaissement contre-passé",
        JournalEntryKind.PeriodClosed => "Clôture signée",
        JournalEntryKind.KeyRotated => "Rotation des clés",
        _ => kind.ToString(),
    };

    public static string StatusLabel(PlatformStatus status) => status switch
    {
        PlatformStatus.Deposited => "Déposée",
        PlatformStatus.Issued => "Émise",
        PlatformStatus.Received => "Reçue",
        PlatformStatus.MadeAvailable => "Mise à disposition",
        PlatformStatus.InProgress => "Prise en charge",
        PlatformStatus.Approved => "Approuvée",
        PlatformStatus.PartiallyApproved => "Approuvée partiellement",
        PlatformStatus.Disputed => "En litige",
        PlatformStatus.Suspended => "Suspendue",
        PlatformStatus.Completed => "Complétée",
        PlatformStatus.Refused => "Refusée",
        PlatformStatus.PaymentSent => "Paiement transmis",
        PlatformStatus.Collected => "Encaissée",
        PlatformStatus.Rejected => "Rejetée",
        _ => status.ToString(),
    };

    private static string MethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Transfer => "Virement",
        PaymentMethod.Cheque => "Chèque",
        PaymentMethod.Card => "Carte bancaire",
        PaymentMethod.Cash => "Espèces",
        PaymentMethod.DirectDebit => "Prélèvement",
        _ => method.ToString(),
    };

    private static string? Person(Guid? id, IReadOnlyDictionary<Guid, string> people) =>
        id is not { } value || value == Guid.Empty ? null
        : people.TryGetValue(value, out string? name) ? $"{name} ({value})"
        : value.ToString();

    private static string? Day(DateOnly? day) => day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Instant(DateTimeOffset? at) => at?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Amount(decimal amount) => amount.ToString("0.00", French);

    private static StringBuilder Header(params string[] columns) => new StringBuilder().Append(string.Join(';', columns)).Append("\r\n");

    private static void Row(StringBuilder csv, params string?[] values) =>
        csv.Append(string.Join(';', values.Select(Escape))).Append("\r\n");

    /// <summary>Entre guillemets si la valeur contient un séparateur, un guillemet ou un saut de ligne (RFC 4180).</summary>
    internal static string Escape(string? value) =>
        value is null ? string.Empty
        : value.AsSpan().IndexOfAny(";\"\r\n") >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
        : value;

    private static byte[] Encode(StringBuilder csv) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
}
