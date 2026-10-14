using System.Reflection;

namespace Lettra.Ai.Tests;

/// <summary>Règles de dépendances (annexe B §4.1) : le domaine et l'application ne connaissent aucun SDK d'IA.</summary>
public sealed class ArchitectureTests
{
    private static IEnumerable<string> References(string assembly) =>
        Assembly.Load(assembly).GetReferencedAssemblies().Select(a => a.Name!);

    [Theory]
    [InlineData("Lettra.Compliance")]
    [InlineData("Lettra.Poc.Application")]
    public void Domaine_et_application_ne_dependent_d_aucune_brique_IA(string assembly) =>
        Assert.DoesNotContain(References(assembly), r => r.StartsWith("Lettra.Ai") || r.StartsWith("Anthropic"));

    [Theory]
    [InlineData("Lettra.Ai.Abstractions")]
    [InlineData("Lettra.Ai.Orchestration")]
    [InlineData("Lettra.Ai.Privacy")]
    [InlineData("Lettra.Ai.Tools")]
    [InlineData("Lettra.Ai.Skills")]
    public void Seul_l_adapter_Claude_connait_le_SDK_Anthropic(string assembly) =>
        Assert.DoesNotContain(References(assembly), r => r.StartsWith("Anthropic"));

    [Fact]
    public void L_orchestrateur_ne_connait_aucun_moteur_concret() =>
        Assert.DoesNotContain(References("Lettra.Ai.Orchestration"), r => r is "Lettra.Ai.Claude" or "Lettra.Ai.Local" or "Lettra.Ai.Stub");
}
