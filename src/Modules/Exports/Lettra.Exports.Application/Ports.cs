using Lettra.Calculations;
using Lettra.Domain.Billing;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application;

/// <summary>
/// Une écriture du journal des ventes telle que l'export la lit : calculée et enregistrée à la validation de la pièce,
/// dans la même transaction que son entrée au journal d'intégrité. Le FEC ne recalcule rien.
/// </summary>
public sealed record SalesJournalExportEntry(
    Guid InvoiceId,
    InvoiceKind Kind,
    string PieceNumber,
    DateOnly Date,
    Guid CustomerId,
    string CustomerName,
    IReadOnlyList<AccountingEntryLine> Lines);

/// <summary>Lecture du journal des ventes d'un dossier, dans l'ordre de validation des pièces.</summary>
public interface ISalesJournalReader
{
    /// <summary>Écritures datées de <paramref name="from"/> à <paramref name="to"/> (bornes comprises), dans l'ordre de validation.</summary>
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
