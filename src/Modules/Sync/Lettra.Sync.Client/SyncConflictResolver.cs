using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Mapping;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Sync;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Sync.Client;

/// <summary>
/// Résolution d'un champ disputé (ADR-0013). Le serveur a gardé sa valeur et la réplique l'a reçue : « prendre celle du
/// cabinet » efface seulement le conflit ; « garder ma saisie » remet la valeur du poste par la commande de modification,
/// dont l'intention part au cycle suivant avec la version du cabinet comme état de départ.
/// </summary>
internal static class SyncConflictResolver
{
    public static async Task<Result> ResolveAsync(IServiceProvider services, long conflictId, SyncResolution resolution, CancellationToken ct)
    {
        ISyncOutbox outbox = services.GetRequiredService<ISyncOutbox>();
        SyncConflictRecord? conflict = (await outbox.ListConflictsAsync(ct).ConfigureAwait(false)).FirstOrDefault(c => c.Id == conflictId);
        if (conflict is null)
        {
            return SyncErrors.EntityNotFound;
        }

        if (resolution == SyncResolution.KeepMine)
        {
            Result kept = await KeepMineAsync(services, conflict, ct).ConfigureAwait(false);
            if (kept.IsFailure)
            {
                return kept;
            }
        }
        else if (resolution != SyncResolution.TakeServer)
        {
            throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Résolution inconnue.");
        }

        // Une saisie gardée deux fois ne repart qu'une fois : le conflit disparaît avec la première.
        await outbox.RemoveConflictAsync(conflict.Id, ct).ConfigureAwait(false);
        return Result.Success();
    }

    private static Task<Result> KeepMineAsync(IServiceProvider services, SyncConflictRecord conflict, CancellationToken ct)
    {
        ICommandDispatcher commands = services.GetRequiredService<ICommandDispatcher>();
        DossierId dossier = services.GetRequiredService<ITenantContext>().Current.DossierId;
        return conflict.Entity switch
        {
            SyncEntityKind.Customer => ReapplyAsync(
                conflict,
                ContractsJsonContext.Default.CreateCustomerRequest,
                async () => await services.GetRequiredService<ICustomerReadStore>().GetAsync(new CustomerId(conflict.EntityId), ct).ConfigureAwait(false) is { } c
                    ? (c.ToRequest(), c.Version) : null,
                (request, version) => commands.SendAsync(request.ToUpdateCommand(dossier, new CustomerId(conflict.EntityId), version), ct)),
            SyncEntityKind.Supplier => ReapplyAsync(
                conflict,
                ContractsJsonContext.Default.CreateSupplierRequest,
                async () => await services.GetRequiredService<ISupplierReadStore>().GetAsync(new SupplierId(conflict.EntityId), ct).ConfigureAwait(false) is { } s
                    ? (s.ToRequest(), s.Version) : null,
                (request, version) => commands.SendAsync(request.ToUpdateCommand(dossier, new SupplierId(conflict.EntityId), version), ct)),
            SyncEntityKind.CatalogItem => ReapplyAsync(
                conflict,
                ContractsJsonContext.Default.SaveCatalogItemRequest,
                async () => await services.GetRequiredService<ICatalogItemReadStore>().GetAsync(new CatalogItemId(conflict.EntityId), ct).ConfigureAwait(false) is { } i
                    ? (i.ToRequest(), i.Version) : null,
                (request, version) => commands.SendAsync(request.ToUpdateCommand(dossier, new CatalogItemId(conflict.EntityId), version), ct)),
            _ => Task.FromResult<Result>(SyncErrors.EntityNotFound),
        };
    }

    private static async Task<Result> ReapplyAsync<TRequest>(
        SyncConflictRecord conflict,
        JsonTypeInfo<TRequest> typeInfo,
        Func<Task<(TRequest State, long Version)?>> read,
        Func<TRequest, long, Task<Result>> write)
    {
        if (await read().ConfigureAwait(false) is not (TRequest local, long version))
        {
            return SyncErrors.EntityNotFound;
        }

        JsonPropertyInfo? property = typeInfo.Properties.FirstOrDefault(p => MemberName(p) == conflict.Conflict.Field);
        if (property is null)
        {
            return SyncErrors.EntityNotFound;
        }

        JsonObject state = JsonSerializer.SerializeToNode(local, typeInfo)!.AsObject();
        JsonNode? mine = JsonNode.Parse(conflict.Conflict.Mine.GetRawText());
        if (JsonNode.DeepEquals(state[property.Name], mine))
        {
            // La réception a échoué après l'envoi : la réplique a encore la saisie du poste, pas la version du cabinet.
            // Repartir d'ici ne changerait rien au serveur ; il faut d'abord recevoir sa version.
            return SyncErrors.ConflictAwaitingRefresh;
        }

        state[property.Name] = mine;
        return await write(state.Deserialize(typeInfo)!, version).ConfigureAwait(false);
    }

    private static string MemberName(JsonPropertyInfo property) =>
        property.AttributeProvider is MemberInfo member ? member.Name : property.Name;
}
