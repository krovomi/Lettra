using System.Reflection;
using NetArchTest.Rules;

namespace Lettra.Architecture.Tests;

/// <summary>
/// Les hôtes (Api, Web, Worker) et le jeu de démonstration, hors de la matrice des couches : ce sont des racines de
/// composition, qui assemblent l'infrastructure, mais aucun n'accède à une base sans passer par l'application. MAUI n'est
/// compilé qu'en CI (charges de travail mobiles) : ses règles sont les mêmes, vérifiées à la revue.
/// </summary>
public sealed class HostRulesTests
{
    private static readonly string[] Providers =
        ["Lettra.Infrastructure.Persistence.Sqlite", "Lettra.Infrastructure.Persistence.PostgreSql", "Lettra.Infrastructure.Persistence.SqlServer"];

    /// <summary>Les assemblies Lettra que chaque hôte peut voir. Toute nouvelle flèche est ajoutée ici, donc revue.</summary>
    public static TheoryData<string, string[]> AllowedReferences => new()
    {
        {
            "Lettra.Api",
            [
                "Lettra.Application", "Lettra.Bootstrap", "Lettra.Contracts", "Lettra.DemoData", "Lettra.Domain", "Lettra.Exports.Application",
                "Lettra.Exports.Infrastructure", "Lettra.Infrastructure.Catalog", "Lettra.Infrastructure.Identity.Oidc",
                "Lettra.Infrastructure.Observability", "Lettra.Infrastructure.Persistence", "Lettra.Supervision.Application",
                "Lettra.Supervision.Domain", "Lettra.Supervision.Infrastructure", "Lettra.Sync.Server",
            ]
        },
        // Le navigateur ne voit que les écrans et leur passerelle HTTP : ni domaine, ni application, ni infrastructure.
        { "Lettra.Web", ["Lettra.UI.Abstractions", "Lettra.UI.Gateway.Http", "Lettra.UI.Shared"] },
        {
            "Lettra.Worker",
            [
                "Lettra.Application", "Lettra.Bootstrap", "Lettra.Domain", "Lettra.Exports.Infrastructure", "Lettra.Infrastructure.Catalog",
                "Lettra.Infrastructure.Persistence", "Lettra.Supervision.Infrastructure",
            ]
        },
        // Le jeu de démonstration se remplit par les cas d'usage et les passerelles en processus, jamais par l'infrastructure.
        {
            "Lettra.DemoData",
            [
                "Lettra.Application", "Lettra.Bootstrap", "Lettra.Contracts", "Lettra.Domain", "Lettra.Supervision.Application",
                "Lettra.Supervision.Domain", "Lettra.UI.Abstractions", "Lettra.UI.Gateway.InProcess",
            ]
        },
    };

    [Theory]
    [MemberData(nameof(AllowedReferences))]
    public void Chaque_hote_ne_reference_que_ses_dependances_autorisees(string assemblyName, string[] allowed)
    {
        Assembly assembly = Assemblies.Hosts.Single(a => a.GetName().Name == assemblyName);

        Assemblies.LettraReferencesOf(assembly).Should().BeSubsetOf(allowed, because: $"{assemblyName} ne doit dépendre que de {string.Join(", ", allowed)}");
    }

    [Fact]
    public void Tous_les_hotes_sont_couverts() =>
        Assemblies.Hosts.Select(a => a.GetName().Name).Should().BeEquivalentTo(AllowedReferences.Select(row => (string)row[0]));

    [Fact]
    public void Aucun_hote_ne_connait_ef_ni_un_fournisseur_de_base()
    {
        foreach (Assembly assembly in Assemblies.Hosts)
        {
            Types.InAssembly(assembly)
                .ShouldNot().HaveDependencyOnAny(["Microsoft.EntityFrameworkCore", .. Providers])
                .GetResult().FailingTypeNames.Should().BeNullOrEmpty(because: $"{assembly.GetName().Name} : le fournisseur est choisi par Lettra.Bootstrap");
        }
    }

    [Fact]
    public void Les_endpoints_passent_par_l_application_et_jamais_par_l_infrastructure()
    {
        // Écarts connus (X18 de docs/qualite-du-code.md), à retirer quand ces endpoints passent par le médiateur.
        string[] known = ["Lettra.Api.Endpoints.KpiEndpoints"];

        IEnumerable<string> failing = Types.InAssembly(Assemblies.Api)
            .That().ResideInNamespaceStartingWith("Lettra.Api.Endpoints")
            .ShouldNot().HaveDependencyOn("Lettra.Infrastructure")
            .GetResult().FailingTypeNames ?? [];

        failing.Select(name => name.Split('+')[0]).Distinct().Except(known).Should().BeEmpty();
    }
}
