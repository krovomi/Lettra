using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.External;
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

/// <summary>Les clients et fournisseurs du dossier et leur dernière vérification auprès des registres, ceux à examiner d'abord.</summary>
/// <remarks>Comme la fiche anti-blanchiment, réservé au cabinet : le résultat d'un criblage ne se divulgue pas (art. L561-18).</remarks>
public sealed record ListThirdPartyChecksQuery(DossierId Dossier) : IQuery<Result<ThirdPartyChecksResponse>>, IRequirePermission
{
    public Permission Permission => Permission.VigilanceConsulter;
}

/// <summary>
/// Vérifier un client ou un fournisseur auprès des registres officiels : bénéficiaires effectifs, gel des avoirs (le tiers,
/// ses bénéficiaires et le titulaire de son compte), BODACC et, pour un fournisseur, le code de son attestation de
/// vigilance. La réponse est gardée : c'est la trace de la vigilance du cabinet.
/// </summary>
public sealed record CheckThirdPartyCommand(DossierId Dossier, ThirdPartyKind Kind, Guid PartyId)
    : ICommand<Result<ThirdPartyCheckRow>>, IRequirePermission
{
    public Permission Permission => Permission.VigilanceGerer;
}

/// <summary>Faire vérifier par l'URSSAF le code de l'attestation de vigilance d'un fournisseur ; reconnu, la vérification est notée.</summary>
public sealed record VerifyAttestationCodeCommand(DossierId Dossier, SupplierId SupplierId) : ICommand<Result<UrssafAnswer>>, IRequirePermission
{
    public Permission Permission => Permission.TiersModifier;
}

/// <summary>Lire au registre les bénéficiaires effectifs de l'entreprise du dossier, pour remplir la fiche anti-blanchiment.</summary>
public sealed record LookupRegisteredOwnersQuery(DossierId Dossier) : IQuery<Result<RegisteredOwnersResponse>>, IRequirePermission
{
    public Permission Permission => Permission.VigilanceGerer;
}

