using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Supervision;

/// <summary>
/// Base de supervision (annexe G §9) : les alertes, en métadonnées seulement (règle, gravité, portée en identifiants
/// techniques, chronologie). Aucune donnée de dossier. Par défaut dans la base Catalog, sous son propre schéma et son
/// propre historique de migrations ; un fichier à part sur le poste isolé.
/// </summary>
public sealed class SupervisionDbContext(DbContextOptions<SupervisionDbContext> options) : DbContext(options)
{
    public const string SchemaName = "supervision";
    public const string MigrationsHistoryTable = "__SupervisionMigrationsHistory";

    public DbSet<SupervisionAlertRow> Alerts => Set<SupervisionAlertRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        if (!Database.ProviderName!.EndsWith(".Sqlite", StringComparison.Ordinal))
        {
            modelBuilder.HasDefaultSchema(SchemaName);
        }

        modelBuilder.Entity<SupervisionAlertRow>(e =>
        {
            e.ToTable("Alerts");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Fingerprint).HasMaxLength(SupervisionAlertRow.MaxFingerprintLength);
            e.Property(x => x.RuleId).HasMaxLength(SupervisionAlertRow.MaxRuleIdLength);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Fingerprint).IsUnique();
            e.HasIndex(x => new { x.CabinetId, x.State });
            e.HasIndex(x => new { x.DossierId, x.State });
            e.HasIndex(x => new { x.GroupId, x.State });
            e.HasIndex(x => x.State);
        });
    }
}

/// <summary>
/// Une alerte : les colonnes servent au tri et au filtrage par portée, l'état complet est dans <see cref="Snapshot"/>
/// (JSON écrit par le module Supervision).
/// </summary>
public sealed class SupervisionAlertRow
{
    public const int MaxFingerprintLength = 300;
    public const int MaxRuleIdLength = 100;

    public Guid Id { get; set; }

    public string Fingerprint { get; set; } = string.Empty;

    public string RuleId { get; set; } = string.Empty;

    public int Severity { get; set; }

    public int State { get; set; }

    public int ScopeLevel { get; set; }

    public Guid? CabinetId { get; set; }

    public Guid? GroupId { get; set; }

    public Guid? DossierId { get; set; }

    /// <summary>Dernière occurrence, en millisecondes Unix (tri identique sur les trois providers).</summary>
    public long LastSeen { get; set; }

    public string Snapshot { get; set; } = string.Empty;

    /// <summary>Jeton de concurrence : deux instances ne réécrivent pas la même alerte sans le voir.</summary>
    public int Version { get; set; }
}
