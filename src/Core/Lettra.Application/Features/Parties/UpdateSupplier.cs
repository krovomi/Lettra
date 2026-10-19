using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Parties;

/// <summary>Modifier un fournisseur, à partir de la version lue (ADR-0013).</summary>
public sealed record UpdateSupplierCommand(DossierId Dossier, SupplierId SupplierId, Supplier.Draft Data, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.TiersModifier;
}

internal sealed class UpdateSupplierHandler(
    ISupplierRepository suppliers,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UpdateSupplierCommand, Result>
{
    public async Task<Result> HandleAsync(UpdateSupplierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<Supplier> loaded = Concurrency.Load(
            await suppliers.FindAsync(command.SupplierId, cancellationToken).ConfigureAwait(false), SupplierErrors.NotFound, command.ExpectedVersion);
        if (loaded.IsFailure)
        {
            return Result.Failure(loaded.Errors);
        }

        Supplier supplier = loaded.Value;

        Result updated = supplier.Update(command.Data, clock.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
