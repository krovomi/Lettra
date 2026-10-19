using Lettra.Ai.Tools.Company;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Customers;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Ai.Tools;

/// <summary>
/// Contexte du dossier pour l'assistant, lu par les requêtes de l'application (donc avec les droits de l'utilisateur) :
/// company.json virtuel construit depuis les paramètres du dossier, et identités à pseudonymiser.
/// Clients et fournisseurs : les identités servent uniquement à la pseudonymisation : elles ne partent jamais vers le moteur.
/// </summary>
internal sealed class AiDossierContext(IQueryDispatcher queries, ICompanyFactsSource company) : IAiDossierContext
{
    /// <summary>Au-delà, les noms restants sont encore couverts par les détecteurs (SIREN, IBAN, e-mails…), pas par leur nom.</summary>
    private const int MaxPages = 10;

    public async Task<string> GetCompanyJsonAsync(DossierId dossier, CancellationToken cancellationToken)
    {
        CompanyFacts? facts = await company.GetAsync(dossier, cancellationToken).ConfigureAwait(false);
        return CompanyJsonBuilder.Build(facts).ToJsonString(AiToolResult.JsonOptions);
    }

    public async Task<IReadOnlyList<KnownIdentity>> GetKnownIdentitiesAsync(DossierId dossier, CancellationToken cancellationToken)
    {
        // L'entreprise du dossier d'abord : son nom, son adresse et ses identifiants figurent dans company.json.
        CompanyFacts? facts = await company.GetAsync(dossier, cancellationToken).ConfigureAwait(false);
        List<KnownIdentity> identities = [.. facts?.Identities() ?? []];
        await CollectAsync(
            page => new ListCustomersQuery(dossier, page, ListCustomersQuery.MaxPageSize),
            ListCustomersQuery.MaxPageSize,
            c => Identities(c.Kind == CustomerKindDto.Individual ? IdentityKind.Person : IdentityKind.Client, c.Name, c.Siren),
            identities,
            cancellationToken).ConfigureAwait(false);
        await CollectAsync(
            page => new ListSuppliersQuery(dossier, page, ListSuppliersQuery.MaxPageSize),
            ListSuppliersQuery.MaxPageSize,
            s => Identities(IdentityKind.Client, s.Name, s.Siren),
            identities,
            cancellationToken).ConfigureAwait(false);
        return identities;
    }

    private static IEnumerable<KnownIdentity> Identities(IdentityKind kind, string name, string? siren)
    {
        yield return new KnownIdentity(kind, name);
        if (siren is { Length: > 0 })
        {
            yield return new KnownIdentity(IdentityKind.Siren, siren);
        }
    }

    private async Task CollectAsync<TItem>(
        Func<int, IQuery<Result<PagedResponse<TItem>>>> query,
        int pageSize,
        Func<TItem, IEnumerable<KnownIdentity>> map,
        List<KnownIdentity> identities,
        CancellationToken cancellationToken)
    {
        for (int page = 1; page <= MaxPages; page++)
        {
            Result<PagedResponse<TItem>> result = await queries.QueryAsync(query(page), cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                // Sans droit de lecture des tiers, l'assistant ne peut pas les lire : seuls les détecteurs s'appliquent.
                return;
            }

            identities.AddRange(result.Value.Items.SelectMany(map));
            if (page * pageSize >= result.Value.TotalCount)
            {
                return;
            }
        }
    }
}
