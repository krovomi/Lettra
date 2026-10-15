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
        Supplier? supplier = await suppliers.FindAsync(command.SupplierId, cancellationToken).ConfigureAwait(false);
        if (supplier is null)
        {
            return SupplierErrors.NotFound;
        }

        Result fresh = Concurrency.Check(supplier, command.ExpectedVersion);
        if (fresh.IsFailure)
        {
            return fresh;
        }

        Result updated = supplier.Update(command.Data, clock.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
