using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Parties;
using Lettra.Infrastructure.Persistence.Concurrency;
using Lettra.Infrastructure.Persistence.Numbering;
using Lettra.Infrastructure.Persistence.Search;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Lettra.Infrastructure.Persistence;

/// <summary>
/// Le DbContext d'un dossier. Un seul modèle pour les trois providers ; seule la chaîne
/// de connexion (et l'assembly de migrations) change d'un dossier à l'autre.
/// </summary>
public sealed class LettraDbContext(DbContextOptions<LettraDbContext> options, TenantDescriptor tenant) : DbContext(options)
{
    public TenantDescriptor Tenant { get; } = tenant;

    public DbSet<Customer> Customers => Set<Customer>();

    internal DbSet<DossierStamp> DossierStamp => Set<DossierStamp>();

    internal DbSet<NumberSeries> NumberSeries => Set<NumberSeries>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        // Le schéma fait partie du modèle : sans cette clé de cache, EF réutiliserait le modèle
        // (donc le schéma) du premier dossier ouvert pour tous les autres (constat du POC base par dossier).
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, TenantSchemaModelCacheKeyFactory>();
        optionsBuilder.AddInterceptors(VersionInterceptor.Instance);
        optionsBuilder.AddInterceptors(SearchKeyInterceptor.Instance);
        if (Tenant.Schema is not null)
        {
            // Les migrations sont générées sans schéma ; le provider les applique dans le schéma du dossier
            // (search_path PostgreSQL). Seul le schéma par défaut diffère du modèle des migrations : l'écart
            // n'est pas une migration manquante (vérifiée, elle, par les dossiers sans schéma).
            optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        if (Tenant.Schema is not null)
        {
            modelBuilder.HasDefaultSchema(Tenant.Schema);
        }

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LettraDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        // Montants : decimal(18,4) partout, arrondi au centime par la règle unique de Lettra.Calculations.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }
}

/// <summary>Un modèle EF par (type de contexte, schéma) au lieu d'un seul par type.</summary>
internal sealed class TenantSchemaModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is LettraDbContext lettra
            ? (context.GetType(), lettra.Tenant.Schema, designTime)
            : (context.GetType(), designTime);
}
