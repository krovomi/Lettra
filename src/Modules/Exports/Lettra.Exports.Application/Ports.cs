using Lettra.Calculations;
using Lettra.Domain.Billing;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application;

/// <summary>
/// Une écriture comptable telle que l'export la lit : celle d'une pièce de vente (journal <c>VE</c>), calculée et
/// enregistrée à la validation de la pièce, ou celle d'un encaissement ou de sa contre-passation (journal <c>BQ</c>,
/// vague 6, <see cref="InvoiceId"/> étant alors l'encaissement). Écrite dans la même transaction que son entrée au journal
/// d'intégrité ; le FEC ne recalcule rien. <see cref="LetteringCode"/> et <see cref="LetteringDate"/> portent le lettrage
/// actif de la ligne du compte client (411).
/// </summary>
public sealed record SalesJournalExportEntry(
    Guid InvoiceId,
    InvoiceKind Kind,
    string PieceNumber,
    DateOnly Date,
    Guid CustomerId,
    string CustomerName,
    IReadOnlyList<AccountingEntryLine> Lines,
    string JournalCode = SalesJournalExportEntry.SalesJournal,
    string? LetteringCode = null,
    DateOnly? LetteringDate = null,
    bool IsReversal = false)
{
    public const string SalesJournal = "VE";

    public bool IsBankEntry => JournalCode == PaymentAccounts.JournalCode;
}

/// <summary>Lecture des écritures d'un dossier (ventes et banque), dans l'ordre d'enregistrement.</summary>
public interface ISalesJournalReader
{
    /// <summary>Écritures datées de <paramref name="from"/> à <paramref name="to"/> (bornes comprises), dans l'ordre d'enregistrement.</summary>
    Task<IReadOnlyList<SalesJournalExportEntry>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>Dépôt des fichiers d'un export en masse (Worker) ; un fichier déjà déposé n'est pas refait à la reprise.</summary>
public interface IExportArchive
{
    Task<bool> ExistsAsync(string batch, DossierId dossier, string fileName, CancellationToken cancellationToken);

    Task WriteAsync(string batch, DossierId dossier, string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    /// <summary>Le rapport du lot (un fichier par lot, réécrit à chaque passe).</summary>
    Task WriteReportAsync(string batch, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}
