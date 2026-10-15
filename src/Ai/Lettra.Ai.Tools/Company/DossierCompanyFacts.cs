using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Features.Billing;
using Lettra.Contracts.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Ai.Tools.Company;

/// <summary>
/// Paramètres du dossier lus dans la fiche entreprise du module Facturation, par sa requête : donc avec les droits de
/// l'utilisateur, jamais en accès direct à la base. Fiche absente ou illisible : rien n'est renseigné.
/// </summary>
internal sealed class DossierCompanyFacts(IQueryDispatcher queries) : ICompanyFactsSource
{
    public async Task<CompanyFacts?> GetAsync(DossierId dossier, CancellationToken cancellationToken)
    {
        Result<CompanyProfileResponse> profile = await queries
            .QueryAsync(new GetCompanyProfileQuery(dossier), cancellationToken)
            .ConfigureAwait(false);
        return profile.IsSuccess ? ToFacts(profile.Value) : null;
    }

    internal static CompanyFacts ToFacts(CompanyProfileResponse profile) => new()
    {
        Name = profile.Name,
        LegalForm = profile.LegalForm,
        ShareCapital = profile.ShareCapital,
        Siren = profile.Siren,
        Siret = profile.Siret,
        Rcs = profile.Rcs,
        VatNumber = profile.VatNumber,
        AddressLine1 = profile.AddressLine1,
        AddressLine2 = profile.AddressLine2,
        PostalCode = profile.PostalCode,
        City = profile.City,
        CountryCode = profile.CountryCode,
        VatFranchise = profile.VatFranchise,
        VatOnDebits = profile.VatOnDebits,
        VatRegime = ToPaperasse(profile.VatDeclarationRegime),
        PaymentDays = profile.PaymentDays,
        PaymentTerms = profile.PaymentTerms,
        LatePenaltyRate = profile.LatePenaltyRate,
        EarlyPaymentDiscount = profile.EarlyPaymentDiscount,
        Iban = profile.Iban,
        Bic = profile.Bic,

        // Les avoirs sont numérotés dans la série des factures (pas de série dédiée).
        InvoicePrefix = NumberSeriesCodes.Invoices,
        CreditNotePrefix = NumberSeriesCodes.Invoices,
        QuotePrefix = NumberSeriesCodes.Quotes,
    };

    /// <summary>Régime de déclaration au format de <c>tax.regime_tva</c> de Paperasse.</summary>
    internal static string? ToPaperasse(VatDeclarationRegimeDto? regime) => regime switch
    {
        VatDeclarationRegimeDto.RealNormal => "reel_normal",
        VatDeclarationRegimeDto.RealSimplified => "reel_simplifie",
        VatDeclarationRegimeDto.MiniReal => "mini_reel",
        _ => null,
    };
}
