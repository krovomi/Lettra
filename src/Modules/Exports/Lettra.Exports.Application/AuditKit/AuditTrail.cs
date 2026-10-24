using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Exports.Domain.Platform;

namespace Lettra.Exports.Application.AuditKit;

/// <summary>
/// Ce que la piste d'audit fiable d'une période lit dans la base du dossier : les pièces validées datées de la période
/// (factures, acomptes, avoirs) avec leurs auteurs, toutes leurs transmissions à la plateforme agréée, et les
/// encaissements datés de la période, contre-passés compris.
/// </summary>
public sealed record AuditTrailData(
    IReadOnlyList<Invoice> Invoices,
    IReadOnlyList<PlatformTransmission> Transmissions,
    IReadOnlyList<Payment> Payments);

/// <summary>Lecture de la piste d'audit fiable d'un dossier sur une période (bornes comprises).</summary>
public interface IAuditTrailReader
{
    /// <summary>Pièces dans l'ordre de leur date puis de leur numéro ; encaissements dans l'ordre de leur numéro.</summary>
    Task<AuditTrailData> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
