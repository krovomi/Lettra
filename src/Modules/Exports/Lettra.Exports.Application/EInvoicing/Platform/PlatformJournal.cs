using Lettra.Application.Abstractions.Integrity;
using Lettra.Contracts.Exports;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.EInvoicing.Platform;

/// <summary>
/// Les statuts d'une transmission dans le journal inaltérable du dossier : une entrée chaînée et scellée par statut,
/// dans la transaction qui l'enregistre. L'entrée porte la pièce (numéro, client, date) et l'empreinte du statut ; un
/// encaissement porte aussi son montant (TTC encaissé). Aucune ne compte dans le chiffre d'affaires des clôtures.
/// </summary>
internal static class PlatformJournal
{
    private const string Currency = Money.Euro;

    public static string Fingerprint(PlatformTransmission transmission, int position)
    {
        PlatformStatusEvent status = transmission.History[position];
        return PlatformEventFingerprint.Compute(
            transmission.Id,
            position,
            transmission.Platform,
            transmission.Reference,
            (int)transmission.Channel,
            (int?)transmission.Format,
            (int)status.Status,
            status.At,
            status.Reason,
            status.Amount);
    }

    /// <summary>Journalise les statuts de <paramref name="from"/> à la fin de l'historique.</summary>
    public static async Task<Result> AppendAsync(
        IIntegrityJournal journal, Invoice invoice, PlatformTransmission transmission, int from, CancellationToken cancellationToken)
    {
        for (int position = from; position < transmission.History.Count; position++)
        {
            PlatformStatusEvent status = transmission.History[position];
            JournalEntryKind kind = position == 0 ? JournalEntryKind.PlatformTransmitted
                : PlatformLifecycle.IsDeclaredBySeller(status.Status) ? JournalEntryKind.PlatformPaymentReported
                : JournalEntryKind.PlatformStatusReceived;
            decimal collected = status.Status == PlatformStatusDto.Collected ? status.Amount ?? 0m : 0m;
            Result<long> appended = await journal.AppendAsync(
                new DocumentLifecycleEvent(
                    kind,
                    invoice.Kind switch
                    {
                        InvoiceKind.DownPayment => JournaledDocumentKind.DepositInvoice,
                        InvoiceKind.CreditNote => JournaledDocumentKind.CreditNote,
                        _ => JournaledDocumentKind.Invoice,
                    },
                    invoice.Id.Value,
                    invoice.SeriesCode,
                    transmission.InvoiceNumber,
                    invoice.IssueDate!.Value,
                    invoice.CustomerId.Value,
                    0m,
                    0m,
                    collected,
                    Currency,
                    Fingerprint(transmission, position),
                    transmission.Id),
                cancellationToken).ConfigureAwait(false);
            if (appended.IsFailure)
            {
                return Result.Failure(appended.Errors);
            }
        }

        return Result.Success();
    }
}
