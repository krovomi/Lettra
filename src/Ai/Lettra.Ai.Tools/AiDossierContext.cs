using System.Text.Json;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Ai.Tools;

/// <summary>
/// Contexte du dossier pour l'assistant, lu par les requêtes de l'application (donc avec les droits de l'utilisateur).
/// Les identités servent uniquement à la pseudonymisation : elles ne partent jamais vers le moteur.
/// </summary>
internal sealed class AiDossierContext(IQueryDispatcher queries) : IAiDossierContext
{
    /// <summary>Au-delà, les noms restants sont encore couverts par les détecteurs (SIREN, IBAN, e-mails…), pas par leur nom.</summary>
    private const int MaxCustomerPages = 10;

    public Task<string> GetCompanyJsonAsync(DossierId dossier, CancellationToken cancellationToken)
    {
        // Les paramètres de l'entreprise du dossier (forme, régime de TVA, exercice) arrivent avec la base Catalog
        // et le module Dossier ; d'ici là, l'assistant le sait et demande ce qui lui manque.
        string json = JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_note"] = "Paramètres de l'entreprise non encore renseignés dans Lettra : demander à l'utilisateur ce qui manque.",
        }, AiToolResult.JsonOptions);
        return Task.FromResult(json);
    }

    public async Task<IReadOnlyList<KnownIdentity>> GetKnownIdentitiesAsync(DossierId dossier, CancellationToken cancellationToken)
    {
        List<KnownIdentity> identities = [];
        for (int page = 1; page <= MaxCustomerPages; page++)
        {
            Result<PagedResponse<CustomerSummary>> result = await queries
                .QueryAsync(new ListCustomersQuery(dossier, page, ListCustomersQuery.MaxPageSize), cancellationToken)
                .ConfigureAwait(false);
            if (result.IsFailure)
            {
                // Sans droit de lecture des tiers, l'assistant ne peut pas lire les clients : seuls les détecteurs s'appliquent.
                break;
            }

            foreach (CustomerSummary customer in result.Value.Items)
            {
                identities.Add(new KnownIdentity(customer.Kind == CustomerKindDto.Individual ? IdentityKind.Person : IdentityKind.Client, customer.Name));
                if (customer.Siren is { Length: > 0 } siren)
                {
                    identities.Add(new KnownIdentity(IdentityKind.Siren, siren));
                }
            }

            if (page * ListCustomersQuery.MaxPageSize >= result.Value.TotalCount)
            {
                break;
            }
        }

        return identities;
    }
}
