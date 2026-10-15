using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Catalog;

/// <summary>Ajouter un article au catalogue du dossier, avec l'identifiant choisi par l'appelant s'il en donne un.</summary>
public sealed record CreateCatalogItemCommand(DossierId Dossier, CatalogItem.Draft Data, CatalogItemId? Id = null)
    : ICommand<Result<CatalogItemId>>, IRequirePermission
{
    public Permission Permission => Permission.CatalogueGerer;
}

/// <summary>Modifier un article. Les documents déjà émis gardent leur copie de la ligne.</summary>
public sealed record UpdateCatalogItemCommand(DossierId Dossier, CatalogItemId CatalogItemId, CatalogItem.Draft Data, long? ExpectedVersion = null)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.CatalogueGerer;
}

/// <summary>Archiver (ou réactiver) un article : on ne supprime pas, un document ancien peut y renvoyer.</summary>
public sealed record SetCatalogItemArchivedCommand(DossierId Dossier, CatalogItemId CatalogItemId, bool IsArchived, long? ExpectedVersion = null)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.CatalogueGerer;
}

internal sealed class CreateCatalogItemHandler(
    ICatalogItemRepository items,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CreateCatalogItemCommand, Result<CatalogItemId>>
{
    public async Task<Result<CatalogItemId>> HandleAsync(CreateCatalogItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<CatalogItem> created = CatalogItem.Create(command.Id ?? CatalogItemId.New(), command.Data, clock.GetUtcNow());
        if (created.IsFailure)
        {
            return Result<CatalogItemId>.Failure(created.Errors);
        }

        // L'unicité se vérifie ici pour un message clair ; l'index unique de la base reste le garde-fou.
        if (await items.ReferenceExistsAsync(created.Value.ReferenceKey, except: null, cancellationToken).ConfigureAwait(false))
        {
            return CatalogItemErrors.ReferenceAlreadyUsed;
        }

        await items.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created.Value.Id;
    }
}

internal sealed class UpdateCatalogItemHandler(
    ICatalogItemRepository items,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UpdateCatalogItemCommand, Result>
{
    public async Task<Result> HandleAsync(UpdateCatalogItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        CatalogItem? item = await items.FindAsync(command.CatalogItemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return CatalogItemErrors.NotFound;
        }

        Result fresh = Concurrency.Check(item, command.ExpectedVersion);
        if (fresh.IsFailure)
        {
            return fresh;
        }

        string key = CatalogItem.KeyOf(command.Data.Reference);
        if (key.Length > 0 && key != item.ReferenceKey
            && await items.ReferenceExistsAsync(key, item.Id, cancellationToken).ConfigureAwait(false))
        {
            return CatalogItemErrors.ReferenceAlreadyUsed;
        }

        Result updated = item.Update(command.Data, clock.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

internal sealed class SetCatalogItemArchivedHandler(
    ICatalogItemRepository items,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SetCatalogItemArchivedCommand, Result>
{
    public async Task<Result> HandleAsync(SetCatalogItemArchivedCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        CatalogItem? item = await items.FindAsync(command.CatalogItemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return CatalogItemErrors.NotFound;
        }

        Result fresh = Concurrency.Check(item, command.ExpectedVersion);
        if (fresh.IsFailure)
        {
            return fresh;
        }

        item.SetArchived(command.IsArchived, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
