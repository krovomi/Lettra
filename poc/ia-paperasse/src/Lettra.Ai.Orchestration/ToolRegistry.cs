using Lettra.Ai.Abstractions;

namespace Lettra.Ai.Orchestration;

/// <summary>
/// Registre des outils exposables au modèle. Refuse au démarrage tout outil dont le pouvoir n'est pas
/// « lecture seule » ou « crée un brouillon », ou dont le nom évoque une action réservée à un humain.
/// </summary>
public sealed class ToolRegistry
{
    private static readonly string[] ForbiddenVerbs =
        ["valider", "emettre", "émettre", "envoyer", "supprimer", "effacer", "payer", "exporter", "signer", "transmettre", "cloturer", "clôturer"];

    private readonly Dictionary<string, IAiTool> _tools;

    public ToolRegistry(IEnumerable<IAiTool> tools)
    {
        _tools = new Dictionary<string, IAiTool>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            if (!Enum.IsDefined(tool.Risk) || tool.Risk is not (AiToolRisk.ReadOnly or AiToolRisk.CreatesDraft))
                throw new InvalidOperationException($"Outil « {tool.Name} » refusé : pouvoir non autorisé.");
            // « valider_facture » vérifie sans rien valider : c'est le seul nom toléré, et il est en lecture seule.
            var lower = tool.Name.ToLowerInvariant();
            if (lower != "valider_facture" && ForbiddenVerbs.Any(v => lower.Contains(v)))
                throw new InvalidOperationException($"Outil « {tool.Name} » refusé : son nom évoque une action réservée à un humain.");
            if (lower == "valider_facture" && tool.Risk != AiToolRisk.ReadOnly)
                throw new InvalidOperationException("« valider_facture » doit rester en lecture seule.");
            if (!_tools.TryAdd(tool.Name, tool))
                throw new InvalidOperationException($"Outil « {tool.Name} » déclaré deux fois.");
        }
    }

    public IReadOnlyCollection<IAiTool> All => _tools.Values;

    /// <summary>Outils proposés pour cette demande : fonction activée et droits de l'utilisateur.</summary>
    public IReadOnlyList<IAiTool> For(AiFeature feature, IReadOnlySet<string> permissions, AiSettings settings) =>
        _tools.Values
            .Where(t => t.Features.Contains(feature))
            .Where(t => permissions.Contains(t.RequiredPermission))
            .Where(t => t.Name != "consulter_site_officiel" || settings.OfficialWebLookup)
            .OrderBy(t => t.Name, StringComparer.Ordinal) // ordre stable : le préfixe reste en cache
            .ToList();
}
