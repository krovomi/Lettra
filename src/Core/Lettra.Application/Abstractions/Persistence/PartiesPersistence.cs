using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Persistence;

/// <summary>Écriture des fournisseurs : on manipule l'agrégat.</summary>
public interface ISupplierRepository
{
    Task AddAsync(Supplier supplier, CancellationToken cancellationToken);

    Task<Supplier?> FindAsync(SupplierId id, CancellationToken cancellationToken);
}

/// <summary>
/// Critères d'une page de clients ou de fournisseurs : nom contenant <see cref="Search"/> (sans accents ni casse), tri,
/// et pour les clients seulement, leur nature (<see cref="Kind"/>).
/// </summary>
public sealed record PartyListFilter(
    string? Search,
    int Page,
    int PageSize,
    PartySortDto Sort = PartySortDto.Name,
    SortDirectionDto Direction = SortDirectionDto.Ascending,
    CustomerKind? Kind = null);

/// <summary>Lecture des fournisseurs : projection directe en contrats.</summary>
public interface ISupplierReadStore
{
    Task<PagedResponse<SupplierSummary>> ListAsync(PartyListFilter filter, CancellationToken cancellationToken);

    Task<SupplierResponse?> GetAsync(SupplierId id, CancellationToken cancellationToken);
}

/// <summary>Les changements d'IBAN des fournisseurs (à vérifier avant tout paiement).</summary>
public interface IIbanChangeRepository
{
    Task AddAsync(IbanChange change, CancellationToken cancellationToken);

    Task<IbanChange?> FindAsync(IbanChangeId id, CancellationToken cancellationToken);

    /// <summary>Les changements encore à vérifier d'un fournisseur, suivis pour être remplacés par un plus récent.</summary>
    Task<IReadOnlyList<IbanChange>> PendingForAsync(SupplierId supplier, CancellationToken cancellationToken);

    /// <summary>Le nombre de changements encore à vérifier dans le dossier.</summary>
    Task<int> CountPendingAsync(CancellationToken cancellationToken);
}

/// <summary>Critères de la liste des changements d'IBAN : état, nom du fournisseur (sans accents ni casse), tri.</summary>
public sealed record IbanChangeListFilter(
    IbanChangeStatusDto? Status,
    string? Search,
    int Page,
    int PageSize,
    IbanChangeSortDto Sort = IbanChangeSortDto.ChangedAt,
    SortDirectionDto Direction = SortDirectionDto.Descending);

/// <summary>Lecture des changements d'IBAN : projection directe en contrats, avec le nom du fournisseur.</summary>
public interface IIbanChangeReadStore
{
    Task<PagedResponse<IbanChangeResponse>> ListAsync(IbanChangeListFilter filter, CancellationToken cancellationToken);

    /// <summary>L'historique d'un fournisseur, du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<IbanChangeResponse>> ForSupplierAsync(SupplierId supplier, CancellationToken cancellationToken);
}

/// <summary>Un fournisseur payable par virement (non supprimé, avec un IBAN), tel que lu pour les contrôles avant paiement.</summary>
public sealed record SupplierPayeeRow(
    SupplierId Id,
    string Name,
    string? AccountHolderName,
    Iban Iban,
    PayeeMatch? CheckResult,
    DateTimeOffset? CheckedAt,
    string? SuggestedName,
    bool IbanChangePending);

/// <summary>Lecture des bénéficiaires du dossier : les contrôles se calculent dans l'application, les fournisseurs d'un dossier se comptent en centaines.</summary>
public interface IPayeeCheckReadStore
{
    Task<IReadOnlyList<SupplierPayeeRow>> ListPayeesAsync(CancellationToken cancellationToken);
}
