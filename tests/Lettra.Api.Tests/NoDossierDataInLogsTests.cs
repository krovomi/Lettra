using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>
/// Test de CI de l'annexe G : aucune donnée de dossier dans les journaux techniques, à tous les niveaux
/// (y compris les traces SQL d'EF Core), sur le parcours nominal comme sur les refus.
/// </summary>
public sealed class NoDossierDataInLogsTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    [Fact]
    public async Task Aucune_donnee_saisie_n_apparait_dans_les_journaux()
    {
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());

        CreateCustomerRequest valid = new()
        {
            Name = "Fromagerie Quenelle",
            Siret = "55210055400013",
            VatNumber = "FR96552100554",
            Email = "secret@quenelle.fr",
            AddressLine1 = "12 quai Saint-Antoine",
            PostalCode = "69002",
            City = "Lyon",
        };
        CreateCustomerRequest invalid = valid with { Siret = "55210055400014", Email = "secret-invalide@quenelle" };

        (await client.PostAsJsonAsync("/api/v1/customers", valid, ContractsJsonContext.Default.CreateCustomerRequest)).EnsureSuccessStatusCode();
        await client.PostAsJsonAsync("/api/v1/customers", invalid, ContractsJsonContext.Default.CreateCustomerRequest);
        await client.GetAsync(new Uri("/api/v1/customers", UriKind.Relative));

        factory.Logs.Should().Contain(l => l.Contains("CreateCustomerCommand", StringComparison.Ordinal), "le cas d'usage est bien journalisé");
        string[] dossierData = ["Fromagerie", "Quenelle", "552100554", "55210055400013", "55210055400014", "FR96552100554", "secret", "quai Saint-Antoine", "69002", "Lyon"];
        factory.Logs.Should().AllSatisfy(entry => entry.Should().NotContainAny(dossierData));
    }
}
