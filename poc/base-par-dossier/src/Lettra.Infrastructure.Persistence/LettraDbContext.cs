using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Lettra.Infrastructure.Persistence;

/// Un seul modèle pour les trois fournisseurs ; seules la chaîne de connexion et l'assembly de migrations changent.
public sealed class LettraDbContext(DbContextOptions<LettraDbContext> options, TenantDescriptor tenant)
    : DbContext(options)
{
    public TenantDescriptor Tenant { get; } = tenant;

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<NumberSeries> NumberSeries => Set<NumberSeries>();
    public DbSet<DossierIdentity> DossierIdentity => Set<DossierIdentity>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        // Le schéma fait partie du modèle : sans cette clé de cache, EF réutiliserait
        // le modèle du premier dossier (et donc son schéma) pour tous les autres.
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, TenantSchemaModelCacheKeyFactory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (Tenant.Schema is not null)
            modelBuilder.HasDefaultSchema(Tenant.Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LettraDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
        configurationBuilder.Properties<string>().HaveMaxLength(200);
    }
}

internal sealed class TenantSchemaModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is LettraDbContext l
            ? (context.GetType(), l.Tenant.Schema, designTime)
            : (object)(context.GetType(), designTime);
}
