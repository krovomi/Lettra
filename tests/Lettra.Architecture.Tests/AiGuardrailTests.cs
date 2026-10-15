using System.Reflection;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Billing;
using NetArchTest.Rules;

namespace Lettra.Architecture.Tests;

/// <summary>
/// L'IA ne valide ni n'émet jamais (ADR-0006, annexe B §4.2) : parmi toutes les commandes de l'application, la couche IA
/// n'a le droit d'envoyer que la création d'un devis brouillon. Une commande ajoutée plus tard est interdite par défaut.
/// </summary>
public sealed class AiGuardrailTests
{
    private static readonly Type[] AllowedCommands = [typeof(CreateQuoteCommand)];

    [Fact]
    public void La_couche_IA_n_envoie_aucune_commande_hors_du_brouillon_de_devis()
    {
        string[] forbidden = [.. Assemblies.Application.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>)))
            .Except(AllowedCommands)
            .Select(t => t.FullName!)];

        forbidden.Should().Contain([
            typeof(ValidateInvoiceCommand).FullName!, typeof(IssueInvoiceCommand).FullName!, typeof(ValidateQuoteCommand).FullName!,
            typeof(ApproveCreditNoteCommand).FullName!, typeof(DeleteQuoteCommand).FullName!]);

        foreach (Assembly assembly in new[] { Assemblies.AiSkills, Assemblies.AiPrivacy, Assemblies.AiTools, Assemblies.AiOrchestration, Assemblies.AiClaude, Assemblies.AiLocal })
        {
            Types.InAssembly(assembly)
                .ShouldNot().HaveDependencyOnAny(forbidden)
                .GetResult().FailingTypeNames.Should().BeNullOrEmpty(because: $"{assembly.GetName().Name} : l'IA propose, l'utilisateur valide et émet");
        }
    }
}
