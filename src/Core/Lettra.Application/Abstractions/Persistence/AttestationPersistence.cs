using Lettra.Domain.Parties;
using Lettra.Domain.Vigilance;

namespace Lettra.Application.Abstractions.Persistence;

/// <summary>Un fournisseur actif et le suivi de son attestation de vigilance (vide tant qu'il n'est pas suivi).</summary>
public sealed record SupplierAttestationEntry(SupplierId SupplierId, string SupplierName, SubcontractorAttestation? Attestation);

/// <summary>Les attestations de vigilance des fournisseurs, dans la base du dossier.</summary>
public interface IAttestationStore
{
    /// <summary>Le suivi d'un fournisseur (modifiable), ou <c>null</c> s'il n'est pas suivi.</summary>
    Task<SubcontractorAttestation?> FindAsync(SupplierId supplier, CancellationToken cancellationToken);

    Task AddAsync(SubcontractorAttestation attestation, CancellationToken cancellationToken);

    /// <summary>Les fournisseurs non archivés et leur suivi ; les fournisseurs d'un dossier se comptent en centaines.</summary>
    Task<IReadOnlyList<SupplierAttestationEntry>> ListAsync(CancellationToken cancellationToken);
}
