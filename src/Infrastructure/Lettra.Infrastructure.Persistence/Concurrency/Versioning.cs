using Lettra.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Concurrency;

/// <summary>
/// Concurrence optimiste portable (ADR-0013) : une colonne <c>Version</c> entière, jeton de concurrence, au lieu
/// de <c>xmin</c> (PostgreSQL) ou <c>rowversion</c> (SQL Server) que SQLite n'a pas. Même valeur sur les trois
/// providers, donc même <c>ETag</c> en ligne et dans les intentions rejouées par la synchronisation.
/// </summary>
public static class VersioningConfiguration
{
    public const string ColumnName = nameof(IVersioned.Version);

    /// <summary>À appeler dans la configuration EF de chaque agrégat <see cref="IVersioned"/>.</summary>
    public static EntityTypeBuilder<T> IsVersioned<T>(this EntityTypeBuilder<T> builder)
        where T : class, IVersioned
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Property(e => e.Version).IsConcurrencyToken().ValueGeneratedNever();
        return builder;
    }
}

/// <summary>
/// Avance la version des agrégats écrits : 1 à l'insertion, +1 à chaque modification. EF ajoute alors
/// <c>WHERE Version = @lue</c> à l'UPDATE : une ligne modifiée entre-temps n'est pas écrasée.
/// </summary>
internal sealed class VersionInterceptor : SaveChangesInterceptor
{
    public static VersionInterceptor Instance { get; } = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Advance(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Advance(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Advance(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (EntityEntry<IVersioned> entry in context.ChangeTracker.Entries<IVersioned>())
        {
            PropertyEntry<IVersioned, long> version = entry.Property(e => e.Version);
            switch (entry.State)
            {
                case EntityState.Added:
                    version.CurrentValue = 1;
                    break;
                case EntityState.Modified:
                    version.CurrentValue = version.OriginalValue + 1;
                    break;
            }
        }
    }
}
