using System.Globalization;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Features.Assistant;
using Lettra.Contracts.Assistant;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Catalog;

/// <summary>
/// Modes 2, 3 et 5 : réglages IA de chaque cabinet dans la base Catalog. Lus à chaque question posée à l'assistant
/// (une lecture par clé primaire) : une désactivation par l'administrateur s'applique tout de suite, sans cache à purger.
/// </summary>
internal sealed class CatalogAiSettingsStore(IDbContextFactory<CatalogDbContext> factory, TimeProvider clock) : IAiSettingsStore
{
    public bool IsAvailable => true;

    public async ValueTask<StoredAiSettings?> FindAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            CatalogAiSettings? row = await db.AiSettings.AsNoTracking()
                .SingleOrDefaultAsync(s => s.OrganisationId == organisation.Value, cancellationToken).ConfigureAwait(false);
            return row is null ? null : ToStored(row);
        }
    }

    public async ValueTask<Result<StoredAiSettings>> SaveAsync(
        OrganisationId organisation,
        AiSettings settings,
        long? expectedVersion,
        Guid updatedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            CatalogAiSettings? row = await db.AiSettings
                .SingleOrDefaultAsync(s => s.OrganisationId == organisation.Value, cancellationToken).ConfigureAwait(false);
            if (row?.Version != expectedVersion)
            {
                return AiSettingsErrors.SettingsConflict;
            }

            if (row is null)
            {
                row = new CatalogAiSettings { OrganisationId = organisation.Value };
                db.AiSettings.Add(row);
            }

            row.Enabled = settings.Enabled;
            row.Engine = (int)settings.Engine;
            row.Features = string.Join(',', settings.Features.Select(f => ((int)f).ToString(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal));
            row.ExcludedDossiers = string.Join(',', settings.ExcludedDossiers.Select(d => d.Value.ToString("D")).Order(StringComparer.Ordinal));
            row.OfficialWebLookup = settings.OfficialWebLookup;
            row.MaxToolRounds = settings.MaxToolRounds;
            row.Version = (expectedVersion ?? 0) + 1;
            row.UpdatedAt = clock.GetUtcNow();
            row.UpdatedBy = updatedBy;

            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                // Concurrence : un autre administrateur a enregistré entre la lecture et l'écriture (version modifiée,
                // ou premier enregistrement simultané qui heurte la clé primaire). Organisation inconnue : clé étrangère.
                return AiSettingsErrors.SettingsConflict;
            }

            return ToStored(row);
        }
    }

    private static StoredAiSettings ToStored(CatalogAiSettings row) => new(
        new AiSettings
        {
            Enabled = row.Enabled,
            Engine = Enum.IsDefined((AiEngineKind)row.Engine) ? (AiEngineKind)row.Engine : AiEngineKind.None,
            Features = Split(row.Features)
                .Select(f => (AssistantFeature)int.Parse(f, CultureInfo.InvariantCulture))
                .Where(Enum.IsDefined)
                .ToHashSet(),
            ExcludedDossiers = Split(row.ExcludedDossiers).Select(d => new DossierId(Guid.Parse(d))).ToHashSet(),
            OfficialWebLookup = row.OfficialWebLookup,
            MaxToolRounds = Math.Clamp(row.MaxToolRounds, 1, SaveAiSettingsRequest.MaxToolRoundsLimit),
        },
        row.Version,
        row.UpdatedAt,
        row.UpdatedBy);

    private static string[] Split(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
