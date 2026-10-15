using Lettra.Application.Abstractions.Ai;
using Lettra.Contracts.Assistant;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lettra.Ai.Orchestration;

/// <summary>
/// Réglages IA lus dans la configuration (<c>Lettra:Ai</c>). Valeur par défaut : désactivé. Les réglages propres à chaque
/// cabinet sont enregistrés dans la base Catalog (modes 2, 3 et 5) et l'emportent ; ce fichier sert le poste isolé (mode 1),
/// les réglages par défaut d'un déploiement et le développement.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Lettra:Ai";

    public bool Enabled { get; set; }

    public AiEngineKind Engine { get; set; } = AiEngineKind.None;

    public IList<AssistantFeature> Features { get; } = [];

    public IList<Guid> ExcludedDossiers { get; } = [];

    public bool OfficialWebLookup { get; set; }

    public int MaxToolRounds { get; set; } = 8;

    /// <summary>
    /// Réglages propres à un cabinet (clé : identifiant du cabinet), qui remplacent les réglages ci-dessus pour ses dossiers
    /// tant que le cabinet n'a rien enregistré dans la base Catalog. Conservé pour les déploiements existants.
    /// Clé texte : le binder de configuration ne sait pas lire une clé <see cref="Guid"/>.
    /// </summary>
    public IDictionary<string, AiOptions> Organisations { get; } = new Dictionary<string, AiOptions>(StringComparer.OrdinalIgnoreCase);

    internal AiSettings ToSettings() => new()
    {
        Enabled = Enabled,
        Engine = Engine,
        Features = Features.ToHashSet(),
        ExcludedDossiers = ExcludedDossiers.Select(DossierId.From).ToHashSet(),
        OfficialWebLookup = OfficialWebLookup,
        MaxToolRounds = Math.Clamp(MaxToolRounds, 1, 20),
    };
}

/// <summary>
/// Réglages du cabinet : ceux enregistrés dans la base Catalog (modes 2, 3 et 5) l'emportent ; à défaut, l'entrée du cabinet
/// dans la configuration, puis les réglages du déploiement. Sans rien de tout cela, l'IA reste désactivée.
/// </summary>
internal sealed class CabinetAiUsagePolicy(IOptionsMonitor<AiOptions> options, IAiSettingsStore store) : IAiUsagePolicy
{
    public async ValueTask<AiSettings> GetSettingsAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        StoredAiSettings? stored = await store.FindAsync(organisation, cancellationToken).ConfigureAwait(false);
        if (stored is not null)
        {
            return stored.Settings;
        }

        AiOptions current = options.CurrentValue;
        AiOptions effective = current.Organisations.TryGetValue(organisation.Value.ToString("D"), out AiOptions? specific) ? specific : current;
        return effective.ToSettings();
    }
}

/// <summary>Moteurs disponibles sur ce déploiement : chaque adapter s'enregistre sous la clé de son <see cref="AiEngineKind"/>.</summary>
internal sealed class KeyedAiEngineFactory(IServiceProvider services) : IAiEngineFactory
{
    public IAiEngine? Resolve(AiEngineKind kind) =>
        kind == AiEngineKind.None ? null : services.GetKeyedService<IAiEngine>(kind);
}
