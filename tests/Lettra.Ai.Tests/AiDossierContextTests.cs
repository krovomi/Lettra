using Lettra.Ai.Tools;
using Lettra.Ai.Tools.Company;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Common;

namespace Lettra.Ai.Tests;

/// <summary>Les identités à masquer sont lues par les cas d'usage, donc avec les droits de l'utilisateur.</summary>
public sealed class AiDossierContextTests
{
    [Fact]
    public async Task Clients_et_fournisseurs_du_dossier_sont_masques_avec_leur_SIREN()
    {
        FakeQueries queries = new(allowed: true);

        IReadOnlyList<KnownIdentity> identities = await new AiDossierContext(queries, new NoCompanyFacts()).GetKnownIdentitiesAsync(AssistantHarness.Dossier, CancellationToken.None);

        identities.Should().BeEquivalentTo(new KnownIdentity[]
        {
            new(IdentityKind.Client, "Dupont SARL"),
            new(IdentityKind.Siren, "987654321"),
            new(IdentityKind.Person, "Jean Martin"),
            new(IdentityKind.Client, "Imprimerie Lumière"),
            new(IdentityKind.Siren, "552100554"),
        });
    }

    [Fact]
    public async Task Sans_droit_de_lecture_des_tiers_seuls_les_detecteurs_s_appliquent()
    {
        IReadOnlyList<KnownIdentity> identities = await new AiDossierContext(new FakeQueries(allowed: false), new NoCompanyFacts())
            .GetKnownIdentitiesAsync(AssistantHarness.Dossier, CancellationToken.None);

        identities.Should().BeEmpty();
    }

    private sealed class FakeQueries(bool allowed) : IQueryDispatcher
    {
        public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
        {
            object result = (query, allowed) switch
            {
                (_, false) when query is ListCustomersQuery => (Result<PagedResponse<CustomerSummary>>)SecurityErrors.Forbidden,
                (_, false) => (Result<PagedResponse<SupplierSummary>>)SecurityErrors.Forbidden,
                (ListCustomersQuery, _) => Result<PagedResponse<CustomerSummary>>.Success(new(
                    [new(Guid.NewGuid(), CustomerKindDto.Company, "Dupont SARL", "987654321", "Lyon"), new(Guid.NewGuid(), CustomerKindDto.Individual, "Jean Martin", null, "Lyon")],
                    1,
                    200,
                    2)),
                _ => Result<PagedResponse<SupplierSummary>>.Success(new([new(Guid.NewGuid(), "Imprimerie Lumière", "552100554", "Paris")], 1, 200, 1)),
            };
            return Task.FromResult((TResult)result);
        }
    }
}
