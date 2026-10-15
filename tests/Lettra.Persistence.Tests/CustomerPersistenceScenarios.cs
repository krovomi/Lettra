using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>Scénarios communs, joués à l'identique sur chaque provider.</summary>
internal static class CustomerPersistenceScenarios
{
    private static readonly Customer.Draft Martin = new(
        CustomerKind.Company, "Boulangerie Martin", null, "73282932000074", "FR44732829320", "compta@martin.fr",
        "1 rue de la Paix", "Bâtiment B", "75002", "Paris", "FR", PreferredLanguage: "en-us");

    private static readonly Customer.Draft Dupont = new(
        CustomerKind.Individual, "Jeanne Dupont", null, null, null, null, "Rue Neuve 1", null, "1000", "Bruxelles", "BE");

    /// <summary>Aller-retour complet : tous les objets-valeurs sont relus à l'identique.</summary>
    public static async Task RoundTripAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        CustomerId id;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            Result<CustomerId> created = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                .SendAsync(new CreateCustomerCommand(dossier, Martin), default);
            created.IsSuccess.Should().BeTrue();
            id = created.Value;
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            Result<CustomerResponse> read = await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
                .QueryAsync(new GetCustomerQuery(dossier, id), default);

            read.Value.Should().BeEquivalentTo(new
            {
                Id = id.Value,
                Kind = CustomerKindDto.Company,
                Name = "Boulangerie Martin",
                Siren = "732829320",
                Siret = "73282932000074",
                VatNumber = "FR44732829320",
                Email = "compta@martin.fr",
                AddressLine1 = "1 rue de la Paix",
                AddressLine2 = "Bâtiment B",
                PostalCode = "75002",
                City = "Paris",
                CountryCode = "FR",
                PreferredLanguage = "en-US",
            });
        }
    }

    /// <summary>Une base par dossier : ce qui est écrit dans A n'apparaît jamais dans B.</summary>
    public static async Task IsolationAsync(TenantHarness harness, DatabaseProvider provider, string connectionStringA, string connectionStringB)
    {
        DossierId a = harness.AddDossier(provider, connectionStringA);
        DossierId b = harness.AddDossier(provider, connectionStringB);

        await using (AsyncServiceScope scope = await harness.OpenAsync(a))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            (await commands.SendAsync(new CreateCustomerCommand(a, Martin), default)).IsSuccess.Should().BeTrue();
            (await commands.SendAsync(new CreateCustomerCommand(a, Dupont), default)).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(b))
        {
            IServiceProvider sp = scope.ServiceProvider;
            Result<PagedResponse<CustomerSummary>> listB = await sp.GetRequiredService<IQueryDispatcher>().QueryAsync(new ListCustomersQuery(b), default);
            listB.Value.TotalCount.Should().Be(0);

            // Même ouvert sur B, viser A est refusé par le pipeline avant toute requête SQL.
            Result<PagedResponse<CustomerSummary>> crossing = await sp.GetRequiredService<IQueryDispatcher>().QueryAsync(new ListCustomersQuery(a), default);
            crossing.Errors.Should().ContainSingle().Which.Code.Should().Be(TenancyErrors.DossierMismatchCode);
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(a))
        {
            Result<PagedResponse<CustomerSummary>> listA = await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
                .QueryAsync(new ListCustomersQuery(a, Page: 1, PageSize: 1), default);
            listA.Value.TotalCount.Should().Be(2);
            listA.Value.Items.Should().ContainSingle().Which.Name.Should().Be("Boulangerie Martin");
        }
    }
}
