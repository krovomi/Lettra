using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Vigilance;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Domain.Vigilance;

namespace Lettra.Application.Features.Parties;

/// <summary>Les fournisseurs du dossier et leurs attestations de vigilance, ceux à traiter d'abord.</summary>
public sealed record ListAttestationsQuery(DossierId Dossier) : IQuery<Result<SupplierAttestationsResponse>>, IRequirePermission
{
    public Permission Permission => Permission.TiersConsulter;
}

/// <summary>Enregistrer le contrat d'un fournisseur et l'attestation reçue : qui modifie les fournisseurs.</summary>
public sealed record SaveAttestationCommand(DossierId Dossier, SupplierId SupplierId, SubcontractorAttestation.Draft Data, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.TiersModifier;
}

internal sealed class AttestationHandlers(IAttestationStore store, ISupplierRepository suppliers, IUnitOfWork unitOfWork, TimeProvider clock) :
    IQueryHandler<ListAttestationsQuery, Result<SupplierAttestationsResponse>>,
    ICommandHandler<SaveAttestationCommand, Result>
{
    public async Task<Result<SupplierAttestationsResponse>> HandleAsync(ListAttestationsQuery query, CancellationToken cancellationToken) =>
        AttestationOverview.Build(await store.ListAsync(cancellationToken).ConfigureAwait(false), LegalCalendar.DateOf(clock.GetUtcNow()));

    public async Task<Result> HandleAsync(SaveAttestationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await suppliers.FindAsync(command.SupplierId, cancellationToken).ConfigureAwait(false) is not { IsArchived: false })
        {
            return AttestationErrors.SupplierNotFound;
        }

        DateTimeOffset now = clock.GetUtcNow();
        DateOnly today = LegalCalendar.DateOf(now);
        SubcontractorAttestation? attestation = await store.FindAsync(command.SupplierId, cancellationToken).ConfigureAwait(false);
        if (attestation is null)
        {
            Result<SubcontractorAttestation> created = SubcontractorAttestation.Create(command.SupplierId, command.Data, today, now);
            if (created.IsFailure)
            {
                return Result.Failure(created.Errors);
            }

            await store.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Result updated = Concurrency.Check(attestation, command.ExpectedVersion);
            if (updated.IsSuccess)
            {
                updated = attestation.Update(command.Data, today, now);
            }

            if (updated.IsFailure)
            {
                return updated;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Les calculs de l'écran « Attestations de vigilance » et de son indicateur, en fonctions pures.</summary>
public static class AttestationOverview
{
    public static SupplierAttestationsResponse Build(IReadOnlyList<SupplierAttestationEntry> entries, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return new SupplierAttestationsResponse(
        [
            .. entries.Select(e => ToRow(e, today))
                .OrderBy(r => Urgency(r.Status))
                .ThenBy(r => r.SupplierName, StringComparer.CurrentCultureIgnoreCase),
        ]);
    }

    public static AttestationStatus StatusOf(SubcontractorAttestation? attestation, DateOnly today) =>
        attestation?.StatusOn(today) ?? AttestationStatus.NotRequired;

    /// <summary>
    /// L'indicateur de la vue cabinet : les fournisseurs à ne pas payer en l'état (attestation exigée mais absente, périmée
    /// ou non vérifiée), sinon ceux dont l'attestation expire dans les trente jours. Ni nom ni montant.
    /// </summary>
    public static DossierIndicator? Indicator(IReadOnlyList<SubcontractorAttestation> attestations, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(attestations);
        List<(AttestationStatus Status, DateOnly? Until)> tracked = [.. attestations.Select(a => (a.StatusOn(today), a.ValidUntil))];
        List<(AttestationStatus Status, DateOnly? Until)> blocking = [.. tracked.Where(t => SubcontractorAttestation.BlocksPayment(t.Status))];
        if (blocking.Count > 0)
        {
            return new DossierIndicator(DossierIndicatorKeys.SupplierAttestations, blocking.Count,
                blocking.Any(b => b.Status == AttestationStatus.Expired) ? IndicatorLevel.Critical : IndicatorLevel.Warning,
                blocking.Min(b => b.Until));
        }

        List<DateOnly?> soon = [.. tracked.Where(t => t.Status == AttestationStatus.ExpiringSoon).Select(t => t.Until)];
        return soon.Count == 0 ? null : new DossierIndicator(DossierIndicatorKeys.SupplierAttestations, soon.Count, IndicatorLevel.Info, soon.Min());
    }

    public static SupplierAttestationRow ToRow(SupplierAttestationEntry entry, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entry);
        SubcontractorAttestation? a = entry.Attestation;
        return new SupplierAttestationRow(entry.SupplierId.Value, entry.SupplierName, a?.ContractAmountExclTax, a?.IssuedOn, a?.ValidUntil, a?.SecurityCode,
            a?.VerifiedOn, (AttestationStatusDto)(int)StatusOf(a, today), a?.Version);
    }

    public static SubcontractorAttestation.Draft ToDraft(this SaveAttestationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new SubcontractorAttestation.Draft(request.ContractAmountExclTax, request.IssuedOn, request.SecurityCode, request.VerifiedOn);
    }

    /// <summary>Ce qui bloque un paiement d'abord, puis ce qui expire bientôt, puis le reste.</summary>
    private static int Urgency(AttestationStatusDto status) => status switch
    {
        AttestationStatusDto.Expired => 0,
        AttestationStatusDto.Missing => 1,
        AttestationStatusDto.NotVerified => 2,
        AttestationStatusDto.ExpiringSoon => 3,
        AttestationStatusDto.Valid => 4,
        _ => 5,
    };
}
