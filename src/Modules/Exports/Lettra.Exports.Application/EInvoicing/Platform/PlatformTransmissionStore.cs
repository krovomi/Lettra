using Lettra.Exports.Domain.Platform;

namespace Lettra.Exports.Application.EInvoicing.Platform;

/// <summary>Les transmissions d'un dossier, dans sa base.</summary>
public interface IPlatformTransmissionStore
{
    /// <summary>La dernière transmission de la pièce, ou rien si elle n'a jamais été transmise.</summary>
    Task<PlatformTransmission?> FindLatestAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>
    /// Ajoute une nouvelle transmission, ou les statuts ajoutés à une transmission existante, dans la transaction de
    /// l'appelant (<c>IBillingTransaction</c>) : ils s'enregistrent avec leurs entrées au journal inaltérable.
    /// </summary>
    Task StageAsync(PlatformTransmission transmission, CancellationToken cancellationToken);
}