internal sealed class ThirdPartyCheckHandlers(
    IThirdPartyCheckStore checks,
    ICustomerRepository customers,
    ISupplierRepository suppliers,
    IAttestationStore attestations,
    ThirdPartyRegisters registers,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock) :
    IQueryHandler<ListThirdPartyChecksQuery, Result<ThirdPartyChecksResponse>>,
    ICommandHandler<CheckThirdPartyCommand, Result<ThirdPartyCheckRow>>,
    ICommandHandler<VerifyAttestationCodeCommand, Result<UrssafAnswer>>
{
    public async Task<Result<ThirdPartyChecksResponse>> HandleAsync(ListThirdPartyChecksQuery query, CancellationToken cancellationToken) =>
        ThirdPartyCheckOverview.Build(await checks.ListAsync(cancellationToken).ConfigureAwait(false), LegalCalendar.DateOf(clock.GetUtcNow()));

    public async Task<Result<ThirdPartyCheckRow>> HandleAsync(CheckThirdPartyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Party? party = command.Kind switch
        {
            ThirdPartyKind.Customer => await customers.FindAsync(new CustomerId(command.PartyId), cancellationToken).ConfigureAwait(false) is { IsArchived: false } c
                ? new Party(c.Name, c.Siren, null)
                : null,
            ThirdPartyKind.Supplier => await suppliers.FindAsync(new SupplierId(command.PartyId), cancellationToken).ConfigureAwait(false) is { IsArchived: false } s
                ? new Party(s.Name, s.Siren, s.AccountHolderName)
                : null,
            _ => null,
        };
        if (party is null)
        {
            return ThirdPartyCheckErrors.PartyNotFound;
        }

        DateTimeOffset now = clock.GetUtcNow();
        DateOnly today = LegalCalendar.DateOf(now);
        SubcontractorAttestation? attestation = command.Kind == ThirdPartyKind.Supplier
            ? await attestations.FindAsync(new SupplierId(command.PartyId), cancellationToken).ConfigureAwait(false)
            : null;
        Result<ThirdPartyFindings> findings = await registers.CheckAsync(party, attestation, cancellationToken).ConfigureAwait(false);
        if (findings.IsFailure)
        {
            return Result<ThirdPartyCheckRow>.Failure(findings.Errors);
        }

        if (findings.Value.Urssaf == UrssafAnswer.Recognized && attestation is { VerifiedOn: null })
        {
            attestation.MarkVerified(today, now);
        }

        ThirdPartyCheck? check = await checks.FindAsync(command.PartyId, cancellationToken).ConfigureAwait(false);
        if (check is null)
        {
            check = ThirdPartyCheck.Create(command.PartyId, command.Kind, findings.Value, user.UserId, now);
            await checks.AddAsync(check, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            check.Record(findings.Value, user.UserId, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ThirdPartyCheckOverview.ToRow(new ThirdPartyEntry(command.Kind, command.PartyId, party.Name, party.Siren, check), today);
    }

    public async Task<Result<UrssafAnswer>> HandleAsync(VerifyAttestationCodeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await suppliers.FindAsync(command.SupplierId, cancellationToken).ConfigureAwait(false) is not { IsArchived: false } supplier)
        {
            return AttestationErrors.SupplierNotFound;
        }

        if (await attestations.FindAsync(command.SupplierId, cancellationToken).ConfigureAwait(false) is not { SecurityCode: { } code, IssuedOn: not null } attestation)
        {
            return AttestationErrors.VerificationNeedsCode;
        }

        if (supplier.Siren is not { } siren)
        {
            return AttestationErrors.SirenMissing;
        }

        Result<UrssafAnswer> answer = await registers.Urssaf.VerifyAsync(siren, code, cancellationToken).ConfigureAwait(false);
        if (answer.IsSuccess && answer.Value == UrssafAnswer.Recognized)
        {
            DateTimeOffset now = clock.GetUtcNow();
            attestation.MarkVerified(LegalCalendar.DateOf(now), now);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return answer;
    }
}

internal sealed class LookupRegisteredOwnersHandler(ICompanyProfileRepository profiles, ThirdPartyRegisters registers)
    : IQueryHandler<LookupRegisteredOwnersQuery, Result<RegisteredOwnersResponse>>
{
    public async Task<Result<RegisteredOwnersResponse>> HandleAsync(LookupRegisteredOwnersQuery query, CancellationToken cancellationToken)
    {
        if (await profiles.FindAsync(cancellationToken).ConfigureAwait(false) is not { Siren: { } siren } profile)
        {
            return new RegisteredOwnersResponse(null, [], null);
        }

        Result<IReadOnlyList<RegisteredOwner>> owners = await registers.Owners.FindAsync(siren, cancellationToken).ConfigureAwait(false);
        if (owners.IsFailure)
        {
            return Result<RegisteredOwnersResponse>.Failure(owners.Errors);
        }

        Result<IReadOnlyList<string>> frozen = await registers.FrozenNamesAsync(cancellationToken).ConfigureAwait(false);
        if (frozen.IsFailure)
        {
            return Result<RegisteredOwnersResponse>.Failure(frozen.Errors);
        }

        return new RegisteredOwnersResponse(
            siren.Value,
            [
                .. owners.Value.Select(o => new RegisteredOwnerDto(
                    o.Name, (OwnershipKindDto)(int)o.Kind, o.SharePercent, AssetFreezeNames.FirstMatch([o.Name], frozen.Value) is not null)),
            ],
            AssetFreezeNames.FirstMatch([profile.Name], frozen.Value));
    }
}

/// <summary>Ce que les registres savent d'un tiers : son nom, son SIREN et le titulaire de son compte (fournisseur).</summary>
internal sealed record Party(string Name, Siren? Siren, string? AccountHolderName);

/// <summary>Les quatre registres interrogés ensemble pour un tiers.</summary>
internal sealed class ThirdPartyRegisters(
    IBeneficialOwnerRegister owners, IAssetFreezeRegister freezes, ILegalNoticeFeed notices, IUrssafAttestationCheck urssaf)
{
    public IBeneficialOwnerRegister Owners => owners;

    public IUrssafAttestationCheck Urssaf => urssaf;

    /// <summary>Tous les noms inscrits au registre des gels, alias compris.</summary>
    public async Task<Result<IReadOnlyList<string>>> FrozenNamesAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<FrozenEntry>> list = await freezes.ListAsync(cancellationToken).ConfigureAwait(false);
        return list.IsFailure
            ? Result<IReadOnlyList<string>>.Failure(list.Errors)
            : Result<IReadOnlyList<string>>.Success([.. list.Value.SelectMany(e => e.Aliases.Prepend(e.Name))]);
    }

    public async Task<Result<ThirdPartyFindings>> CheckAsync(Party party, SubcontractorAttestation? attestation, CancellationToken cancellationToken)
    {
        IReadOnlyList<RegisteredOwner> registered = [];
        IReadOnlyList<LegalNotice> published = [];
        if (party.Siren is { } siren)
        {
            Result<IReadOnlyList<RegisteredOwner>> found = await owners.FindAsync(siren, cancellationToken).ConfigureAwait(false);
            if (found.IsFailure)
            {
                return Result<ThirdPartyFindings>.Failure(found.Errors);
            }

            Result<IReadOnlyList<LegalNotice>> listed = await notices.ListAsync(siren, cancellationToken).ConfigureAwait(false);
            if (listed.IsFailure)
            {
                return Result<ThirdPartyFindings>.Failure(listed.Errors);
            }

            (registered, published) = (found.Value, listed.Value);
        }

        Result<IReadOnlyList<string>> frozen = await FrozenNamesAsync(cancellationToken).ConfigureAwait(false);
        if (frozen.IsFailure)
        {
            return Result<ThirdPartyFindings>.Failure(frozen.Errors);
        }

        UrssafAnswer? answer = null;
        if (party.Siren is { } supplierSiren && attestation is { SecurityCode: { } code, IssuedOn: not null })
        {
            Result<UrssafAnswer> verified = await urssaf.VerifyAsync(supplierSiren, code, cancellationToken).ConfigureAwait(false);
            if (verified.IsFailure)
            {
                return Result<ThirdPartyFindings>.Failure(verified.Errors);
            }

            answer = verified.Value;
        }

        string? match = AssetFreezeNames.FirstMatch([party.Name, party.AccountHolderName, .. registered.Select(o => o.Name)], frozen.Value);
        LegalNotice? notice = published.OrderByDescending(n => n.PublishedOn).FirstOrDefault();
        return new ThirdPartyFindings(
            party.Siren is null ? null : registered.Count,
            match is null ? FreezeScreening.Clear : FreezeScreening.PossibleMatch,
            match,
            notice?.Kind,
            notice?.PublishedOn,
            notice?.Label,
            answer);
    }
}

/// <summary>Les calculs de l'écran « Vérifier les tiers » et de son indicateur, en fonctions pures.</summary>
public static class ThirdPartyCheckOverview
{
    public static ThirdPartyChecksResponse Build(IReadOnlyList<ThirdPartyEntry> entries, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return new ThirdPartyChecksResponse(
        [
            .. entries.Select(e => ToRow(e, today))
                .OrderBy(r => Urgency(r.Status))
                .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
        ]);
    }

    public static ThirdPartyCheckRow ToRow(ThirdPartyEntry entry, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ThirdPartyCheck? c = entry.Check;
        return new ThirdPartyCheckRow(
            (ThirdPartyKindDto)(int)entry.Kind,
            entry.PartyId,
            entry.Name,
            entry.Siren?.Value,
            c is null ? ThirdPartyCheckStatusDto.NeverChecked : (ThirdPartyCheckStatusDto)(int)c.StatusOn(today),
            c?.CheckedAt,
            c?.OwnersFound,
            (FreezeScreeningDto?)(int?)c?.Freeze,
            c?.FreezeMatch,
            (LegalNoticeKindDto?)(int?)c?.Notice,
            c?.NoticeOn,
            c?.NoticeLabel,
            (UrssafAnswerDto?)(int?)c?.Urssaf);
    }

    /// <summary>
    /// L'indicateur de la vue cabinet : les tiers dont la dernière vérification demande un examen, critique dès qu'un nom
    /// ressemble à une personne gelée. Ni nom ni motif.
    /// </summary>
    public static DossierIndicator? Indicator(IReadOnlyList<ThirdPartyCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        List<ThirdPartyCheck> review = [.. checks.Where(c => c.NeedsReview)];
        return review.Count == 0
            ? null
            : new DossierIndicator(DossierIndicatorKeys.ThirdPartiesToReview, review.Count,
                review.Any(c => c.Freeze == FreezeScreening.PossibleMatch) ? IndicatorLevel.Critical : IndicatorLevel.Warning);
    }

    /// <summary>À examiner d'abord, puis jamais vérifié, puis à refaire, puis le reste.</summary>
    private static int Urgency(ThirdPartyCheckStatusDto status) => status switch
    {
        ThirdPartyCheckStatusDto.ToReview => 0,
        ThirdPartyCheckStatusDto.NeverChecked => 1,
        ThirdPartyCheckStatusDto.Outdated => 2,
        _ => 3,
    };
}
