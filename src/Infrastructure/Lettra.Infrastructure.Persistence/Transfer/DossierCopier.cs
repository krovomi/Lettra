using System.Linq.Expressions;
using System.Reflection;
using Lettra.Infrastructure.Persistence.Integrity;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Lettra.Infrastructure.Persistence.Transfer;

/// <summary>
/// Copie toutes les lignes d'un dossier d'une base à une autre, d'un provider à l'autre (PostgreSQL, SQL Server, SQLite),
/// en passant par le modèle EF : chaque agrégat est relu avec ses parties possédées puis ajouté tel quel, les champs
/// sensibles déchiffrés par la clé de départ et rechiffrés par celle d'arrivée, les clés de recherche recalculées.
/// <para>
/// Les tables partent dans l'ordre de leurs clés étrangères. Une ligne déjà posée dans la base d'arrivée à sa création
/// (séries de numérotation, tête de la chaîne) y est mise à jour. Une clé numérotée par la base (journal IA, files de
/// synchronisation) est renumérotée par la base d'arrivée, dans le même ordre. L'estampille et les clés de la chaîne ne
/// sont pas copiées : la première appartient à chaque base, les secondes changent d'enveloppe (<see cref="IntegrityKeyEnvelope"/>).
/// </para>
/// </summary>
internal static class DossierCopier
{
    /// <summary>Lignes par lecture et par enregistrement : les pièces justificatives sont en base, la mémoire reste bornée.</summary>
    internal const int BatchSize = 100;

    private static readonly HashSet<Type> NotCopied = [typeof(DossierStamp), typeof(StoredIntegrityKey)];

    private static readonly MethodInfo CopyTableMethod =
        typeof(DossierCopier).GetMethod(nameof(CopyTableAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Copie tout ; rend le nombre de lignes copiées par table (agrégats, sans leurs parties possédées).</summary>
    public static async Task<IReadOnlyDictionary<string, int>> CopyAsync(LettraDbContext source, LettraDbContext target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Dictionary<string, int> copied = new(StringComparer.Ordinal);
        foreach (IEntityType table in Tables(target.Model))
        {
            Task<int> copy = (Task<int>)CopyTableMethod.MakeGenericMethod(table.ClrType).Invoke(null, [source, target, table, cancellationToken])!;
            copied[table.GetTableName()!] = await copy.ConfigureAwait(false);
        }

        return copied;
    }

    /// <summary>Les agrégats copiés, chaque table après celles qu'elle référence (tri topologique, stable par nom).</summary>
    internal static IReadOnlyList<IEntityType> Tables(IModel model)
    {
        List<IEntityType> pending = [.. model.GetEntityTypes()
            .Where(t => !t.IsOwned() && t.BaseType is null && !NotCopied.Contains(t.ClrType))
            .OrderBy(t => t.GetTableName(), StringComparer.Ordinal)];
        List<IEntityType> ordered = [];
        while (pending.Count > 0)
        {
            IEntityType next = pending.FirstOrDefault(t => t.GetForeignKeys()
                    .Where(f => !f.IsOwnership && f.PrincipalEntityType != t)
                    .All(f => !pending.Contains(f.PrincipalEntityType.GetRootType())))
                ?? throw new InvalidOperationException("Clés étrangères circulaires entre les tables du dossier : ordre de copie introuvable.");
            ordered.Add(next);
            pending.Remove(next);
        }

        return ordered;
    }

    private static async Task<int> CopyTableAsync<TEntity>(LettraDbContext source, LettraDbContext target, IEntityType table, CancellationToken cancellationToken)
        where TEntity : class
    {
        IKey key = table.FindPrimaryKey() ?? throw new InvalidOperationException($"Table {table.GetTableName()} sans clé primaire.");
        IProperty? renumbered = key.Properties is [{ ValueGenerated: ValueGenerated.OnAdd } generated] ? generated : null;
        bool seeded = await target.Set<TEntity>().AnyAsync(cancellationToken).ConfigureAwait(false);
        int copied = 0;
        while (true)
        {
            List<TEntity> batch = await OrderedByKey(source.Set<TEntity>().AsNoTracking(), key)
                .Skip(copied).Take(BatchSize).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                return copied;
            }

            foreach (TEntity row in batch)
            {
                TEntity? existing = seeded
                    ? await target.Set<TEntity>().FindAsync([.. key.Properties.Select(p => p.GetGetter().GetClrValue(row))], cancellationToken).ConfigureAwait(false)
                    : null;
                if (existing is not null)
                {
                    target.Entry(existing).CurrentValues.SetValues(row);
                    continue;
                }

                if (renumbered is not null)
                {
                    // La base d'arrivée numérote : une valeur explicite y serait refusée (SQL Server) ou désaccorderait sa séquence (PostgreSQL).
                    target.Entry(row).Property(renumbered.Name).CurrentValue = Activator.CreateInstance(renumbered.ClrType);
                }

                target.Add(row);
            }

            await target.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            target.ChangeTracker.Clear();
            copied += batch.Count;
        }
    }

    /// <summary>Tri par clé primaire (toutes ses colonnes) : une pagination stable, et l'ordre d'origine des clés numérotées.</summary>
    private static IQueryable<TEntity> OrderedByKey<TEntity>(IQueryable<TEntity> query, IKey key)
        where TEntity : class
    {
        IOrderedQueryable<TEntity>? ordered = null;
        foreach (IProperty property in key.Properties)
        {
            ParameterExpression row = Expression.Parameter(typeof(TEntity), "row");
            MethodInfo efProperty = typeof(EF).GetMethod(nameof(EF.Property))!.MakeGenericMethod(property.ClrType);
            LambdaExpression selector = Expression.Lambda(
                Expression.Call(efProperty, row, Expression.Constant(property.Name)), row);
            string method = ordered is null ? nameof(Queryable.OrderBy) : nameof(Queryable.ThenBy);
            ordered = (IOrderedQueryable<TEntity>)typeof(Queryable).GetMethods()
                .Single(m => m.Name == method && m.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(TEntity), property.ClrType)
                .Invoke(null, [ordered ?? query, selector])!;
        }

        return ordered ?? query;
    }
}
