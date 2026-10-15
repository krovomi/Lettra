using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Policies;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Policies;

/// <summary>Lire la séparation des tâches du cabinet (écran d'administration).</summary>
public sealed record GetSegregationOfDutiesQuery(OrganisationId Organisation)
    : IQuery<Result<SegregationOfDutiesSettings>>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.CabinetAdministrer;
}

/// <summary>Activer, couper ou régler la séparation des tâches du cabinet (annexe F §3.2).</summary>
public sealed record UpdateSegregationOfDutiesCommand(OrganisationId Organisation, bool Enabled, decimal? CreditNoteApprovalThresholdInclTax)
    : ICommand<Result<SegregationOfDutiesSettings>>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.CabinetAdministrer;
}

internal sealed class UpdateSegregationOfDutiesValidator : IValidator<UpdateSegregationOfDutiesCommand>
{
    public IReadOnlyList<Error> Validate(UpdateSegregationOfDutiesCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return instance.CreditNoteApprovalThresholdInclTax is decimal threshold
            && (threshold < 0m || threshold > SegregationOfDutiesPolicy.MaxThreshold || decimal.Round(threshold, 2) != threshold)
            ? [SegregationOfDutiesErrors.ThresholdInvalid]
            : [];
    }
}

internal sealed class GetSegregationOfDutiesHandler(IOrganisationPolicyStore policies)
    : IQueryHandler<GetSegregationOfDutiesQuery, Result<SegregationOfDutiesSettings>>
{
    public async Task<Result<SegregationOfDutiesSettings>> HandleAsync(GetSegregationOfDutiesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        SegregationOfDutiesPolicy policy = await policies.GetSegregationOfDutiesAsync(query.Organisation, cancellationToken).ConfigureAwait(false);
        return new SegregationOfDutiesSettings(policy.Enabled, policy.CreditNoteApprovalThresholdInclTax);
    }
}

internal sealed class UpdateSegregationOfDutiesHandler(IOrganisationPolicyStore policies, ICurrentUser user)
    : ICommandHandler<UpdateSegregationOfDutiesCommand, Result<SegregationOfDutiesSettings>>
{
    public async Task<Result<SegregationOfDutiesSettings>> HandleAsync(UpdateSegregationOfDutiesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!policies.SupportsSegregationOfDuties)
        {
            return SegregationOfDutiesErrors.NotAvailable;
        }

        SegregationOfDutiesPolicy policy = new(command.Enabled, command.CreditNoteApprovalThresholdInclTax);
        await policies.SaveSegregationOfDutiesAsync(command.Organisation, policy, user.UserId, cancellationToken).ConfigureAwait(false);
        return new SegregationOfDutiesSettings(policy.Enabled, policy.CreditNoteApprovalThresholdInclTax);
    }
}
