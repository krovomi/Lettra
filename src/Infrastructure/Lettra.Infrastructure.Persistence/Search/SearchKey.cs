using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Search;

/// <summary>
/// Colonne <c>SearchKey</c> : la forme de recherche (<see cref="SearchText"/>) d'une propriété texte, tenue
/// à jour à chaque écriture. Propriété fantôme : le domaine n'en sait rien.
/// </summary>
public static class SearchKey
{
    public const string PropertyName = "SearchKey";

    /// <summary>Une forme de recherche peut être plus longue que sa source (ligatures dépliées).</summary>
    public const int MaxLength = 400;

    internal const string SourceAnnotation = "Lettra:SearchKeySource";

    /// <summary>Déclare la colonne de recherche d'une entité, calculée depuis <paramref name="source"/>.</summary>
    public static EntityTypeBuilder<TEntity> HasSearchKey<TEntity>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, string>> source)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(source);
        string sourceName = ((MemberExpression)source.Body).Member.Name;
        builder.Property<string>(PropertyName).HasMaxLength(MaxLength).IsRequired().HasDefaultValue(string.Empty);
        builder.HasAnnotation(SourceAnnotation, sourceName);
        return builder;
    }

    /// <summary>Filtre « contient » sur la colonne de recherche ; le terme est normalisé de la même façon.</summary>
    public static IQueryable<TEntity> WhereSearchKeyContains<TEntity>(this IQueryable<TEntity> query, string? term)
        where TEntity : class
    {
        string normalized = SearchText.Normalize(term);
        return normalized.Length == 0
            ? query
            : query.Where(e => EF.Property<string>(e, PropertyName).Contains(normalized));
    }

    /// <summary>Calcule la colonne de recherche des entités ajoutées ou modifiées, juste avant l'écriture.</summary>
    internal static void Refresh(DbContext context)
    {
        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)
                || entry.Metadata.FindAnnotation(SourceAnnotation)?.Value is not string sourceName)
            {
                continue;
            }

            string key = SearchText.Normalize(entry.Property(sourceName).CurrentValue as string);
            key = key[..Math.Min(key.Length, MaxLength)];
            PropertyEntry target = entry.Property(PropertyName);
            if (!string.Equals(target.CurrentValue as string, key, StringComparison.Ordinal))
            {
                target.CurrentValue = key;
            }
        }
    }
}

/// <summary>Tient à jour les colonnes <c>SearchKey</c> à chaque <c>SaveChanges</c>.</summary>
internal sealed class SearchKeyInterceptor : SaveChangesInterceptor
{
    public static readonly SearchKeyInterceptor Instance = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is not null)
        {
            SearchKey.Refresh(eventData.Context);
        }

        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is not null)
        {
            SearchKey.Refresh(eventData.Context);
        }

        return ValueTask.FromResult(result);
    }
}
