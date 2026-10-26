using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Parties;

/// <summary>Modifier un fournisseur, à partir de la version lue (ADR-0013). Un nouvel IBAN est à vérifier avant tout paiement.</summary>
public sealed record UpdateSupplierCommand(DossierId Dossier, SupplierId SupplierId, Supplier.Draft Data, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.TiersModifier;
}

internal sealed class UpdateSupplierHandler(
    ISupplierRepository suppliers,
    IUnitOfWork unitOfWork,
    IbanChangeDecisions ibanChanges,
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
        Iban? previousIban = supplier.Iban;

        Result updated = supplier.Update(command.Data, clock.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated;
        }

        // Un nouvel IBAN attend sa vérification par un autre canal (fraude au faux fournisseur) : même transaction.
        bool ibanChanged = await ibanChanges.TrackAsync(supplier, previousIban, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (ibanChanged)
        {
            await ibanChanges.PublishAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }
}
