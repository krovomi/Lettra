using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Catalog;

/// <summary>
/// Base « Catalog » du plan de contrôle (modes 2, 3 et 5) : cabinets, serveurs de bases, dossiers, et le registre
/// des droits (comptes, groupes, rôles à portée) lu par <c>IAuthorizationDirectory</c>, et les réglages IA des cabinets.
/// Ses migrations vivent, comme celles des dossiers, dans les trois assemblies de providers.
/// </summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string SchemaName = "catalog";

    public DbSet<CatalogOrganisation> Organisations => Set<CatalogOrganisation>();

    public DbSet<CatalogCluster> Clusters => Set<CatalogCluster>();

    public DbSet<CatalogDossier> Dossiers => Set<CatalogDossier>();

    public DbSet<CatalogUserAccount> UserAccounts => Set<CatalogUserAccount>();

    public DbSet<CatalogGroup> Groups => Set<CatalogGroup>();

    public DbSet<CatalogGroupMember> GroupMembers => Set<CatalogGroupMember>();

    public DbSet<CatalogGroupDossier> GroupDossiers => Set<CatalogGroupDossier>();

    public DbSet<CatalogRoleAssignment> RoleAssignments => Set<CatalogRoleAssignment>();

    public DbSet<CatalogCustomRole> CustomRoles => Set<CatalogCustomRole>();

    public DbSet<CatalogCustomRolePermission> CustomRolePermissions => Set<CatalogCustomRolePermission>();

    public DbSet<CatalogAiSettings> AiSettings => Set<CatalogAiSettings>();
    public DbSet<CatalogOrganisationPolicy> OrganisationPolicies => Set<CatalogOrganisationPolicy>();

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

        // Registre des droits : pas de clé étrangère vers les dossiers ou les cabinets, le registre peut citer une
        // portée avant qu'elle n'existe ; la cohérence de cabinet est vérifiée à la lecture.
        modelBuilder.Entity<CatalogUserAccount>(e =>
        {
            e.ToTable("UserAccounts");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.State).HasConversion<int>();
            e.Property(x => x.DisplayName).HasMaxLength(CatalogUserAccount.MaxDisplayNameLength);
            e.Property(x => x.Email).HasMaxLength(CatalogUserAccount.MaxEmailLength);
            e.HasIndex(x => x.OrganisationId);
        });

        modelBuilder.Entity<CatalogGroup>(e =>
        {
            e.ToTable("Groups");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(CatalogGroup.MaxNameLength);
            e.HasIndex(x => x.OrganisationId);
        });

        modelBuilder.Entity<CatalogGroupMember>(e =>
        {
            e.ToTable("GroupMembers");
            e.HasKey(x => new { x.GroupId, x.UserId });
            e.HasIndex(x => x.UserId);
            e.HasOne<CatalogGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CatalogGroupDossier>(e =>
        {
            e.ToTable("GroupDossiers");
            e.HasKey(x => new { x.GroupId, x.DossierId });
            e.HasIndex(x => x.DossierId);
            e.HasOne<CatalogGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CatalogRoleAssignment>(e =>
        {
            e.ToTable("RoleAssignments");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.SubjectKind).HasConversion<int>();
            e.Property(x => x.ScopeKind).HasConversion<int>();
            e.Property(x => x.RoleKey).HasMaxLength(CatalogRoleAssignment.MaxRoleKeyLength);
            e.HasIndex(x => new { x.SubjectKind, x.SubjectId });
        });

        modelBuilder.Entity<CatalogCustomRole>(e =>
        {
            e.ToTable("CustomRoles");
            e.HasKey(x => new { x.OrganisationId, x.Key });
            e.Property(x => x.Key).HasMaxLength(CatalogRoleAssignment.MaxRoleKeyLength);
            e.Property(x => x.WidestScope).HasConversion<int>();
            e.Property(x => x.Name).HasMaxLength(CatalogCustomRole.MaxNameLength);
            e.Property(x => x.Description).HasMaxLength(CatalogCustomRole.MaxDescriptionLength);
        });

        modelBuilder.Entity<CatalogCustomRolePermission>(e =>
        {
            e.ToTable("CustomRolePermissions");
            e.HasKey(x => new { x.OrganisationId, x.RoleKey, x.Permission });
            e.Property(x => x.RoleKey).HasMaxLength(CatalogRoleAssignment.MaxRoleKeyLength);
            e.Property(x => x.Permission).HasConversion<int>();
            e.HasOne<CatalogCustomRole>().WithMany().HasForeignKey(x => new { x.OrganisationId, x.RoleKey }).OnDelete(DeleteBehavior.Cascade);
        });

        CatalogAiSettings.Configure(modelBuilder);

        modelBuilder.Entity<CatalogOrganisationPolicy>(e =>
        {
            e.ToTable("OrganisationPolicies");
            e.HasKey(x => x.OrganisationId);
            e.Property(x => x.CreditNoteApprovalThresholdInclTax).HasPrecision(18, 2);
            e.HasOne<CatalogOrganisation>().WithOne().HasForeignKey<CatalogOrganisationPolicy>(x => x.OrganisationId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
