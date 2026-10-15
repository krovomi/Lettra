using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Mapping;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Domain.Billing;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Billing;

/// <summary>Listes fermées de la facturation : régimes de TVA, motifs d'exonération, taux. Le domaine en est la seule source.</summary>
public sealed record GetBillingReferenceDataQuery(DossierId Dossier)
    : IQuery<Result<BillingReferenceData>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Lire la fiche de l'entreprise émettrice (identité, TVA, conditions de paiement par défaut).</summary>
public sealed record GetCompanyProfileQuery(DossierId Dossier)
    : IQuery<Result<CompanyProfileResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Créer ou modifier la fiche de l'entreprise émettrice. Pas de permission dédiée au paramétrage du dossier pour
/// l'instant : on reprend celle du catalogue.
/// </summary>
public sealed record SaveCompanyProfileCommand(DossierId Dossier, CompanyProfile.Draft Data, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.CatalogueGerer;
}

internal sealed class GetBillingReferenceDataHandler
    : IQueryHandler<GetBillingReferenceDataQuery, Result<BillingReferenceData>>
{
    private static readonly BillingReferenceData Data = new(
        [.. Enum.GetValues<VatRegime>().Select(r =>
        {
            (string Category, string Exemption, string LegalText)? treatment = VatTreatment.RegimeTreatment(r);
            return new VatRegimeOption(
                (VatRegimeDto)(int)r,
                treatment is null ? VatCategoryDto.Standard : BillingMapper.ToCategoryDto(treatment.Value.Category),
                treatment?.LegalText);
        })],
        [.. VatTreatment.LineExemptions.Select(e => new VatExemptionReasonOption(e.Code, e.LegalText))],
        [.. VatRate.All.OrderByDescending(r => r.Percent).Select(r => new VatRateOption(r.Code, r.Percent))]);

    public Task<Result<BillingReferenceData>> HandleAsync(GetBillingReferenceDataQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<Result<BillingReferenceData>>(Data);
}

internal sealed class GetCompanyProfileHandler(ICompanyProfileRepository profiles)
    : IQueryHandler<GetCompanyProfileQuery, Result<CompanyProfileResponse>>
{
    public async Task<Result<CompanyProfileResponse>> HandleAsync(GetCompanyProfileQuery query, CancellationToken cancellationToken)
    {
        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        return profile is null ? CompanyProfileErrors.NotFound : profile.ToResponse();
    }
}

internal sealed class SaveCompanyProfileHandler(
    ICompanyProfileRepository profiles,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SaveCompanyProfileCommand, Result>
{
    public async Task<Result> HandleAsync(SaveCompanyProfileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            Result<CompanyProfile> created = CompanyProfile.Create(command.Data, clock.GetUtcNow());
            if (created.IsFailure)
            {
                return Result.Failure(created.Errors);
            }

            await profiles.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Result updated = Concurrency.Check(profile, command.ExpectedVersion);
            if (updated.IsSuccess)
            {
                updated = profile.Update(command.Data, clock.GetUtcNow());
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
