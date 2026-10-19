using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Catalog;

/// <summary>
/// Un indicateur de la vue cabinet : un compteur par (dossier, clé), recalculé la nuit. Seulement des agrégats : aucun
/// montant, aucun nom de client ne quitte la base du dossier (ADR-0003, annexe F). Une nouvelle clé n'exige pas de migration.
/// </summary>
public sealed class CatalogDossierIndicator
{
    public const int MaxKeyLength = 64;

    public Guid DossierId { get; set; }

    public string Key { get; set; } = string.Empty;

    public int Count { get; set; }

    /// <summary>Valeur de <c>IndicatorLevel</c> (0 rien à faire … 3 critique).</summary>
    public int Level { get; set; }

    public DateOnly? DueOn { get; set; }

    public DateTimeOffset ComputedAt { get; set; }

    internal static void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<CatalogDossierIndicator>(e =>
    {
        e.ToTable("DossierIndicators");
        e.HasKey(x => new { x.DossierId, x.Key });
        e.Property(x => x.Key).HasMaxLength(MaxKeyLength);
        e.HasOne<CatalogDossier>().WithMany().HasForeignKey(x => x.DossierId).OnDelete(DeleteBehavior.Cascade);
    });
}
