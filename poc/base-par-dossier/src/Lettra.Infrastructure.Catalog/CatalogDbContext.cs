using Lettra.Application.Abstractions.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lettra.Infrastructure.Catalog;

public sealed class Organisation
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    /// Cellule qui héberge le cabinet (ADR-0009). La passerelle route le cabinet vers elle.
    public required string CellId { get; set; }
}

/// Un cluster de bases dans une cellule (PostgreSQL derrière PgBouncer, ou SQL Server en mode 2).
public sealed class DbCluster
{
    public required string Id { get; set; }
    public required string CellId { get; set; }
    public DatabaseProvider Provider { get; set; }
    public int MaxDatabases { get; set; }
    public bool AcceptsNewDossiers { get; set; } = true;
}

public enum DossierState { Provisioning = 0, Active = 1, Hibernated = 2, Failed = 3, Deleted = 4 }

public sealed class Dossier
{
    public Guid Id { get; set; }
    public Guid OrganisationId { get; set; }
    public required string Name { get; set; }
    public required string ClusterId { get; set; }
    public required string DatabaseName { get; set; }
    public DatabaseProvider Provider { get; set; }
    public DossierState State { get; set; }
    /// Dernière migration appliquée (identifiant commun aux trois fournisseurs).
    public string? SchemaVersion { get; set; }
    /// DEK du dossier, chiffrée par la KEK du cabinet. La vider = effacement par destruction de clé.
    public byte[] WrappedDek { get; set; } = [];
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class DossierAccess
{
    public required string UserId { get; set; }
    public Guid DossierId { get; set; }
}

/// Base « Catalog » du plan de contrôle : cabinets, carte dossier → cellule → cluster, droits d'accès.
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Organisation> Organisations => Set<Organisation>();
    public DbSet<DbCluster> Clusters => Set<DbCluster>();
    public DbSet<Dossier> Dossiers => Set<Dossier>();
    public DbSet<DossierAccess> Access => Set<DossierAccess>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder;
        b.HasDefaultSchema("catalog");
        b.Entity<Organisation>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.CellId).HasMaxLength(64);
        });
        b.Entity<DbCluster>(e =>
        {
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.CellId).HasMaxLength(64);
        });
        b.Entity<Dossier>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.ClusterId).HasMaxLength(64);
            e.Property(x => x.DatabaseName).HasMaxLength(63);
            e.Property(x => x.SchemaVersion).HasMaxLength(150);
            e.Property(x => x.LeaseOwner).HasMaxLength(100);
            e.Property(x => x.LastError).HasMaxLength(2000);
            e.HasIndex(x => new { x.OrganisationId, x.Id });
            e.HasIndex(x => new { x.ClusterId, x.DatabaseName }).IsUnique();
            e.HasIndex(x => new { x.State, x.SchemaVersion });
            e.HasOne<Organisation>().WithMany().HasForeignKey(x => x.OrganisationId);
            e.HasOne<DbCluster>().WithMany().HasForeignKey(x => x.ClusterId);
        });
        b.Entity<DossierAccess>(e =>
        {
            e.HasKey(x => new { x.UserId, x.DossierId });
            e.Property(x => x.UserId).HasMaxLength(100);
            e.HasOne<Dossier>().WithMany().HasForeignKey(x => x.DossierId);
        });
    }
}

public sealed class CatalogDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql("Host=localhost;Database=catalog").Options);
}
