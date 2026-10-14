using System.Reflection;
using NetArchTest.Rules;

namespace Lettra.Architecture.Tests;

/// <summary>
/// La matrice des dépendances autorisées entre assemblies Lettra (annexe A §1). Toute nouvelle flèche
/// doit être ajoutée ici, donc revue : c'est le garde-fou de l'architecture hexagonale.
/// </summary>
public sealed class LayerDependencyTests
{
    public static TheoryData<string, string[]> AllowedReferences => new()
    {
        { "Lettra.Calculations", [] },
        { "Lettra.Contracts", [] },
        { "Lettra.Domain", ["Lettra.Calculations"] },
        { "Lettra.Application", ["Lettra.Contracts", "Lettra.Domain"] },
        { "Lettra.Infrastructure.Persistence", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain"] },
        { "Lettra.Infrastructure.Persistence.Sqlite", ["Lettra.Application", "Lettra.Domain", "Lettra.Infrastructure.Persistence"] },
        { "Lettra.Infrastructure.Persistence.PostgreSql", ["Lettra.Application", "Lettra.Domain", "Lettra.Infrastructure.Persistence"] },
        { "Lettra.Infrastructure.Persistence.SqlServer", ["Lettra.Application", "Lettra.Domain", "Lettra.Infrastructure.Persistence"] },
        { "Lettra.Infrastructure.Catalog", ["Lettra.Application", "Lettra.Domain", "Lettra.Infrastructure.Persistence"] },
        { "Lettra.Infrastructure.Identity.Local", ["Lettra.Application", "Lettra.Domain"] },
        { "Lettra.Infrastructure.Identity.Oidc", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain"] },
        { "Lettra.Infrastructure.External", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain"] },
        { "Lettra.Infrastructure.Observability", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain"] },
        { "Lettra.Supervision.Domain", ["Lettra.Calculations", "Lettra.Domain"] },
        { "Lettra.Supervision.Application", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain", "Lettra.Supervision.Domain"] },
        { "Lettra.Supervision.Infrastructure", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain", "Lettra.Supervision.Application", "Lettra.Supervision.Domain"] },
        { "Lettra.UI.Abstractions", ["Lettra.Contracts"] },
        { "Lettra.UI.Shared", ["Lettra.Calculations", "Lettra.Contracts", "Lettra.UI.Abstractions"] },
        { "Lettra.UI.Gateway.Http", ["Lettra.Contracts", "Lettra.UI.Abstractions"] },
        { "Lettra.UI.Gateway.InProcess", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain", "Lettra.UI.Abstractions"] },
    };

    [Theory]
    [MemberData(nameof(AllowedReferences))]
    public void Chaque_assembly_ne_reference_que_ses_dependances_autorisees(string assemblyName, string[] allowed)
    {
        Assembly assembly = Assemblies.All.Single(a => a.GetName().Name == assemblyName);

        Assemblies.LettraReferencesOf(assembly).Should().BeSubsetOf(allowed,
            because: $"{assemblyName} ne doit dépendre que de {string.Join(", ", allowed)} (annexe A §1)");
    }

    [Fact]
    public void Toutes_les_assemblies_sont_couvertes_par_la_matrice()
    {
        IEnumerable<string> covered = AllowedReferences.Select(row => (string)row[0]).Append("Lettra.Bootstrap");

        Assemblies.All.Select(a => a.GetName().Name).Should().BeEquivalentTo(covered);
    }

    [Fact]
    public void Les_domaines_n_utilisent_aucun_framework()
    {
        foreach (Assembly assembly in new[] { Assemblies.Domain, Assemblies.SupervisionDomain })
        {
            Types.InAssembly(assembly)
                .ShouldNot().HaveDependencyOnAny("Microsoft", "System.Text.Json", "System.Net.Http", "System.Data")
                .GetResult().FailingTypeNames.Should().BeNullOrEmpty(because: assembly.GetName().Name);
        }
    }

    [Fact]
    public void Le_coeur_ne_connait_pas_les_modules()
    {
        foreach (Assembly assembly in new[] { Assemblies.Domain, Assemblies.Application, Assemblies.Contracts })
        {
            Assemblies.LettraReferencesOf(assembly)
                .Should().NotContain(name => name.StartsWith("Lettra.Supervision.", StringComparison.Ordinal),
                    because: $"{assembly.GetName().Name} est partagé par tous les modules (ADR-0001)");
        }
    }

    [Fact]
    public void Les_contrats_et_l_ui_ne_voient_jamais_le_domaine_ni_ef()
    {
        foreach (Assembly assembly in new[] { Assemblies.Contracts, Assemblies.UiAbstractions, Assemblies.UiShared, Assemblies.UiGatewayHttp })
        {
            Types.InAssembly(assembly)
                .ShouldNot().HaveDependencyOnAny("Lettra.Domain", "Lettra.Application", "Microsoft.EntityFrameworkCore")
                .GetResult().FailingTypeNames.Should().BeNullOrEmpty(because: assembly.GetName().Name);
        }
    }

    [Fact]
    public void L_application_ne_connait_ni_ef_ni_aspnet() =>
        Types.InAssembly(Assemblies.Application)
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Lettra.Infrastructure", "Lettra.UI")
            .GetResult().FailingTypeNames.Should().BeNullOrEmpty();

    [Fact]
    public void Seule_la_composition_root_connait_les_providers()
    {
        string[] providers = ["Lettra.Infrastructure.Persistence.Sqlite", "Lettra.Infrastructure.Persistence.PostgreSql", "Lettra.Infrastructure.Persistence.SqlServer"];

        Assemblies.All
            .Where(a => a != Assemblies.Bootstrap && !providers.Contains(a.GetName().Name))
            .Where(a => Assemblies.LettraReferencesOf(a).Intersect(providers).Any())
            .Select(a => a.GetName().Name)
            .Should().BeEmpty();
    }
}
