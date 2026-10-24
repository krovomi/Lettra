using Lettra.Contracts.Assistant;

namespace Lettra.Application.Abstractions.Ai;

/// <summary>
/// Repérage des noms propres dans un texte libre, avant tout envoi au moteur (annexe I §7, annexe J §5.2, annexe F §10).
/// Le masquage actuel ne remplace que les noms connus du dossier : un nom tapé librement passerait en clair. Tant que ce
/// repérage n'existe pas, les points d'IA qui reçoivent un texte libre restent fermés, quel que soit le réglage du cabinet
/// (décision du DPO du 23/10/2026).
/// </summary>
public interface INamedEntityDetection
{
    /// <summary>Aucun repérage sur ce déploiement : la valeur par défaut, et la seule qui existe aujourd'hui.</summary>
    public static INamedEntityDetection None { get; } = new NoNamedEntityDetection();

    bool IsAvailable { get; }
}

/// <summary>Points d'IA fermés tant qu'un garde-fou qu'ils exigent manque sur ce déploiement.</summary>
public static class AiFeatureSafeguards
{
    /// <summary>Le point 1 envoie au moteur le texte libre de l'utilisateur, avec les noms qu'il contient.</summary>
    public static bool NeedsNamedEntityDetection(AssistantFeature feature) => feature == AssistantFeature.DraftFromText;

    public static bool IsBlocked(AssistantFeature feature, INamedEntityDetection detection)
    {
        ArgumentNullException.ThrowIfNull(detection);
        return NeedsNamedEntityDetection(feature) && !detection.IsAvailable;
    }
}

internal sealed class NoNamedEntityDetection : INamedEntityDetection
{
    public bool IsAvailable => false;
}
