using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Contracts.Assistant;

namespace Lettra.Ai.Orchestration;

/// <summary>
/// Registre des outils exposables au modèle. Refuse au démarrage tout outil dont le pouvoir n'est pas « lecture seule »
/// ou « crée un brouillon », ou dont le nom évoque une action réservée à un humain (ADR-0006, ADR-0012).
/// </summary>
internal sealed class ToolRegistry
{
    private static readonly string[] ForbiddenVerbs =
        ["valider", "emettre", "émettre", "envoyer", "supprimer", "effacer", "payer", "exporter", "signer", "transmettre", "cloturer", "clôturer"];

    private readonly Dictionary<string, IAiTool> _tools = new(StringComparer.Ordinal);

    public ToolRegistry(IEnumerable<IAiTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        foreach (IAiTool tool in tools)
        {
            if (tool.Risk is not (AssistantToolRisk.ReadOnly or AssistantToolRisk.CreatesDraft))
            {
                throw new InvalidOperationException($"Outil « {tool.Name} » refusé : pouvoir non autorisé.");
            }

            if (ForbiddenVerbs.Any(verb => tool.Name.Contains(verb, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Outil « {tool.Name} » refusé : son nom évoque une action réservée à un humain.");
            }

            if (!_tools.TryAdd(tool.Name, tool))
            {
                throw new InvalidOperationException($"Outil « {tool.Name} » déclaré deux fois.");
            }
        }
    }

    public IReadOnlyCollection<IAiTool> All => _tools.Values;

    /// <summary>Outils proposés pour cette demande : fonction activée, droits de l'utilisateur et réglages du cabinet.</summary>
    public IReadOnlyList<IAiTool> For(AssistantFeature feature, IReadOnlySet<Permission> permissions, AiSettings settings) =>
        [.. _tools.Values
            .Where(t => t.Features.Contains(feature))
            .Where(t => t.RequiredPermission is not { } required || permissions.Contains(required))
            .Where(t => t.IsEnabledBy(settings))
            .OrderBy(t => t.Name, StringComparer.Ordinal)]; // ordre stable : le préfixe du prompt reste en cache
}
