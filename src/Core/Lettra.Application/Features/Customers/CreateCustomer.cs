using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Customers;

/// <summary>Créer un client dans le dossier courant.</summary>
public sealed record CreateCustomerCommand(DossierId Dossier, Customer.Draft Data)
    : ICommand<Result<CustomerId>>, IRequirePermission
{
    public Permission Permission => Permission.TiersCreer;
}

internal sealed class CreateCustomerHandler(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CreateCustomerCommand, Result<CustomerId>>
{
    public async Task<Result<CustomerId>> HandleAsync(CreateCustomerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<Customer> created = Customer.Create(CustomerId.New(), command.Data, clock.GetUtcNow());
        if (created.IsFailure)
        {
            return Result<CustomerId>.Failure(created.Errors);
        }

        await customers.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created.Value.Id;
    }
}
