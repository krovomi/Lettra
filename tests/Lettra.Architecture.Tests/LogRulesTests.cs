using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Lettra.Architecture.Tests;

/// <summary>
/// Les journaux de toutes les assemblies, hôtes compris (Worker, API), ne portent aucune donnée de dossier : un message
/// journalisé ne reçoit que des identifiants, des nombres, des dates, des énumérations, et des textes techniques dont le
/// nom est dans la liste ci-dessous. Un nom de client, un libellé ou un montant saisi n'a aucun paramètre où passer.
/// L'analyseur CA1848 (en erreur hors tests) oblige tout journal à passer par un tel message déclaré ; le parcours réel
/// de l'API est vérifié par <c>NoDossierDataInLogsTests</c>, et les traces du Worker passent par les mêmes processeurs
/// d'expurgation que celles de l'API (<c>AddLettraObservability</c>, appelé par l'amorçage commun).
/// </summary>
public sealed class LogRulesTests
{
    /// <summary>Les paramètres texte admis : des codes, des noms techniques, jamais une valeur saisie. Tout ajout est revu.</summary>
    private static readonly HashSet<string> TechnicalTexts =
    [
        "code", "codes", "errorCodes", "exceptionType", "reason", "useCase", "source", "kind", "kinds", "severity", "ruleId",
        "model", "cellId", "what", "wave", "tool", "title", "scopeLevel", "reference", "host", "engine",
    ];

    [Fact]
    public void Les_messages_journalises_ne_recoivent_aucune_donnee_de_dossier()
    {
        string[] offending =
        [
            .. Assemblies.All.Concat(Assemblies.Hosts)
                .Distinct()
                .SelectMany(a => a.GetTypes())
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(m => m.GetCustomAttribute<LoggerMessageAttribute>() is not null)
                .SelectMany(m => m.GetParameters().Where(p => !IsAllowed(p)).Select(p => $"{m.DeclaringType!.FullName}.{m.Name}({p.ParameterType.Name} {p.Name})")),
        ];

        offending.Should().BeEmpty("un journal ne reçoit que des identifiants, des nombres, des énumérations et des textes techniques nommés");
    }

    [Fact]
    public void La_regle_voit_les_messages_journalises_du_worker() =>
        Assemblies.Worker.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Should().Contain(m => m.GetCustomAttribute<LoggerMessageAttribute>() != null, "le Worker journalise par des messages déclarés");

    private static bool IsAllowed(ParameterInfo parameter)
    {
        Type type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
        return typeof(ILogger).IsAssignableFrom(type)
            || typeof(Exception).IsAssignableFrom(type)
            || type == typeof(LogLevel)
            || type.IsPrimitive
            || type.IsEnum
            || type == typeof(Guid)
            || type == typeof(DateTimeOffset)
            || type == typeof(DateTime)
            || type == typeof(DateOnly)
            || type == typeof(TimeSpan)
            || (type == typeof(string) && TechnicalTexts.Contains(parameter.Name!));
    }
}
