using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Catalog;

/// <summary>
/// Base « Catalog » du plan de contrôle (modes 2, 3 et 5) : cabinets, serveurs de bases et dossiers. Les droits
/// (rôles à portée) relèvent de l'identité (<c>IAuthorizationDirectory</c>).
/// Ses migrations vivent, comme celles des dossiers, dans les trois assemblies de providers.
/// </summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string SchemaName = "catalog";

    public DbSet<CatalogOrganisation> Organisations => Set<CatalogOrganisation>();

    public DbSet<CatalogCluster> Clusters => Set<CatalogCluster>();

    public DbSet<CatalogDossier> Dossiers => Set<CatalogDossier>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        // SQLite n'a pas de schémas : le catalogue y est un fichier à part.
        if (!Database.ProviderName!.EndsWith(".Sqlite", StringComparison.Ordinal))
        {
            modelBuilder.HasDefaultSchema(SchemaName);
        }

        modelBuilder.Entity<CatalogOrganisation>(e =>
        {
            e.ToTable("Organisations");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.CellId).HasMaxLength(64);
        });

        modelBuilder.Entity<CatalogCluster>(e =>
        {
            e.ToTable("Clusters");
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.CellId).HasMaxLength(64);
            e.Property(x => x.Provider).HasConversion<int>();
        });

        modelBuilder.Entity<CatalogDossier>(e =>
        {
            e.ToTable("Dossiers");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(CatalogDossier.MaxNameLength);
            e.Property(x => x.Provider).HasConversion<int>();
            e.Property(x => x.State).HasConversion<int>();
            e.Property(x => x.ConnectionSecretRef).HasMaxLength(256);
            e.Property(x => x.ClusterId).HasMaxLength(64);
            e.Property(x => x.DatabaseName).HasMaxLength(63);
            e.Property(x => x.Schema).HasMaxLength(63);
            e.Property(x => x.SchemaVersion).HasMaxLength(150);
            e.Property(x => x.WrappedDek).HasMaxLength(60);
            e.Property(x => x.LeaseOwner).HasMaxLength(128);
            e.Property(x => x.LastError).HasMaxLength(2000);
            e.HasIndex(x => x.OrganisationId);
            e.HasIndex(x => new { x.ClusterId, x.DatabaseName }).IsUnique();
            e.HasIndex(x => new { x.State, x.SchemaVersion });
            e.HasOne<CatalogOrganisation>().WithMany().HasForeignKey(x => x.OrganisationId);
            e.HasOne<CatalogCluster>().WithMany().HasForeignKey(x => x.ClusterId).IsRequired(false);
        });
    }
}
