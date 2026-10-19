using Lettra.Contracts.Common;

namespace Lettra.UI.Abstractions;

/// <summary>Le kit contrôle fiscal remis par le serveur : son nom et l'archive ZIP (manifeste en tête).</summary>
public sealed record AuditKitDocument(string FileName, byte[] Content);

/// <summary>
/// Kit contrôle fiscal d'une période (vague 7), permission <c>ArchivesExporter</c>. Refus possibles : ceux de
/// l'export FEC (<c>Fec.PeriodInvalid</c>, <c>Fec.PeriodTooLong</c>, <c>Fec.SirenMissing</c>, <c>Fec.ExportInvalid</c>)
/// et de la chaîne d'intégrité.
/// </summary>
public interface IAuditKitGateway
{
    Task<OperationResult<AuditKitDocument>> PrepareAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
