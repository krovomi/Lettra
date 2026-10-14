using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Parties;

/// <summary>Créer un fournisseur dans le dossier courant.</summary>
public sealed record CreateSupplierCommand(DossierId Dossier, Supplier.Draft Data)
    : ICommand<Result<SupplierId>>, IRequirePermission
{
    public Permission Permission => Permission.TiersCreer;
}

internal sealed class CreateSupplierHandler(
    ISupplierRepository suppliers,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CreateSupplierCommand, Result<SupplierId>>
{
    public async Task<Result<SupplierId>> HandleAsync(CreateSupplierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<Supplier> created = Supplier.Create(SupplierId.New(), command.Data, clock.GetUtcNow());
        if (created.IsFailure)
        {
            return Result<SupplierId>.Failure(created.Errors);
        }

        await suppliers.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created.Value.Id;
    }
}
