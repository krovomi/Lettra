using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Parties;

/// <summary>
/// Supprimer un fournisseur, à partir de la version lue (ADR-0013) : retiré des listes, gardé en base comme un client
/// supprimé (voir <c>DeleteCustomerCommand</c>). Un fournisseur déjà supprimé : succès sans écriture.
/// </summary>
public sealed record DeleteSupplierCommand(DossierId Dossier, SupplierId SupplierId, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.TiersModifier;
}

internal sealed class DeleteSupplierHandler(
    ISupplierRepository suppliers,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<DeleteSupplierCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteSupplierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Supplier? supplier = await suppliers.FindAsync(command.SupplierId, cancellationToken).ConfigureAwait(false);
        if (supplier is null)
        {
            return SupplierErrors.NotFound;
        }

        if (supplier.IsArchived)
        {
            return Result.Success();
        }

        Result fresh = Concurrency.Check(supplier, command.ExpectedVersion);
        if (fresh.IsFailure)
        {
            return fresh;
        }

        supplier.SetArchived(true, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
