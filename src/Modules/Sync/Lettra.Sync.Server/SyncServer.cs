using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Catalog;
using Lettra.Application.Features.Customers;
using Lettra.Application.Features.Parties;
using Lettra.Application.Features.Sync;
using Lettra.Application.Mapping;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Suppliers;
using Lettra.Contracts.Sync;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Sync.Server;

/// <summary>
/// Synchronisation côté serveur (ADR-0008). Chaque intention est rejouée par le cas d'usage qu'elle décrit, dans sa
/// propre portée, avec les droits de l'utilisateur au moment de la synchronisation (pas ceux de la saisie) : le pipeline
/// vérifie dossier, permission et règles comme pour un appel de l'API. Une modification se fusionne champ par champ
/// sur le contenu vu par le poste, jamais sur une version.
/// <para>
/// Idempotence : la réponse est gardée par clé avec l'empreinte de l'intention. Un renvoi identique rend la même réponse
/// sans rien rejouer ; la même clé avec un autre contenu est refusée. Si le serveur tombe entre l'écriture et la
/// réponse gardée, le rejeu retombe sur ses pieds : la création trouve l'élément, la fusion ne trouve rien à changer.
/// </para>
/// </summary>
public sealed class SyncServer(
    IDossierWorkScope work,
    ISyncReceipts receipts,
    ISyncChangeFeed feed,
    ITenantContext tenant,
    ICurrentUser user,
    IPermissionService permissions,
    IDeviceSeriesRegistry deviceSeries,
    ISyncDeviceRegistry devices,
    TimeProvider clock)
{
    /// <summary>Les séries du dossier qui se numérotent sur un poste hors ligne (les devis restent au serveur).</summary>
    private static readonly string[] OfflineSeries = [NumberSeriesCodes.Invoices];

    /// <summary>Nouvelles tentatives si un collègue écrit le même élément pendant la fusion.</summary>
    private const int MergeAttempts = 3;

    public async Task<Result<PushSyncIntentsResponse>> PushAsync(PushSyncIntentsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DeviceId == Guid.Empty || request.Intents is not { Count: > 0 and <= SyncHttp.MaxBatch } || request.Intents.Any(i => i is null))
        {
            return SyncErrors.BatchInvalid;
        }

        Result known = await TouchAsync(request.DeviceId, cancellationToken).ConfigureAwait(false);
        if (known.IsFailure)
        {
            return Result<PushSyncIntentsResponse>.Failure(known.Errors);
        }

        List<SyncIntentResult> results = [];
        foreach (SyncIntent intent in request.Intents)
        {
            results.Add(await PushOneAsync(request.DeviceId, intent, cancellationToken).ConfigureAwait(false));
        }

        return new PushSyncIntentsResponse(results);
    }

    /// <summary>
    /// Changements depuis <paramref name="token"/>. Avec <paramref name="deviceId"/> (en-tête du poste), le contact est
    /// inscrit au registre et la règle du cabinet part avec la dernière page.
    /// </summary>
    public async Task<Result<SyncChangesResponse>> PullAsync(string? token, int limit, CancellationToken cancellationToken, Guid? deviceId = null)
    {
        if (!user.IsAuthenticated)
        {
            return SecurityErrors.Unauthenticated;
        }

        if (deviceId is Guid device)
        {
            Result known = await TouchAsync(device, cancellationToken).ConfigureAwait(false);
            if (known.IsFailure)
            {
                return Result<SyncChangesResponse>.Failure(known.Errors);
            }
        }

        IReadOnlySet<Permission> granted = await permissions
            .GetEffectiveAsync(user.UserId, AuthorizationScope.ForDossier(tenant.Current.DossierId), cancellationToken)
            .ConfigureAwait(false);
        SyncFeedScope scope = new(granted.Contains(Permission.TiersConsulter), granted.Contains(Permission.CatalogueConsulter));
        if (!scope.Parties && !scope.Catalog)
        {
            return SecurityErrors.Forbidden;
        }

        Result<SyncChangesResponse> changes = await feed.ReadAsync(token, limit, scope, cancellationToken).ConfigureAwait(false);
        if (changes.IsFailure || changes.Value.HasMore)
        {
            return changes;
        }

        int hours = await devices.GetMaxOfflineHoursAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> released = deviceId is Guid caller
            ? await devices.ListReleasesAsync(caller, cancellationToken).ConfigureAwait(false)
            : [];
        return changes.Value with { Policy = new SyncPolicyResponse(hours), Released = released.Count > 0 ? released : null };
    }

    /// <summary>
    /// Ouvre, ou rend, les séries du poste (ADR-0007). Réservé à qui peut valider une facture : les autres ne
    /// numérotent pas hors ligne.
    /// </summary>
    public async Task<Result<OpenDeviceSeriesResponse>> OpenDeviceSeriesAsync(OpenDeviceSeriesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!user.IsAuthenticated)
        {
            return SecurityErrors.Unauthenticated;
        }

        if (request.DeviceId == Guid.Empty)
        {
            return RightsSnapshotErrors.DeviceRequired;
        }

        Result known = await TouchAsync(request.DeviceId, cancellationToken).ConfigureAwait(false);
        if (known.IsFailure)
        {
            return Result<OpenDeviceSeriesResponse>.Failure(known.Errors);
        }

        IReadOnlySet<Permission> granted = await permissions
            .GetEffectiveAsync(user.UserId, AuthorizationScope.ForDossier(tenant.Current.DossierId), cancellationToken)
            .ConfigureAwait(false);
        if (!granted.Contains(Permission.FactureValider))
        {
            return SecurityErrors.Forbidden;
        }

        List<DeviceSeriesResponse> opened = [];
        foreach (string series in OfflineSeries)
        {
            DeviceSeries open = await deviceSeries.OpenAsync(request.DeviceId, series, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            opened.Add(new DeviceSeriesResponse(open.BaseSeries, open.Code, open.Prefix, open.Next));
        }

        return new OpenDeviceSeriesResponse(opened);
    }

    /// <summary>Un poste révoqué est refusé, quel que soit l'utilisateur connecté (étape 4.4).</summary>
    private Task<Result> TouchAsync(Guid deviceId, CancellationToken cancellationToken) =>
        devices.TouchAsync(deviceId, user.UserId, clock.GetUtcNow(), cancellationToken);

    private async Task<SyncIntentResult> PushOneAsync(Guid deviceId, SyncIntent intent, CancellationToken cancellationToken)
    {
        string hash = Hash(intent);
        SyncReceipt? known = await receipts.FindAsync(intent.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (known is not null && known.RequestHash != hash)
        {
            return Rejected(intent, SyncErrors.IdempotencyKeyReused);
        }

        // Une pièce dont la série a été débloquée (étape 4.4) est rejouée, même refusée la première fois.
        string? number = (intent as ValidatedInvoiceFact)?.Document.Number;
        Guid? releasedBy = number is null ? null : await devices.FindReleaseAsync(deviceId, number, cancellationToken).ConfigureAwait(false);
        if (known is not null && (releasedBy is null || known.Result.Outcome != SyncIntentOutcome.Rejected))
        {
            return known.Result;
        }

        SyncIntentResult result = await work.RunAsync((sp, ct) => IntentReplay.ReplayAsync(sp, intent, deviceId, releasedBy, ct), cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset now = clock.GetUtcNow();
        if (releasedBy is not null && result.Outcome != SyncIntentOutcome.Rejected)
        {
            await devices.ConsumeReleaseAsync(deviceId, number!, now, cancellationToken).ConfigureAwait(false);
        }

        // Une pièce qui attend la précédente n'est pas refusée pour de bon : pas de réponse gardée, elle reviendra.
        if (result.IsWaiting())
        {
            return result;
        }

        SyncReceipt receipt = new(intent.IdempotencyKey, deviceId, hash, result, now);
        await (known is null ? receipts.SaveAsync(receipt, cancellationToken) : receipts.ReplaceAsync(receipt, cancellationToken)).ConfigureAwait(false);
        return result;
    }

    /// <summary>Empreinte SHA-256 du contenu sérialisé de l'intention (format des contrats, stable).</summary>
    internal static string Hash(SyncIntent intent) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(intent, ContractsJsonContext.Default.SyncIntent)));

    internal static SyncIntentResult Rejected(SyncIntent intent, params IReadOnlyList<Error> errors) =>
        new(intent.IdempotencyKey, intent.EntityId, SyncIntentOutcome.Rejected, null, [], errors.ToErrorDetails());

    /// <summary>Le rejeu d'une intention, dans une portée neuve sur le dossier.</summary>
    private static class IntentReplay
    {
        public static Task<SyncIntentResult> ReplayAsync(IServiceProvider services, SyncIntent intent, Guid deviceId, Guid? releasedBy, CancellationToken ct)
        {
            Replayer replayer = new(
                services.GetRequiredService<ICommandDispatcher>(),
                services.GetRequiredService<ICustomerReadStore>(),
                services.GetRequiredService<ISupplierReadStore>(),
                services.GetRequiredService<ICatalogItemReadStore>(),
                services.GetRequiredService<ITenantContext>().Current.DossierId,
                deviceId,
                releasedBy);
            return replayer.ReplayAsync(intent, ct);
        }
    }

    private sealed class Replayer(
        ICommandDispatcher commands,
        ICustomerReadStore customers,
        ISupplierReadStore suppliers,
        ICatalogItemReadStore items,
        DossierId dossier,
        Guid deviceId,
        Guid? releasedBy)
    {
        public Task<SyncIntentResult> ReplayAsync(SyncIntent intent, CancellationToken ct) => intent switch
        {
            CreateCustomerIntent c => CreateAsync(
                intent,
                () => Version(customers.GetAsync(new CustomerId(c.EntityId), ct)),
                async () => (await commands.SendAsync(c.Data.ToCommand(dossier, new CustomerId(c.EntityId)), ct).ConfigureAwait(false)).Errors),
            UpdateCustomerIntent u => UpdateAsync(
                intent, u.Base, u.Data, ContractsJsonContext.Default.CreateCustomerRequest,
                async () => await customers.GetAsync(new CustomerId(u.EntityId), ct).ConfigureAwait(false) is { } s ? (s.ToRequest(), s.Version) : null,
                (merged, version) => commands.SendAsync(merged.ToUpdateCommand(dossier, new CustomerId(u.EntityId), version), ct)),
            DeleteCustomerIntent d => DeleteAsync(
                intent,
                async () => await customers.GetAsync(new CustomerId(d.EntityId), ct).ConfigureAwait(false) is { } s ? (s.IsArchived, s.Version) : null,
                version => commands.SendAsync(new DeleteCustomerCommand(dossier, new CustomerId(d.EntityId), version), ct)),
            CreateSupplierIntent c => CreateAsync(
                intent,
                () => Version(suppliers.GetAsync(new SupplierId(c.EntityId), ct)),
                async () => (await commands.SendAsync(c.Data.ToCommand(dossier, new SupplierId(c.EntityId)), ct).ConfigureAwait(false)).Errors),
            UpdateSupplierIntent u => UpdateAsync(
                intent, u.Base, u.Data, ContractsJsonContext.Default.CreateSupplierRequest,
                async () => await suppliers.GetAsync(new SupplierId(u.EntityId), ct).ConfigureAwait(false) is { } s ? (s.ToRequest(), s.Version) : null,
                (merged, version) => commands.SendAsync(merged.ToUpdateCommand(dossier, new SupplierId(u.EntityId), version), ct)),
            DeleteSupplierIntent d => DeleteAsync(
                intent,
                async () => await suppliers.GetAsync(new SupplierId(d.EntityId), ct).ConfigureAwait(false) is { } s ? (s.IsArchived, s.Version) : null,
                version => commands.SendAsync(new DeleteSupplierCommand(dossier, new SupplierId(d.EntityId), version), ct)),
            CreateCatalogItemIntent c => CreateAsync(
                intent,
                () => Version(items.GetAsync(new CatalogItemId(c.EntityId), ct)),
                async () => (await commands.SendAsync(c.Data.ToCreateCommand(dossier, new CatalogItemId(c.EntityId)), ct).ConfigureAwait(false)).Errors),
            UpdateCatalogItemIntent u => UpdateAsync(
                intent, u.Base, u.Data, ContractsJsonContext.Default.SaveCatalogItemRequest,
                async () => await items.GetAsync(new CatalogItemId(u.EntityId), ct).ConfigureAwait(false) is { } s ? (s.ToRequest(), s.Version) : null,
                (merged, version) => commands.SendAsync(merged.ToUpdateCommand(dossier, new CatalogItemId(u.EntityId), version), ct)),
            SetCatalogItemArchivedIntent a => ArchiveAsync(a, ct),
            ValidatedInvoiceFact f => ImportAsync(f, ct),
            _ => Task.FromResult(Rejected(intent, SyncErrors.BatchInvalid)),
        };

        /// <summary>Un fait : reçu tel quel après revérification, ou refusé ; jamais fusionné ni en conflit.</summary>
        private async Task<SyncIntentResult> ImportAsync(ValidatedInvoiceFact fact, CancellationToken ct)
        {
            Result<SyncIntentOutcome> imported = await commands.SendAsync(new ImportOfflineInvoiceCommand(dossier, deviceId, fact, releasedBy), ct).ConfigureAwait(false);
            return imported.IsSuccess
                ? new SyncIntentResult(fact.IdempotencyKey, fact.EntityId, imported.Value, null, [], [])
                : Rejected(fact, imported.Errors);
        }

        private static async Task<long?> Version<T>(Task<T?> read)
            where T : class
        {
            T? found = await read.ConfigureAwait(false);
            return found switch
            {
                CustomerResponse c => c.Version,
                SupplierResponse s => s.Version,
                CatalogItemResponse i => i.Version,
                _ => null,
            };
        }

        /// <summary>Création avec l'identifiant du poste ; un élément déjà présent veut dire qu'elle est déjà passée.</summary>
        private static async Task<SyncIntentResult> CreateAsync(SyncIntent intent, Func<Task<long?>> version, Func<Task<IReadOnlyList<Error>>> create)
        {
            if (await version().ConfigureAwait(false) is long existing)
            {
                return Outcome(intent, SyncIntentOutcome.AlreadyApplied, existing);
            }

            IReadOnlyList<Error> errors = await create().ConfigureAwait(false);
            return errors.Count > 0
                ? Rejected(intent, errors)
                : Outcome(intent, SyncIntentOutcome.Applied, await version().ConfigureAwait(false));
        }

        private static async Task<SyncIntentResult> UpdateAsync<TRequest>(
            SyncIntent intent,
            TRequest @base,
            TRequest mine,
            JsonTypeInfo<TRequest> typeInfo,
            Func<Task<(TRequest State, long Version)?>> read,
            Func<TRequest, long, Task<Result>> write)
        {
            for (int attempt = 1; ; attempt++)
            {
                if (await read().ConfigureAwait(false) is not (TRequest server, long version))
                {
                    return Rejected(intent, SyncErrors.EntityNotFound);
                }

                MergeOutcome<TRequest> merge = FieldMerge.Merge(@base, mine, server, typeInfo);
                if (!merge.MineChanged)
                {
                    return merge.Conflicts.Count > 0
                        ? Outcome(intent, SyncIntentOutcome.Conflict, version, merge.Conflicts)
                        : Outcome(intent, SyncIntentOutcome.AlreadyApplied, version);
                }

                Result written = await write(merge.Merged, version).ConfigureAwait(false);
                if (written.IsFailure && written.Errors.Any(e => e.Code == ConcurrencyErrors.VersionMismatchCode) && attempt < MergeAttempts)
                {
                    continue;
                }

                if (written.IsFailure)
                {
                    return Rejected(intent, written.Errors);
                }

                long? now = await read().ConfigureAwait(false) is (_, long v) ? v : null;
                SyncIntentOutcome outcome = merge.Conflicts.Count > 0 ? SyncIntentOutcome.Conflict
                    : merge.ServerChanged ? SyncIntentOutcome.Merged
                    : SyncIntentOutcome.Applied;
                return Outcome(intent, outcome, now, merge.Conflicts);
            }
        }

        /// <summary>
        /// Suppression d'un tiers : rejouée sur la version du moment, car une modification faite au bureau entre-temps
        /// n'empêche pas de retirer le tiers des listes. Déjà supprimé : rien à faire.
        /// </summary>
        private static async Task<SyncIntentResult> DeleteAsync(
            SyncIntent intent,
            Func<Task<(bool IsArchived, long Version)?>> read,
            Func<long, Task<Result>> delete)
        {
            for (int attempt = 1; ; attempt++)
            {
                if (await read().ConfigureAwait(false) is not (bool archived, long version))
                {
                    return Rejected(intent, SyncErrors.EntityNotFound);
                }

                if (archived)
                {
                    return Outcome(intent, SyncIntentOutcome.AlreadyApplied, version);
                }

                Result written = await delete(version).ConfigureAwait(false);
                if (written.IsFailure && written.Errors.Any(e => e.Code == ConcurrencyErrors.VersionMismatchCode) && attempt < MergeAttempts)
                {
                    continue;
                }

                return written.IsFailure
                    ? Rejected(intent, written.Errors)
                    : Outcome(intent, SyncIntentOutcome.Applied, await read().ConfigureAwait(false) is (_, long now) ? now : null);
            }
        }

        private async Task<SyncIntentResult> ArchiveAsync(SetCatalogItemArchivedIntent intent, CancellationToken ct)
        {
            for (int attempt = 1; ; attempt++)
            {
                CatalogItemResponse? server = await items.GetAsync(new CatalogItemId(intent.EntityId), ct).ConfigureAwait(false);
                if (server is null)
                {
                    return Rejected(intent, SyncErrors.EntityNotFound);
                }

                if (server.IsArchived == intent.IsArchived)
                {
                    return Outcome(intent, SyncIntentOutcome.AlreadyApplied, server.Version);
                }

                Result written = await commands.SendAsync(
                    new SetCatalogItemArchivedCommand(dossier, new CatalogItemId(intent.EntityId), intent.IsArchived, server.Version), ct).ConfigureAwait(false);
                if (written.IsFailure && written.Errors.Any(e => e.Code == ConcurrencyErrors.VersionMismatchCode) && attempt < MergeAttempts)
                {
                    continue;
                }

                return written.IsFailure
                    ? Rejected(intent, written.Errors)
                    : Outcome(intent, SyncIntentOutcome.Applied, (await items.GetAsync(new CatalogItemId(intent.EntityId), ct).ConfigureAwait(false))?.Version);
            }
        }

        private static SyncIntentResult Outcome(SyncIntent intent, SyncIntentOutcome outcome, long? version, IReadOnlyList<SyncFieldConflict>? conflicts = null) =>
            new(intent.IdempotencyKey, intent.EntityId, outcome, version, conflicts ?? [], []);
    }
}

public static class DependencyInjection
{
    /// <summary>Synchronisation côté serveur (modes 2, 3 et 5). Le journal des changements est activé par la persistance (<c>AddSyncJournal</c>).</summary>
    public static IServiceCollection AddSyncServer(this IServiceCollection services)
    {
        services.AddScoped<SyncServer>();
        return services;
    }
}
