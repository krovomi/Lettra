using Lettra.Domain.Common;
using Lettra.Domain.Integrity;

namespace Lettra.Application.Abstractions.Integrity;

/// <summary>
/// Journal inaltérable du dossier (chaîne anti-fraude). À appeler dans la transaction de validation ou d'émission (le
/// travail de <c>IDocumentNumbering.ExecuteWithNextNumberAsync</c>), avant l'enregistrement : le numéro, la pièce figée
/// et l'entrée s'écrivent ensemble ou pas du tout. Sans transaction ouverte, l'appel ouvre la sienne et enregistre.
/// </summary>
public interface IIntegrityJournal
{
    /// <summary>Ajoute l'entrée chaînée et renvoie son numéro de séquence.</summary>
    Task<Result<long>> AppendAsync(DocumentLifecycleEvent @event, CancellationToken ct);
}

/// <summary>Recalcule l'empreinte canonique des pièces d'après la base, pour le contrôle d'intégrité.</summary>
public interface IJournaledDocumentReader
{
    Task<IReadOnlyDictionary<Guid, string>> ComputeContentHashesAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken ct);
}
