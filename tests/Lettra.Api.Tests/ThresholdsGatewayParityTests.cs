using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Contracts.Tax;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Les seuils qui approchent par la même interface, en HTTP et en direct : la fiche entreprise décide des seuils de TVA
/// et de catégorie, les factures validées font le chiffre d'affaires, les honoraires notés pour l'exercice comptent pour
/// la DAS 2 du bénéficiaire.
/// </summary>
public sealed class ThresholdsGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    private static DateOnly Today => LegalCalendar.DateOf(DateTimeOffset.UtcNow);

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Une_entreprise_en_franchise_suit_ses_seuils_de_tva_sa_categorie_et_ses_honoraires(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        InvoiceResponse invoice = await GatewayHarness.ValidatedInvoiceAsync(services);
        SaveCompanyProfileRequest franchise = GatewayHarness.CompanyProfile();
        franchise.VatFranchise = true;
        franchise.Size = CompanySizeDto.Micro;
        (await services.GetRequiredService<IBillingSettingsGateway>().SaveCompanyProfileAsync(franchise, null, default)).IsSuccess.Should().BeTrue();

        CreateSupplierRequest request = PartiesAndCatalogApiTests.Dupuis();
        request.Name = "Cabinet d'avocats " + Guid.CreateVersion7().ToString("N")[^8..];
        SupplierResponse lawyer = (await services.GetRequiredService<ISuppliersGateway>().CreateAsync(request, default)).Value!;
        IFeeReturnGateway fees = services.GetRequiredService<IFeeReturnGateway>();
        DateOnly start = (await fees.GetAsync(Today, default)).Value!.FiscalYearStart;
        OperationResult<FeeReturnResponse> tracked = await fees.SaveAsync(
            lawyer.Id, new SaveFeeBeneficiaryRequest { Nature = FeeNatureDto.Fees, Profession = "Avocat", FiscalYearStart = start, PaidOutside = 2_500m }, null, default);
        tracked.IsSuccess.Should().BeTrue(GatewayHarness.Codes(tracked));

        OperationResult<ThresholdsResponse> read = await services.GetRequiredService<ITaxGateway>().GetThresholdsAsync(default);

        read.IsSuccess.Should().BeTrue(GatewayHarness.Codes(read));
        ThresholdsResponse view = read.Value!;
        (view.Today, view.RateDays, view.SoonMonths, view.ProfileMissing).Should().Be((Today, 90, 3, false));
        view.Rows.Select(r => r.Code).Should().Contain(["tva.franchise.services.majore", "tva.franchise.services.base", "taille.micro"]);
        view.Rows.Should().NotContain(r => r.Family == ThresholdFamilyDto.VatSimplifiedRegime, "pas de régime simplifié en franchise");
        view.Rows.Single(r => r.Code == "tva.franchise.services.majore").ToDate.Should().BeGreaterThanOrEqualTo(invoice.Totals.TotalExclTax);
        ThresholdWatchResponse das2 = view.Rows.Single(r => r.PartyId == lawyer.Id);
        das2.Should().Match<ThresholdWatchResponse>(r => r.Code == "das2.beneficiaire" && r.PartyName == request.Name && r.Status == ThresholdStatusDto.Crossed
            && r.Soon && r.ToDate >= 2_500m && r.Basis == ThresholdBasisDto.InclTax && r.Source.Contains("240", StringComparison.Ordinal));
        view.Rows[0].Status.Should().Be(ThresholdStatusDto.Crossed, "le plus urgent d'abord");
    }

    public void Dispose() => _harness.Dispose();
}
