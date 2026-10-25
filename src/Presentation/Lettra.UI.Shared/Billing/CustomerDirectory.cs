using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared.Components.Palette;

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

    /// <summary>
    /// Le client dont le nom correspond le mieux à un nom dit ou tapé (« Dupont » trouve « Dupont SARL »), ou rien. Le
    /// dossier cherche d'abord par le mot le plus long du nom, sans accents ni casse : seuls les clients qui le
    /// contiennent arrivent, puis <see cref="PaletteMatcher"/> départage. La liste entière n'est jamais lue.
    /// </summary>
    public static async Task<Guid?> FindBestMatchAsync(this ICustomersGateway customers, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customers);
        string? word = PaletteMatcher.Normalize(name ?? string.Empty)
            .Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries)
            .MaxBy(w => w.Length);
        if (word is null)
        {
            return null;
        }

        OperationResult<PagedResponse<CustomerSummary>> found = await customers
            .SearchAsync(new PartyListQuery(Search: word, PageSize: CustomerListing.MaxPageSize), cancellationToken)
            .ConfigureAwait(false);
        return found.Value?.Items
            .Select(c => (c.Id, Score: PaletteMatcher.Score(c.Name, name!)))
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefault();
    }
}
