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
    TimeProvider clock)
{
    /// <summary>Nouvelles tentatives si un collègue écrit le même élément pendant la fusion.</summary>
    private const int MergeAttempts = 3;

    public async Task<Result<PushSyncIntentsResponse>> PushAsync(PushSyncIntentsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DeviceId == Guid.Empty || request.Intents is not { Count: > 0 and <= SyncHttp.MaxBatch } || request.Intents.Any(i => i is null))
        {
            return SyncErrors.BatchInvalid;
        }

        List<SyncIntentResult> results = [];
        foreach (SyncIntent intent in request.Intents)
        {
            results.Add(await PushOneAsync(request.DeviceId, intent, cancellationToken).ConfigureAwait(false));
        }

        return new PushSyncIntentsResponse(results);
    }

    public async Task<Result<SyncChangesResponse>> PullAsync(string? token, int limit, CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated)
        {
            return SecurityErrors.Unauthenticated;
        }

        IReadOnlySet<Permission> granted = await permissions
            .GetEffectiveAsync(user.UserId, AuthorizationScope.ForDossier(tenant.Current.DossierId), cancellationToken)
            .ConfigureAwait(false);
        SyncFeedScope scope = new(granted.Contains(Permission.TiersConsulter), granted.Contains(Permission.CatalogueConsulter));
        if (!scope.Parties && !scope.Catalog)
        {
            return SecurityErrors.Forbidden;
        }

        return await feed.ReadAsync(token, limit, scope, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SyncIntentResult> PushOneAsync(Guid deviceId, SyncIntent intent, CancellationToken cancellationToken)
    {
        string hash = Hash(intent);
        SyncReceipt? known = await receipts.FindAsync(intent.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (known is not null)
        {
            return known.RequestHash == hash
                ? known.Result
                : Rejected(intent, SyncErrors.IdempotencyKeyReused);
        }

        SyncIntentResult result = await work.RunAsync((sp, ct) => IntentReplay.ReplayAsync(sp, intent, ct), cancellationToken).ConfigureAwait(false);
        await receipts.SaveAsync(new SyncReceipt(intent.IdempotencyKey, deviceId, hash, result, clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
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
        public static Task<SyncIntentResult> ReplayAsync(IServiceProvider services, SyncIntent intent, CancellationToken ct)
        {
            Replayer replayer = new(
                services.GetRequiredService<ICommandDispatcher>(),
                services.GetRequiredService<ICustomerReadStore>(),
                services.GetRequiredService<ISupplierReadStore>(),
                services.GetRequiredService<ICatalogItemReadStore>(),
                services.GetRequiredService<ITenantContext>().Current.DossierId);
            return replayer.ReplayAsync(intent, ct);
        }
    }

    private sealed class Replayer(
        ICommandDispatcher commands,
        ICustomerReadStore customers,
        ISupplierReadStore suppliers,
        ICatalogItemReadStore items,
        DossierId dossier)
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
            CreateSupplierIntent c => CreateAsync(
                intent,
                () => Version(suppliers.GetAsync(new SupplierId(c.EntityId), ct)),
                async () => (await commands.SendAsync(c.Data.ToCommand(dossier, new SupplierId(c.EntityId)), ct).ConfigureAwait(false)).Errors),
            UpdateSupplierIntent u => UpdateAsync(
                intent, u.Base, u.Data, ContractsJsonContext.Default.CreateSupplierRequest,
                async () => await suppliers.GetAsync(new SupplierId(u.EntityId), ct).ConfigureAwait(false) is { } s ? (s.ToRequest(), s.Version) : null,
                (merged, version) => commands.SendAsync(merged.ToUpdateCommand(dossier, new SupplierId(u.EntityId), version), ct)),
            CreateCatalogItemIntent c => CreateAsync(
                intent,
                () => Version(items.GetAsync(new CatalogItemId(c.EntityId), ct)),
                async () => (await commands.SendAsync(c.Data.ToCreateCommand(dossier, new CatalogItemId(c.EntityId)), ct).ConfigureAwait(false)).Errors),
            UpdateCatalogItemIntent u => UpdateAsync(
                intent, u.Base, u.Data, ContractsJsonContext.Default.SaveCatalogItemRequest,
                async () => await items.GetAsync(new CatalogItemId(u.EntityId), ct).ConfigureAwait(false) is { } s ? (s.ToRequest(), s.Version) : null,
                (merged, version) => commands.SendAsync(merged.ToUpdateCommand(dossier, new CatalogItemId(u.EntityId), version), ct)),
            SetCatalogItemArchivedIntent a => ArchiveAsync(a, ct),
            _ => Task.FromResult(Rejected(intent, SyncErrors.BatchInvalid)),
        };

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
