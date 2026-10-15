using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Customers;

/// <summary>
/// Modifier un client, à partir de la version lue (ADR-0013). Les documents déjà émis gardent leur copie
/// du client : la modification ne vaut que pour les documents à venir.
/// </summary>
public sealed record UpdateCustomerCommand(DossierId Dossier, CustomerId CustomerId, Customer.Draft Data, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.TiersModifier;
}

internal sealed class UpdateCustomerHandler(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UpdateCustomerCommand, Result>
{
    public async Task<Result> HandleAsync(UpdateCustomerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Customer? customer = await customers.FindAsync(command.CustomerId, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return CustomerErrors.NotFound;
        }

        Result fresh = Concurrency.Check(customer, command.ExpectedVersion);
        if (fresh.IsFailure)
        {
            return fresh;
        }

        Result updated = customer.Update(command.Data, clock.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
