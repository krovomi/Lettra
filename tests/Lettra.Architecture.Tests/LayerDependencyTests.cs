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
        { "Lettra.Application", ["Lettra.Calculations", "Lettra.Contracts", "Lettra.Domain"] },
        { "Lettra.Infrastructure.Persistence", ["Lettra.Application", "Lettra.Calculations", "Lettra.Contracts", "Lettra.Domain"] },
        { "Lettra.Infrastructure.Persistence.Sqlite", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain", "Lettra.Infrastructure.Persistence"] },
        { "Lettra.Infrastructure.Persistence.PostgreSql", ["Lettra.Application", "Lettra.Domain", "Lettra.Infrastructure.Persistence"] },
        { "Lettra.Infrastructure.Persistence.SqlServer", ["Lettra.Application", "Lettra.Domain", "Lettra.Infrastructure.Persistence"] },
        { "Lettra.Infrastructure.Catalog", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain", "Lettra.Infrastructure.Persistence"] },
        { "Lettra.Infrastructure.Identity.Local", ["Lettra.Application", "Lettra.Domain"] },
        { "Lettra.Infrastructure.Identity.Oidc", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain"] },
        { "Lettra.Infrastructure.External", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain"] },
        { "Lettra.Infrastructure.Observability", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain"] },
        { "Lettra.Supervision.Domain", ["Lettra.Calculations", "Lettra.Domain"] },
        { "Lettra.Supervision.Application", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain", "Lettra.Supervision.Domain"] },
        { "Lettra.Supervision.Infrastructure", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain", "Lettra.Infrastructure.Persistence", "Lettra.Supervision.Application", "Lettra.Supervision.Domain"] },
        // Synchronisation (phase 4) : des cas d'usage autour des ports de l'application, sans accès direct aux bases.
        { "Lettra.Sync.Server", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain"] },
        { "Lettra.Sync.Client", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain"] },
        { "Lettra.UI.Abstractions", ["Lettra.Contracts"] },
        { "Lettra.UI.Shared", ["Lettra.Calculations", "Lettra.Contracts", "Lettra.Printing", "Lettra.UI.Abstractions"] },
        // Rendu PDF (phase 3) : ne voit que les calculs partagés, ni les contrats ni le domaine.
        { "Lettra.Printing", ["Lettra.Calculations"] },
        { "Lettra.UI.Gateway.Http", ["Lettra.Contracts", "Lettra.UI.Abstractions"] },
        { "Lettra.UI.Gateway.InProcess", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain", "Lettra.Supervision.Application", "Lettra.Supervision.Domain", "Lettra.UI.Abstractions"] },

        // Couche IA (annexe B §4.1) : des adapters des ports de l'application, sans accès direct aux bases.
        { "Lettra.Ai.Skills", ["Lettra.Application"] },
        { "Lettra.Ai.Privacy", ["Lettra.Application"] },
        { "Lettra.Ai.Tools", ["Lettra.Application", "Lettra.Contracts", "Lettra.Domain"] },
        { "Lettra.Ai.Orchestration", ["Lettra.Ai.Privacy", "Lettra.Application", "Lettra.Contracts", "Lettra.Domain"] },
        { "Lettra.Ai.Claude", ["Lettra.Application"] },
        { "Lettra.Ai.Local", ["Lettra.Application"] },
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

    [Fact]
    public void La_couche_IA_passe_par_les_cas_d_usage_et_jamais_par_les_bases()
    {
        foreach (Assembly assembly in new[] { Assemblies.AiSkills, Assemblies.AiPrivacy, Assemblies.AiTools, Assemblies.AiOrchestration, Assemblies.AiClaude, Assemblies.AiLocal })
        {
            Types.InAssembly(assembly)
                .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Lettra.Infrastructure", "Lettra.UI")
                .GetResult().FailingTypeNames.Should().BeNullOrEmpty(because: $"{assembly.GetName().Name} : les outils de l'IA appellent les cas d'usage, avec les droits de l'utilisateur (ADR-0006)");
        }
    }

    [Fact]
    public void La_synchronisation_passe_par_les_cas_d_usage_et_jamais_par_les_bases()
    {
        foreach (Assembly assembly in new[] { Assemblies.SyncServer, Assemblies.SyncClient })
        {
            Types.InAssembly(assembly)
                .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Lettra.Infrastructure", "Lettra.UI")
                .GetResult().FailingTypeNames.Should().BeNullOrEmpty(because: $"{assembly.GetName().Name} : une intention est rejouée par son cas d'usage, avec les droits du moment (ADR-0008)");
        }
    }

    [Fact]
    public void Seul_l_adapter_Claude_connait_le_SDK_Anthropic() =>
        Assemblies.All
            .Where(a => a != Assemblies.AiClaude)
            .Where(a => a.GetReferencedAssemblies().Any(r => r.Name == "Anthropic"))
            .Select(a => a.GetName().Name)
            .Should().BeEmpty(because: "le moteur est derrière le port IAiEngine (ADR-0012)");
}
