using System.Net;
using System.Net.Http.Json;
using Lettra.Application.Abstractions.External;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Directory;
using Lettra.Contracts.Serialization;
using Lettra.Domain.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>Contrôle des clients dans l'annuaire par l'API réelle : seuls les SIREN partent, les cessés reviennent.</summary>
public sealed class CustomerDirectoryCheckApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    [Fact]
    public async Task Un_client_cesse_revient_dans_le_controle_avec_son_identifiant()
    {
        Directory directory = new();
        using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ICompanyDirectory>(directory)));
        using HttpClient client = host.CreateClient();
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        CreateCustomerRequest request = CustomerApiTests.Martin();
        request.Name = "Boulangerie radiée";
        request.Siret = null;
        request.Siren = "443061841";
        using HttpResponseMessage created = await client.PostAsJsonAsync("/api/v1/customers", request, ContractsJsonContext.Default.CreateCustomerRequest);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        CustomerResponse customer = (await created.Content.ReadFromJsonAsync(ContractsJsonContext.Default.CustomerResponse))!;
        directory.Ceased.Add("443061841");

        CustomerDirectoryCheckResponse check = (await client.GetFromJsonAsync("/api/v1/directory/customer-check", ContractsJsonContext.Default.CustomerDirectoryCheckResponse))!;

        check.Complete.Should().BeTrue();
        check.Checked.Should().BeGreaterThan(0);
        check.Findings.Should().ContainSingle(f => f.CustomerId == customer.Id)
            .Which.Should().Be(new CustomerDirectoryFinding(customer.Id, "Boulangerie radiée", "443061841", DirectoryStatusDto.Ceased));
        directory.Queries.Should().OnlyContain(q => q.Length == 9 && q.All(char.IsAsciiDigit), "seuls les SIREN quittent le dossier");
    }

    private sealed class Directory : ICompanyDirectory
    {
        public HashSet<string> Ceased { get; } = [];

        public List<string> Queries { get; } = [];

        public Task<Result<CompanySearchResponse>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            CompanySearchResult company = new(query, null, "X", null, null, null, null, "FR", "FR00" + query, null, !Ceased.Contains(query));
            return Task.FromResult<Result<CompanySearchResponse>>(new CompanySearchResponse([company], 1));
        }
    }
}
