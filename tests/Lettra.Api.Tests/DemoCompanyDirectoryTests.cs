using Lettra.Application.Abstractions.External;
using Lettra.Contracts.Directory;
using Lettra.DemoData;
using Lettra.Domain.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>Annuaire de la démonstration : les entreprises fictives du jeu, des clients cessés, et l'annuaire réel pour le reste.</summary>
public sealed class DemoCompanyDirectoryTests
{
    private static readonly DemoDirectoryData Data = DemoDirectoryData.Build(new DemoDataOptions());

    [Fact]
    public void Le_jeu_donne_les_entreprises_des_dossiers_leurs_clients_et_quelques_cesses()
    {
        Data.Companies.Should().HaveCountGreaterThan(50);
        Data.Companies.Values.Should().Contain(c => !c.IsActive).And.Contain(c => c.IsActive);
        Data.Companies.Keys.Should().OnlyContain(s => s.Length == 9 && s[0] == '9');
        Data.Companies.Values.Should().OnlyContain(c => c.VatNumber.EndsWith(c.Siren, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_SIREN_de_la_demo_repond_sans_appeler_l_annuaire_reel()
    {
        Recording real = new();
        CompanySearchResult ceased = Data.Companies.Values.First(c => !c.IsActive);

        Result<CompanySearchResponse> found = await new DemoCompanyDirectory(Data, real).SearchAsync(ceased.Siren, 1, default);

        found.Value.Items.Should().ContainSingle().Which.Should().Be(ceased);
        real.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_SIREN_inconnu_de_la_demo_part_vers_l_annuaire_reel()
    {
        Recording real = new() { Answer = [FakeCompany("552032534", "DANONE")] };

        Result<CompanySearchResponse> found = await new DemoCompanyDirectory(Data, real).SearchAsync("552032534", 1, default);

        found.Value.Items.Should().ContainSingle().Which.Name.Should().Be("DANONE");
        real.Queries.Should().Equal("552032534");
    }

    [Fact]
    public async Task Une_recherche_par_nom_met_la_demo_d_abord_et_survit_a_un_annuaire_reel_en_panne()
    {
        CompanySearchResult any = Data.Companies.Values.First();
        string word = any.Name.Split(' ')[0].ToLowerInvariant();

        Result<CompanySearchResponse> online = await new DemoCompanyDirectory(Data, new Recording { Answer = [FakeCompany("552032534", "AUTRE")] })
            .SearchAsync(word, 200, default);
        Result<CompanySearchResponse> offline = await new DemoCompanyDirectory(Data, new Recording { Fail = true }).SearchAsync(word, 25, default);
        Result<CompanySearchResponse> nothing = await new DemoCompanyDirectory(Data, new Recording { Fail = true }).SearchAsync("zzzz introuvable", 5, default);

        online.Value.Items[0].Siren.Should().StartWith("9");
        online.Value.Items[^1].Name.Should().Be("AUTRE");
        offline.Value.Items.Should().NotBeEmpty().And.OnlyContain(c => Data.Companies.ContainsKey(c.Siren));
        nothing.IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData("true", "Demo", true)]
    [InlineData("true", "Production", false)]
    [InlineData("false", "Demo", false)]
    public void L_annuaire_de_demo_ne_se_branche_que_si_le_jeu_est_demande_hors_production(string seed, string environment, bool expected)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Lettra:Demo:Seed"] = seed }).Build();
        ServiceCollection services = new();
        services.AddSingleton<ICompanyDirectory>(new Recording());

        services.AddDemoCompanyDirectory(configuration, environment);

        using ServiceProvider provider = services.BuildServiceProvider();
        (provider.GetRequiredService<ICompanyDirectory>() is DemoCompanyDirectory).Should().Be(expected);
    }

    private static CompanySearchResult FakeCompany(string siren, string name) =>
        new(siren, null, name, null, null, null, null, "FR", "FR00" + siren, null, true);

    private sealed class Recording : ICompanyDirectory
    {
        public List<CompanySearchResult> Answer { get; init; } = [];

        public bool Fail { get; init; }

        public List<string> Queries { get; } = [];

        public Task<Result<CompanySearchResponse>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return Task.FromResult(Fail ? DirectoryErrors.Unavailable : (Result<CompanySearchResponse>)new CompanySearchResponse(Answer, Answer.Count));
        }
    }
}
