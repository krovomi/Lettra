using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Customers;

/// <summary>
/// Supprimer un client, à partir de la version lue (ADR-0013). Il disparaît des listes et des choix de saisie, mais
/// reste en base : ses devis et factures le désignent, et une pièce numérotée ne perd jamais son client. Supprimer un
/// client déjà supprimé réussit sans rien écrire (renvoi d'une synchronisation, double clic).
/// </summary>
public sealed record DeleteCustomerCommand(DossierId Dossier, CustomerId CustomerId, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.TiersModifier;
}

internal sealed class DeleteCustomerHandler(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<DeleteCustomerCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteCustomerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Customer? customer = await customers.FindAsync(command.CustomerId, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return CustomerErrors.NotFound;
        }

        if (customer.IsArchived)
        {
            return Result.Success();
        }

        Result fresh = Concurrency.Check(customer, command.ExpectedVersion);
        if (fresh.IsFailure)
        {
            return fresh;
        }

        customer.SetArchived(true, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
