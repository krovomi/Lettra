using System.Net;
using System.Text;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Tax;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Déclarations par la même interface, en HTTP et en direct : brouillon de CA3, DES et son fichier, calendrier fiscal,
/// réglages versionnés et dépôts marqués, avec les mêmes refus.
/// </summary>
public sealed class TaxGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    private static DateOnly Today => LegalCalendar.DateOf(DateTimeOffset.UtcNow);

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Le_brouillon_de_ca3_suit_le_regime_de_la_fiche_entreprise(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        ITaxGateway tax = services.GetRequiredService<ITaxGateway>();
        await GatewayHarness.ValidatedInvoiceAsync(services);

        await GatewayHarness.SaveCompanyProfileAsync(services, VatDeclarationRegimeDto.RealNormal);
        OperationResult<VatReturnResponse> draft = await tax.GetVatReturnAsync(Today, default);
        await GatewayHarness.SaveCompanyProfileAsync(services, regime: null);
        OperationResult<VatReturnResponse> missing = await tax.GetVatReturnAsync(Today, default);

        draft.IsSuccess.Should().BeTrue(GatewayHarness.Codes(draft));
        draft.Value!.Form.Should().Be(VatReturnFormDto.Ca3);
        draft.Value.From.Should().Be(new DateOnly(Today.Year, Today.Month, 1));
        missing.Errors.Select(e => e.Code).Should().Contain(ErrorCodes.Tax.RegimeMissing);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task La_des_et_son_fichier_puis_un_mois_impossible(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        ITaxGateway tax = services.GetRequiredService<ITaxGateway>();
        await GatewayHarness.ValidatedInvoiceAsync(services);

        OperationResult<DesResponse> des = await tax.GetDesAsync(Today.Year, Today.Month, default);
        OperationResult<DesDocument> file = await tax.ExportDesAsync(Today.Year, Today.Month, default);
        OperationResult<DesResponse> badMonth = await tax.GetDesAsync(Today.Year, 13, default);
        OperationResult<DesDocument> badFile = await tax.ExportDesAsync(Today.Year, 13, default);

        des.Value!.Lines.Should().BeEmpty("le client de test est en France");
        file.IsSuccess.Should().BeTrue(GatewayHarness.Codes(file));
        file.Value!.FileName.Should().Be($"DES-{Today.Year:D4}-{Today.Month:D2}.csv");
        Encoding.UTF8.GetString(file.Value.Content).Should().Contain("ligne;numero_tva_client;montant_eur;periode");
        badMonth.Errors.Select(e => e.Code).Should().ContainSingle().Which.Should().Be(ErrorCodes.Tax.PeriodInvalid);
        badFile.Errors.Select(e => e.Code).Should().ContainSingle().Which.Should().Be(ErrorCodes.Tax.PeriodInvalid);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Le_tableau_des_delais_de_paiement_classe_une_facture_due_selon_la_date_de_cloture(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        ITaxGateway tax = services.GetRequiredService<ITaxGateway>();
        InvoiceResponse invoice = await GatewayHarness.ValidatedInvoiceAsync(services);

        OperationResult<PaymentDelaysResponse> today = await tax.GetPaymentDelaysAsync(Today, default);
        OperationResult<PaymentDelaysResponse> later = await tax.GetPaymentDelaysAsync(Today.AddDays(400), default);
        OperationResult<PaymentDelaysResponse> lastClosing = await tax.GetPaymentDelaysAsync(null, default);

        today.IsSuccess.Should().BeTrue(GatewayHarness.Codes(today));
        PaymentDelayInvoiceResponse due = today.Value!.Invoices.Should().ContainSingle(i => i.InvoiceId == invoice.Id).Subject;
        due.OutstandingInclTax.Should().Be(invoice.Totals.TotalInclTax);
        due.DaysLate.Should().BeLessThanOrEqualTo(0);
        today.Value.TurnoverInclTax.Should().BeGreaterThanOrEqualTo(invoice.Totals.TotalInclTax);
        today.Value.TermBreaches.Should().NotContain(b => b.InvoiceId == invoice.Id);
        later.Value!.Invoices.Should().ContainSingle(i => i.InvoiceId == invoice.Id).Which.Bracket.Should().Be(4);
        later.Value.Brackets[4].Count.Should().BeGreaterThanOrEqualTo(1);
        lastClosing.Value!.ClosingDate.Should().BeBefore(Today);
        lastClosing.Value.Invoices.Should().NotContain(i => i.InvoiceId == invoice.Id);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Une_echeance_au_dela_du_plafond_legal_est_signalee(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        ITaxGateway tax = services.GetRequiredService<ITaxGateway>();
        IInvoicesGateway invoices = services.GetRequiredService<IInvoicesGateway>();
        Guid customer = (await services.GetRequiredService<ICustomersGateway>().CreateAsync(CustomerApiTests.Martin(), default)).Value!.Id;
        await GatewayHarness.SaveCompanyProfileAsync(services);
        InvoiceResponse draft = (await invoices.CreateAsync(
            new SaveInvoiceRequest { CustomerId = customer, DueDate = Today.AddDays(150), Lines = [GatewayHarness.Conseil()] }, default)).Value!;
        InvoiceResponse invoice = (await invoices.ValidateAsync(draft.Id, draft.Version, default)).Value!;

        OperationResult<PaymentDelaysResponse> report = await tax.GetPaymentDelaysAsync(Today, default);

        PaymentTermBreachResponse breach = report.Value!.TermBreaches.Should().ContainSingle(b => b.InvoiceId == invoice.Id).Subject;
        breach.DueDate.Should().Be(Today.AddDays(150));
        breach.DaysOver.Should().BePositive();
        breach.LegalCeiling.Should().BeBefore(breach.DueDate);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Les_reglages_se_changent_une_fois_par_version_et_refusent_un_jour_impossible(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        ITaxGateway tax = services.GetRequiredService<ITaxGateway>();

        FiscalSettingsResponse settings = (await tax.GetSettingsAsync(default)).Value!;
        OperationResult<FiscalSettingsResponse> saved = await tax.UpdateSettingsAsync(
            new UpdateFiscalSettingsRequest(12, 31, VatPeriodicityDto.Monthly, 19, true, false, settings.Version), default);
        OperationResult<FiscalSettingsResponse> stale = await tax.UpdateSettingsAsync(
            new UpdateFiscalSettingsRequest(12, 31, VatPeriodicityDto.Monthly, 19, true, false, settings.Version), default);
        OperationResult<FiscalSettingsResponse> invalid = await tax.UpdateSettingsAsync(
            new UpdateFiscalSettingsRequest(2, 30, VatPeriodicityDto.Monthly, 30, true, false, saved.Value!.Version), default);

        saved.Value.Version.Should().Be(settings.Version + 1);
        saved.Value.VatDeadlineDay.Should().Be(19);
        saved.Value.TrackedFrom.Should().NotBeNull();
        (await tax.GetSettingsAsync(default)).Value!.Version.Should().Be(saved.Value.Version);

        // Réglages fiscaux : conflit 409 sans fiche jointe, donc un refus simple (pas un « stale ») sur les deux transports.
        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        stale.Current.Should().BeNull();
        invalid.Errors.Select(e => e.Code).Should().BeEquivalentTo([ErrorCodes.Tax.FiscalYearEndInvalid, ErrorCodes.Tax.DeadlineDayInvalid]);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Le_calendrier_les_echeances_proches_et_un_depot_marque(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        ITaxGateway tax = services.GetRequiredService<ITaxGateway>();
        await GatewayHarness.ValidatedInvoiceAsync(services);
        await GatewayHarness.SaveCompanyProfileAsync(services, VatDeclarationRegimeDto.RealNormal);
        DateOnly from = new(Today.Year, 1, 1), to = new(Today.Year, 12, 31);

        OperationResult<FiscalCalendarResponse> calendar = await tax.GetCalendarAsync(from, to, default);
        OperationResult<IReadOnlyList<FiscalDeadlineResponse>> upcoming = await tax.GetUpcomingAsync(400, default);
        calendar.IsSuccess.Should().BeTrue(GatewayHarness.Codes(calendar));
        FiscalDeadlineResponse target = calendar.Value!.Deadlines.First(d => d.Obligation == FiscalObligationDto.VatReturnCa3);
        OperationResult<FiscalDeadlineResponse> marked = await tax.MarkFilingAsync(new MarkFiscalFilingRequest(target.Obligation, target.PeriodStart, true), default);
        OperationResult<FiscalDeadlineResponse> unknown = await tax.MarkFilingAsync(
            new MarkFiscalFilingRequest(target.Obligation, target.PeriodStart.AddDays(3), true), default);

        upcoming.IsSuccess.Should().BeTrue(GatewayHarness.Codes(upcoming));
        marked.Value!.State.Should().Be(FiscalDeadlineStateDto.Filed);
        marked.Value.FiledAt.Should().NotBeNull();
        unknown.Errors.Should().ContainSingle().Which.Should().Be(new ErrorDetail(ErrorCodes.Tax.DeadlineUnknown, nameof(MarkFiscalFilingRequest.PeriodStart)));
        (await tax.GetCalendarAsync(to, from, default)).Errors.Select(e => e.Code).Should().ContainSingle().Which.Should().Be(ErrorCodes.Tax.PeriodInvalid);
    }

    [Fact]
    public async Task Serveur_injoignable_chaque_appel_rend_Api_Unreachable()
    {
        await using ServiceProvider services = GatewayHarness.Unreachable();
        ITaxGateway tax = services.GetRequiredService<ITaxGateway>();

        List<IReadOnlyList<ErrorDetail>> failures =
        [
            (await tax.GetVatReturnAsync(Today, default)).Errors,
            (await tax.GetDesAsync(2026, 1, default)).Errors,
            (await tax.ExportDesAsync(2026, 1, default)).Errors,
            (await tax.GetCalendarAsync(Today, Today, default)).Errors,
            (await tax.GetUpcomingAsync(7, default)).Errors,
            (await tax.MarkFilingAsync(new MarkFiscalFilingRequest(FiscalObligationDto.Cfe, Today, true), default)).Errors,
            (await tax.GetSettingsAsync(default)).Errors,
            (await tax.UpdateSettingsAsync(new UpdateFiscalSettingsRequest(12, 31, VatPeriodicityDto.Monthly, 19, true, false, 0), default)).Errors,
        ];

        failures.Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable));
    }

    [Fact]
    public async Task Le_fichier_des_sans_nom_ou_sur_une_page_d_erreur_illisible()
    {
        await using ServiceProvider unnamed = GatewayHarness.Answering(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("ligne;numero_tva_client"u8.ToArray()),
        });
        await using ServiceProvider proxy = GatewayHarness.Answering(_ => GatewayHarness.HtmlError(HttpStatusCode.BadGateway));

        OperationResult<DesDocument> file = await unnamed.GetRequiredService<ITaxGateway>().ExportDesAsync(2026, 1, default);
        OperationResult<DesDocument> failure = await proxy.GetRequiredService<ITaxGateway>().ExportDesAsync(2026, 1, default);

        file.Value!.FileName.Should().Be("DES.csv", "sans Content-Disposition, la gateway donne un nom par défaut");
        failure.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unexpected);
    }

    public void Dispose() => _harness.Dispose();
}
