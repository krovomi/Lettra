using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Catalog;

/// <summary>
/// Réglages IA d'un cabinet (ADR-0012, annexe F §10). Une ligne par cabinet qui a enregistré ses réglages ; sans ligne,
/// les réglages de la configuration du déploiement s'appliquent (IA désactivée par défaut). Aucune donnée de dossier ici,
/// seulement les identifiants des dossiers exclus.
/// </summary>
public sealed class CatalogAiSettings
{
    public Guid OrganisationId { get; set; }

    public bool Enabled { get; set; }

    /// <summary>Valeur de <c>AiEngineKind</c> (0 aucun, 1 Claude UE, 2 modèle local).</summary>
    public int Engine { get; set; }

    /// <summary>Fonctions activées (<c>AssistantFeature</c>), séparées par des virgules : « 3,6 ».</summary>
    public string Features { get; set; } = string.Empty;

    /// <summary>Dossiers exclus de l'assistant, identifiants séparés par des virgules.</summary>
    public string ExcludedDossiers { get; set; } = string.Empty;

    public bool OfficialWebLookup { get; set; }

    public int MaxToolRounds { get; set; }

    /// <summary>Incrémentée à chaque enregistrement : un administrateur qui enregistre sur une version périmée est refusé.</summary>
    public long Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Compte qui a fait la dernière modification (traçabilité de l'activation de l'IA).</summary>
    public Guid UpdatedBy { get; set; }

    internal static void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<CatalogAiSettings>(e =>
    {
        e.ToTable("AiSettings");
        e.HasKey(x => x.OrganisationId);
        e.Property(x => x.OrganisationId).ValueGeneratedNever();
        e.Property(x => x.Features).HasMaxLength(200);
        e.Property(x => x.Version).IsConcurrencyToken();
        e.HasOne<CatalogOrganisation>().WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Cascade);
    });
}
