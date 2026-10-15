using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Authorization;

/// <summary>
/// Applique la politique du cabinet du dossier courant à l'utilisateur courant. La politique est relue à chaque
/// contrôle : un changement de l'administrateur vaut pour la validation suivante.
/// </summary>
internal sealed class SegregationOfDutiesService(
    ITenantContext tenant,
    ICurrentUser user,
    IPermissionService permissions,
    IOrganisationPolicyStore policies) : ISegregationOfDuties
{
    public async Task<Result> EnsureCanIssueAsync(IReadOnlyCollection<Guid> enteredBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(enteredBy);
        SegregationOfDutiesPolicy policy = await PolicyAsync(cancellationToken).ConfigureAwait(false);
        return policy.Enabled && enteredBy.Contains(user.UserId)
            ? SegregationOfDutiesErrors.EnteredByIssuer
            : Result.Success();
    }

    public async Task<CreditNoteApproval> EvaluateCreditNoteAsync(decimal totalInclTax, CancellationToken cancellationToken)
    {
        SegregationOfDutiesPolicy policy = await PolicyAsync(cancellationToken).ConfigureAwait(false);
        if (!policy.Enabled || policy.CreditNoteApprovalThresholdInclTax is not decimal threshold || Math.Abs(totalInclTax) <= threshold)
        {
            return CreditNoteApproval.NotRequired;
        }

        IReadOnlySet<Permission> granted = await permissions
            .GetEffectiveAsync(user.UserId, tenant.Current.DossierId, cancellationToken).ConfigureAwait(false);
        return granted.Contains(Permission.AvoirApprouver)
            ? CreditNoteApproval.GrantedByCurrentUser
            : CreditNoteApproval.ManagerApprovalRequired;
    }

    public async Task<Result> EnsureCanApproveCreditNoteAsync(Guid createdBy, CancellationToken cancellationToken)
    {
        SegregationOfDutiesPolicy policy = await PolicyAsync(cancellationToken).ConfigureAwait(false);
        return policy.Enabled && createdBy == user.UserId
            ? SegregationOfDutiesErrors.ApproverIsCreator
            : Result.Success();
    }

    private Task<SegregationOfDutiesPolicy> PolicyAsync(CancellationToken cancellationToken) =>
        policies.SupportsSegregationOfDuties
            ? policies.GetSegregationOfDutiesAsync(tenant.Current.OrganisationId, cancellationToken)
            : Task.FromResult(SegregationOfDutiesPolicy.Disabled);
}

/// <summary>Poste isolé (mode 1) : un seul utilisateur, aucune politique de cabinet à appliquer.</summary>
internal sealed class NoOrganisationPolicies : IOrganisationPolicyStore
{
    public bool SupportsSegregationOfDuties => false;

    public Task<SegregationOfDutiesPolicy> GetSegregationOfDutiesAsync(OrganisationId organisation, CancellationToken cancellationToken) =>
        Task.FromResult(SegregationOfDutiesPolicy.Disabled);

    public Task SaveSegregationOfDutiesAsync(
        OrganisationId organisation, SegregationOfDutiesPolicy policy, Guid changedBy, CancellationToken cancellationToken) =>
        throw new NotSupportedException("La séparation des tâches n'existe pas sur le poste isolé.");
}
