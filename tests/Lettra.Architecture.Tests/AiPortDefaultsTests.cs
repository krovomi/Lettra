using System.Reflection;
using Lettra.Application;
using Lettra.Application.Abstractions.Ai;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Architecture.Tests;

/// <summary>
/// Sans IA, Lettra marche entièrement (§9 de la proposition « IA qui anticipe ») : chaque port d'IA dont dépend un cas
/// d'usage a une implémentation « None » enregistrée par l'application elle-même, sans la couche IA, et cette
/// implémentation coupe l'IA. Un port d'IA ajouté plus tard sans implémentation par défaut fait échouer ce test.
/// </summary>
public sealed class AiPortDefaultsTests
{
    private const string AiPortsNamespace = "Lettra.Application.Abstractions.Ai";

    /// <summary>Les interfaces de l'espace de noms de l'IA que reçoit un constructeur de l'application ou d'un module.</summary>
    private static IReadOnlyList<Type> Ports { get; } =
    [
        .. new[] { Assemblies.Application, Assemblies.ExportsApplication, Assemblies.SupervisionApplication, Assemblies.UiGatewayInProcess }
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .Where(t => t.IsInterface && t.Namespace == AiPortsNamespace)
            .Distinct()
            .OrderBy(t => t.Name, StringComparer.Ordinal),
    ];

    [Fact]
    public void Chaque_port_d_IA_de_l_application_a_une_implementation_par_defaut_sans_la_couche_IA()
    {
        Ports.Should().Contain([
            typeof(IAiUsagePolicy), typeof(IAiEngineFactory), typeof(IAiEngineHealth), typeof(INamedEntityDetection),
            typeof(IAccountingAssistant), typeof(IAiAuditLog), typeof(IAiSettingsStore)]);

        ServiceCollection services = new();
        services.AddApplication();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        foreach (Type port in Ports)
        {
            object? implementation = scope.ServiceProvider.GetService(port);
            implementation.Should().NotBeNull(because: $"{port.Name} doit avoir une implémentation « None » enregistrée par AddApplication");
            implementation!.GetType().Assembly.Should().BeSameAs(Assemblies.Application, because: $"l'implémentation par défaut de {port.Name} appartient à l'application, pas à la couche IA");
        }
    }

    [Fact]
    public async Task Les_implementations_par_defaut_coupent_l_IA()
    {
        ServiceCollection services = new();
        services.AddApplication();
        using ServiceProvider provider = services.BuildServiceProvider();

        (await provider.GetRequiredService<IAiUsagePolicy>().GetSettingsAsync(new OrganisationId(Guid.NewGuid()), CancellationToken.None)).Enabled.Should().BeFalse();
        Enum.GetValues<AiEngineKind>().Select(provider.GetRequiredService<IAiEngineFactory>().Resolve).Should().AllSatisfy(e => e.Should().BeNull());
        provider.GetRequiredService<IAiEngineHealth>().IsResponding(AiEngineKind.ClaudeEu).Should().BeTrue("aucun moteur n'est surveillé");
        provider.GetRequiredService<INamedEntityDetection>().IsAvailable.Should().BeFalse();
        provider.GetRequiredService<IAiSettingsStore>().IsAvailable.Should().BeFalse();
    }
}
