using System.Reflection;
using NetArchTest.Rules;

namespace Lettra.Architecture.Tests;

/// <summary>
/// Les couches Application des modules suivent la règle de <c>Lettra.Application</c> : des cas d'usage autour de ports,
/// sans EF, sans ASP.NET, sans infrastructure ni écran (ADR-0001).
/// </summary>
public sealed class ApplicationLayerRulesTests
{
    [Fact]
    public void Les_couches_application_des_modules_ne_connaissent_ni_ef_ni_aspnet()
    {
        foreach (Assembly assembly in new[] { Assemblies.ExportsApplication, Assemblies.SupervisionApplication })
        {
            Types.InAssembly(assembly)
                .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Lettra.Infrastructure", "Lettra.UI")
                .GetResult().FailingTypeNames.Should().BeNullOrEmpty(because: assembly.GetName().Name);
        }
    }
}
