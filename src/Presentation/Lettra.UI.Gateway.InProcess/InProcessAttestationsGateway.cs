using Lettra.Application.Features.Parties;
using Lettra.Contracts.Common;
using Lettra.Contracts.Vigilance;
using Lettra.Domain.Parties;
using Lettra.Domain.Vigilance;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage et mêmes permissions que les routes <c>/attestations</c> de l'API.</summary>
internal sealed class InProcessAttestationsGateway(IServiceScopeFactory scopes) : IAttestationsGateway
{
    public Task<OperationResult<SupplierAttestationsResponse>> ListAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new ListAttestationsQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<SupplierAttestationsResponse>> SaveAsync(Guid supplierId, SaveAttestationRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.WriteThenReadAsync(
                sp, new SaveAttestationCommand(dossier, new SupplierId(supplierId), request.ToDraft(), expectedVersion), new ListAttestationsQuery(dossier), ct),
            cancellationToken);

    public Task<OperationResult<AttestationVerificationResponse>> VerifyAsync(Guid supplierId, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            async (sp, dossier, ct) =>
            {
                OperationResult<UrssafAnswer> answer = await InProcessScope.SendAsync(sp, new VerifyAttestationCodeCommand(dossier, new SupplierId(supplierId)), ct).ConfigureAwait(false);
                if (answer is not { IsSuccess: true, Value: var value })
                {
                    return OperationResult<AttestationVerificationResponse>.Failure(answer.Errors);
                }

                OperationResult<SupplierAttestationsResponse> list = await InProcessScope.QueryAsync(sp, new ListAttestationsQuery(dossier), ct).ConfigureAwait(false);
                return list is { IsSuccess: true, Value: { } rows }
                    ? OperationResult<AttestationVerificationResponse>.Success(new AttestationVerificationResponse((UrssafAnswerDto)(int)value, rows))
                    : OperationResult<AttestationVerificationResponse>.Failure(list.Errors);
            },
            cancellationToken);
}
