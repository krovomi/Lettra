using System.Text.Json;
using System.Text.Json.Serialization;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Preferences;
using Lettra.Domain.Common;
using Lettra.Domain.Preferences;
using Lettra.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Catalog;

/// <summary>
/// Modes 2, 3 et 5 : les préférences de chaque utilisateur dans la base Catalog, un document JSON par compte. Le
/// document est relu par le domaine (<see cref="UserPreferences.Restore"/>) : un champ disparu ou hors bornes est écarté.
/// </summary>
internal sealed class CatalogUserPreferencesStore(IDbContextFactory<CatalogDbContext> factory, TimeProvider clock) : IUserPreferencesStore
{
    public bool IsAvailable => true;

    public async ValueTask<UserPreferences?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            CatalogUserPreferences? row = await db.UserPreferences.AsNoTracking()
                .SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken).ConfigureAwait(false);
            return row is null ? null : ToDomain(row);
        }
    }

    public async ValueTask<Result<UserPreferences>> SaveAsync(UserPreferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        string document = JsonSerializer.Serialize(ToDocument(preferences), PreferencesJson.Default.PreferencesDocument);
        if (document.Length > CatalogUserPreferences.MaxDocumentLength)
        {
            return UserPreferencesErrors.TooMany(nameof(UserPreferences));
        }

        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            CatalogUserPreferences? row = await db.UserPreferences
                .SingleOrDefaultAsync(p => p.UserId == preferences.UserId, cancellationToken).ConfigureAwait(false);
            if ((row?.Version ?? 0) != preferences.Version)
            {
                return ConcurrencyErrors.VersionMismatch;
            }

            if (row is null)
            {
                row = new CatalogUserPreferences { UserId = preferences.UserId };
                db.UserPreferences.Add(row);
            }

            row.Document = document;
            row.Version = preferences.Version + 1;
            row.UpdatedAt = clock.GetUtcNow();
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                // Un autre appareil a écrit entre la lecture et l'écriture (version changée, ou première ligne simultanée).
                return ConcurrencyErrors.VersionMismatch;
            }

            return ToDomain(row);
        }
    }

    private static UserPreferences ToDomain(CatalogUserPreferences row)
    {
        PreferencesDocument? document = null;
        try
        {
            document = JsonSerializer.Deserialize(row.Document, PreferencesJson.Default.PreferencesDocument);
        }
        catch (JsonException)
        {
            // Document illisible (écrit par une version future incompatible) : on repart des réglages de départ.
        }

        return UserPreferences.Restore(
            row.UserId,
            document?.Workspace,
            document?.EntryDefaults,
            document?.Usage?.Select(u => new DossierUsage(u.DossierId, u.Entries ?? [])),
            row.Version,
            row.UpdatedAt);
    }

    private static PreferencesDocument ToDocument(UserPreferences preferences) => new(
        preferences.Workspace,
        preferences.EntryDefaults,
        [.. preferences.Usage.Select(u => new UsageDocument(u.DossierId, u.Entries))]);
}

/// <summary>Le document rangé en base. Les champs s'ajoutent sans migration ; un champ inconnu est ignoré à la lecture.</summary>
internal sealed record PreferencesDocument(
    WorkspacePreferences? Workspace,
    IReadOnlyList<EntryDefaults>? EntryDefaults,
    IReadOnlyList<UsageDocument>? Usage);

internal sealed record UsageDocument(Guid DossierId, IReadOnlyList<UsageEntry>? Entries);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PreferencesDocument))]
internal sealed partial class PreferencesJson : JsonSerializerContext;
