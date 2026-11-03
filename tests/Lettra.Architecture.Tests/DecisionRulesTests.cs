using System.Reflection;
using System.Runtime.CompilerServices;
using Lettra.Application.Abstractions.Decisions;
using Lettra.Application.Abstractions.Messaging;
using NetArchTest.Rules;

namespace Lettra.Architecture.Tests;

/// <summary>
/// « Décisions à prendre » : les règles lisent et proposent, sans médiateur ni IA ; une action acceptée ne se fait qu'avec un
/// clic. Seules des actions réversibles, sur une liste fermée, pourront un jour se faire sans clic (niveau « faire et
/// prévenir », non réalisé) : la liste est vide, et une action qui se déclare réversible doit y être ajoutée en revue.
/// </summary>
public sealed class DecisionRulesTests
{
    private static readonly Type[] AllowedReversibleActions = [];

    private static IEnumerable<Type> Implementations<T>() =>
        Assemblies.All.SelectMany(a => a.GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false } && typeof(T).IsAssignableFrom(t));

    [Fact]
    public void Les_regles_de_decision_lisent_sans_mediateur_ni_IA()
    {
        Type[] rules = [.. Implementations<IDecisionRule>()];
        rules.Select(t => t.Name).Should().Contain(["ReminderDecisionRule", "FiscalDeadlineDecisionRule", "ThresholdDecisionRule", "IbanChangeDecisionRule"]);

        foreach (Assembly assembly in rules.Select(t => t.Assembly).Distinct())
        {
            Types.InAssembly(assembly)
                .That().ImplementInterface(typeof(IDecisionRule))
                .ShouldNot().HaveDependencyOnAny(typeof(ICommandDispatcher).FullName!, typeof(IQueryDispatcher).FullName!, "Lettra.Ai")
                .GetResult().FailingTypeNames.Should().BeNullOrEmpty(because: "une règle lit ses stores et propose : elle ne décide ni n'écrit rien");
        }
    }

    [Fact]
    public void Aucune_action_de_decision_ne_se_dit_reversible_hors_de_la_liste_fermee()
    {
        Type[] actions = [.. Implementations<IDecisionActionHandler>()];
        actions.Select(t => t.Name).Should().Contain("SendReminderDecisionAction");

        // Reversible est une constante de la classe : lue sans construire l'action (ses dépendances ne servent pas ici).
        Type[] reversible = [.. actions.Where(t => ((IDecisionActionHandler)RuntimeHelpers.GetUninitializedObject(t)).Reversible)];

        reversible.Should().BeSubsetOf(AllowedReversibleActions, "une action qui se fait sans clic doit se défaire sans conséquence hors de Lettra");
    }
}
