using Lettra.Application.Abstractions.External;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Features;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Directory;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Tests;

/// <summary>Contrôle des clients dans l'annuaire SIRENE : cessés, inconnus, annuaire en panne, plafond.</summary>
public sealed class CustomerDirectoryCheckTests
{
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());

    private static CustomerSummary Company(string name, string? siren) => new(Guid.CreateVersion7(), CustomerKindDto.Company, name, siren, "Lyon");

    private static Task<Result<CustomerDirectoryCheckResponse>> CheckAsync(Customers store, Directory directory) =>
        new CheckCustomersInDirectoryHandler(store, directory).HandleAsync(new CheckCustomersInDirectoryQuery(Dossier), default);

    [Fact]
    public async Task Les_clients_cesses_et_les_SIREN_inconnus_sont_signales()
    {
        Customers store = new([Company("Zinc Actif", "111111111"), Company("Boulangerie Fermée", "222222222"),
            Company("Faute de frappe", "333333333"), Company("Sans SIREN", null)]);
        Directory directory = new() { Ceased = { "222222222" }, Unknown = { "333333333" } };

        Result<CustomerDirectoryCheckResponse> result = await CheckAsync(store, directory);

        result.Value.Checked.Should().Be(3);
        result.Value.Complete.Should().BeTrue();
        result.Value.Findings.Select(f => (f.Name, f.Status)).Should().Equal(
            ("Boulangerie Fermée", DirectoryStatusDto.Ceased), ("Faute de frappe", DirectoryStatusDto.NotFound));
        directory.Queries.Should().Equal("111111111", "222222222", "333333333");
        store.Filters.Should().OnlyContain(f => f.Kind == CustomerKind.Company && f.PageSize == Paging.MaxPageSize);
    }

    [Fact]
    public async Task Un_annuaire_injoignable_des_le_depart_rend_son_erreur()
    {
        Result<CustomerDirectoryCheckResponse> result = await CheckAsync(new Customers([Company("A", "111111111")]), new Directory { FailAfter = 0 });

        result.IsFailure.Should().BeTrue();
        result.Errors[0].Code.Should().Be(DirectoryErrors.UnavailableCode);
    }

    [Fact]
    public async Task Un_annuaire_qui_tombe_en_route_rend_un_controle_partiel()
    {
        Customers store = new([.. Enumerable.Range(0, 8).Select(i => Company($"C{i}", $"{111_111_110 + i}"))]);

        Result<CustomerDirectoryCheckResponse> result = await CheckAsync(store, new Directory { FailAfter = 2, Ceased = { "111111110" } });

        result.Value.Checked.Should().Be(2);
        result.Value.Complete.Should().BeFalse();
        result.Value.Findings.Should().ContainSingle().Which.Name.Should().Be("C0");
    }

    [Fact]
    public async Task Une_reponse_manquee_n_empeche_pas_de_controler_les_suivants()
    {
        Customers store = new([Company("A", "111111111"), Company("B", "222222222"), Company("C", "333333333")]);
        Directory directory = new() { FailingSirens = { "222222222" }, Ceased = { "333333333" } };

        Result<CustomerDirectoryCheckResponse> result = await CheckAsync(store, directory);

        result.Value.Checked.Should().Be(2);
        result.Value.Complete.Should().BeFalse();
        result.Value.Findings.Should().ContainSingle().Which.Name.Should().Be("C");
    }

    [Fact]
    public async Task Le_controle_parcourt_les_pages_jusqu_au_plafond()
    {
        Customers store = new([.. Enumerable.Range(0, 650).Select(i => Company($"C{i:000}", $"{100_000_000 + i}"))]);
        Directory directory = new();

        Result<CustomerDirectoryCheckResponse> result = await CheckAsync(store, directory);

        result.Value.Checked.Should().Be(CheckCustomersInDirectoryQuery.MaxCustomers);
        store.Filters.Select(f => f.Page).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Le_controle_demande_la_consultation_des_tiers() =>
        new CheckCustomersInDirectoryQuery(Dossier).Permission.Should().Be(Abstractions.Identity.Permission.TiersConsulter);

    private sealed class Customers(List<CustomerSummary> all) : ICustomerReadStore
    {
        public List<PartyListFilter> Filters { get; } = [];

        public Task<PagedResponse<CustomerSummary>> ListAsync(PartyListFilter filter, CancellationToken cancellationToken)
        {
            Filters.Add(filter);
            List<CustomerSummary> page = [.. all.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)];
            return Task.FromResult(new PagedResponse<CustomerSummary>(page, filter.Page, filter.PageSize, all.Count));
        }

        public Task<CustomerResponse?> GetAsync(CustomerId id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Directory : ICompanyDirectory
    {
        public HashSet<string> Ceased { get; } = [];

        public HashSet<string> Unknown { get; } = [];

        public int FailAfter { get; init; } = int.MaxValue;

        public HashSet<string> FailingSirens { get; } = [];

        public List<string> Queries { get; } = [];

        public Task<Result<CompanySearchResponse>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
        {
            if (Queries.Count >= FailAfter || FailingSirens.Contains(query))
            {
                Queries.Add("échec");
                return Task.FromResult<Result<CompanySearchResponse>>(DirectoryErrors.Unavailable);
            }

            Queries.Add(query);
            List<CompanySearchResult> items = Unknown.Contains(query) ? []
                : [new CompanySearchResult(query, null, "X", null, null, null, null, "FR", "FR00" + query, null, !Ceased.Contains(query))];
            return Task.FromResult<Result<CompanySearchResponse>>(new CompanySearchResponse(items, items.Count));
        }
    }
}
