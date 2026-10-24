using NetArchTest.Rules;

namespace Lettra.Architecture.Tests;

/// <summary>
/// Les écrans et services de l'interface déclarent ce dont ils ont besoin (injection, ou <c>Optional&lt;T&gt;</c> quand
/// l'hôte peut ne pas le fournir) : aucun ne reçoit le conteneur pour y chercher ses dépendances. Seuls l'enregistrement
/// des services, <c>Optional&lt;T&gt;</c> lui-même et la langue posée au démarrage de l'hôte le voient.
/// </summary>
public sealed class UiDependencyRulesTests
{
    private static readonly string[] Allowed =
    [
        "Lettra.UI.Shared.DependencyInjection",
        "Lettra.UI.Shared.Drafts.OfflineDraftsRegistration",
        "Lettra.UI.Shared.Preferences.ScreenLanguage",
        "Lettra.UI.Shared.Services.Optional`1",
        "Lettra.UI.Shared.Services.OptionalRegistration",
    ];

    [Fact]
    public void L_interface_ne_cherche_pas_ses_dependances_dans_le_conteneur()
    {
        TestResult result = Types.InAssembly(Assemblies.UiShared)
            .ShouldNot().HaveDependencyOn("System.IServiceProvider")
            .GetResult();

        IEnumerable<string> offenders = (result.FailingTypeNames ?? []).Where(name => !Allowed.Contains(OuterType(name), StringComparer.Ordinal));
        offenders.Should().BeEmpty("un écran ou un service déclare ses dépendances au lieu de les chercher dans le conteneur");
    }

    [Fact]
    public void Chaque_exception_existe_encore()
    {
        IEnumerable<string> types = Assemblies.UiShared.GetTypes().Select(t => t.FullName ?? t.Name);

        types.Should().Contain(Allowed);
    }

    /// <summary>Les fermetures d'une méthode (<c>DependencyInjection+&lt;&gt;c</c>) comptent pour leur type.</summary>
    private static string OuterType(string name) => name.Split('+', '/')[0];
}
