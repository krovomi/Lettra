using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Shared.Billing;

/// <summary>
/// Tous les clients du dossier pour les listes de choix (client d'une pièce, palette Ctrl+K), lus page par page à la
/// taille que l'API accepte (<see cref="CustomerListing.MaxPageSize"/>). Une seule page trop grande était refusée en
/// entier : la liste restait vide et aucun devis ni facture ne pouvait naître.
/// </summary>
public static class CustomerDirectory
{
    /// <summary>Garde-fou contre une API qui renverrait toujours des pages pleines.</summary>
    private const int MaxPages = 50;

    public static async Task<OperationResult<IReadOnlyList<CustomerSummary>>> ListAllAsync(
        this ICustomersGateway customers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customers);
        List<CustomerSummary> all = [];
        for (int page = 1; page <= MaxPages; page++)
        {
            OperationResult<PagedResponse<CustomerSummary>> result =
                await customers.ListAsync(page, CustomerListing.MaxPageSize, cancellationToken).ConfigureAwait(false);
            if (result.Value is not { } paged)
            {
                return result.Errors.Count == 0
                    ? OperationResult<IReadOnlyList<CustomerSummary>>.Success(all)
                    : OperationResult<IReadOnlyList<CustomerSummary>>.Failure(result.Errors);
            }

            all.AddRange(paged.Items);
            if (paged.Items.Count < CustomerListing.MaxPageSize || all.Count >= paged.TotalCount)
            {
                break;
            }
        }

        return OperationResult<IReadOnlyList<CustomerSummary>>.Success(all);
    }
}
