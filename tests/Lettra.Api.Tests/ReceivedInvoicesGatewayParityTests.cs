using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Factures fournisseurs reçues par la plateforme agréée simulée et annuaire, par la même interface, en HTTP et en
/// direct : relevé sans doublon, détail et fichier d'origine, réponses de l'acheteur, mêmes refus.
/// </summary>
public sealed class ReceivedInvoicesGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Relever_lire_puis_repondre_a_une_facture_recue(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IReceivedInvoicesGateway reception = services.GetRequiredService<IReceivedInvoicesGateway>();
        SaveCompanyProfileRequest profile = GatewayHarness.CompanyProfile();
        profile.Size = CompanySizeDto.SmallOrMedium;

        OperationResult<FetchReceivedInvoicesResponse> withoutSiren = await reception.FetchAsync(default);
        OperationResult<CompanyProfileResponse> saved = await services.GetRequiredService<IBillingSettingsGateway>().SaveCompanyProfileAsync(profile, null, default);
        OperationResult<FetchReceivedInvoicesResponse> first = await reception.FetchAsync(default);
        OperationResult<FetchReceivedInvoicesResponse> second = await reception.FetchAsync(default);
        ReceivedInvoicesResponse list = (await reception.GetAsync(default)).Value!;
        ReceivedInvoiceSummary stationery = list.Invoices.Single(i => i.Number == "PM-2026-0412");
        ReceivedInvoiceResponse detail = (await reception.GetInvoiceAsync(stationery.Id, default)).Value!;
        OperationResult<ReceiptDocument> file = await reception.DownloadAsync(stationery.Id, default);
        OperationResult<ReceivedInvoiceResponse> approved = await reception.RespondAsync(
            stationery.Id, new RespondToReceivedInvoiceRequest { Status = PlatformStatusDto.Approved }, default);
        OperationResult<ReceivedInvoiceResponse> withoutReason = await reception.RespondAsync(
            stationery.Id, new RespondToReceivedInvoiceRequest { Status = PlatformStatusDto.Disputed }, default);
        OperationResult<ReceivedInvoiceResponse> unknown = await reception.GetInvoiceAsync(Guid.NewGuid(), default);

        withoutSiren.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Reception.CompanySirenMissing);
        saved.Value!.Size.Should().Be(CompanySizeDto.SmallOrMedium);
        saved.Value.EInvoicingIssuingFrom.Should().Be(new DateOnly(2027, 9, 1));
        saved.Value.EInvoicingReceivingFrom.Should().Be(new DateOnly(2026, 9, 1));
        first.Value.Should().Be(new FetchReceivedInvoicesResponse(4, 0));
        second.Value.Should().Be(new FetchReceivedInvoicesResponse(0, 0));
        list.PlatformConfigured.Should().BeTrue();
        list.Directory!.ElectronicAddress.Should().Be("0225:552100554");
        list.IssuingFrom.Should().Be(new DateOnly(2027, 9, 1));
        list.Invoices.Should().HaveCount(4);
        detail.Lines.Should().HaveCount(2);
        detail.BusinessProcess.Should().Be("B1");
        file.Value!.Content.Should().NotBeEmpty();
        file.Value.FileName.Should().Be(detail.FileName);
        approved.Value!.Summary.Status.Should().Be(PlatformStatusDto.Approved);
        approved.Value.AvailableResponses.Should().Contain(PlatformStatusDto.PaymentSent);
        withoutReason.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Reception.ReasonRequired);
        unknown.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Reception.NotFound);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task L_annuaire_repond_de_meme(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IReceivedInvoicesGateway reception = services.GetRequiredService<IReceivedInvoicesGateway>();

        OperationResult<DirectoryLookupResponse> found = await reception.LookupDirectoryAsync("732829320", default);
        OperationResult<DirectoryLookupResponse> invalid = await reception.LookupDirectoryAsync("12345", default);

        found.Value!.Consulted.Should().BeTrue();
        found.Value.Entry!.ElectronicAddress.Should().Be("0225:732829320");
        invalid.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Reception.SirenInvalid);
    }

    public void Dispose() => _harness.Dispose();
}
