using System.Globalization;
using System.Text;
using Bunit;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Tax;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Components.Tax;
using Lettra.UI.Shared.Pages.Tax;
using Lettra.UI.Shared.Services;
using AngleSharp.Dom;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Tax;

/// <summary>Déclarations : brouillon de TVA avec ses pièces, DES et son fichier, calendrier fiscal, réglages.</summary>
[Collection("culture")]
public sealed class TaxPagesTests : BunitContext
{
    private readonly FakeTaxGateway _tax = new();
    private readonly FakeFileDownloader _files = new();

    public TaxPagesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<ITaxGateway>(_tax);
        Services.AddSingleton<IFileDownloader>(_files);
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<TPage> Open<TPage>(string route)
        where TPage : IComponent
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(route);
        IRenderedComponent<TPage> page = Render<TPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    [Fact]
    public void Le_brouillon_ouvre_par_defaut_le_mois_ecoule_et_chaque_case_montre_ses_pieces()
    {
        IRenderedComponent<VatReturnPage> page = Open<VatReturnPage>("tax/vat");

        _tax.VatDates.Should().Equal(new DateOnly(2026, 9, 30));
        page.Find("#vat-period").TextContent.Should().Be("Septembre 2026");
        page.Find("#vat-form").TextContent.Should().Contain("CA3");
        page.Find("#vat-due").TextContent.Should().Contain("15 octobre 2026");
        page.Find("#vat-box-Rate20").TextContent.Should().Contain("08").And.Contain("200");
        page.Find("#check-JournalMatch").ClassList.Should().Contain("is-ok");
        page.FindAll("#vat-box-Rate20-sources").Should().BeEmpty("les pièces sont repliées au départ");

        page.Find("#vat-box-Rate20-toggle").Click();

        page.Find("#vat-box-Rate20-toggle").GetAttribute("aria-expanded").Should().Be("true");
        page.Find("#vat-box-Rate20-sources a").GetAttribute("href").Should().Be($"invoices/{FakeTaxGateway.InvoiceId}");
        page.Find("#vat-box-Rate20-sources").TextContent.Should().Contain("FAC-000001").And.Contain("Atelier Durand");
    }

    [Fact]
    public void Marquer_la_declaration_deposee_passe_par_le_toaster()
    {
        IRenderedComponent<VatReturnPage> page = Open<VatReturnPage>("tax/vat?date=2026-09-15");

        page.Find("#vat-file").Click();

        page.WaitForAssertion(() => page.Find("#vat-filed"));
        _tax.Marks.Should().ContainSingle().Which.Should().Be(new MarkFiscalFilingRequest(FiscalObligationDto.VatReturnCa3, new DateOnly(2026, 9, 1), true));
        this.ToastText().Should().Be("Échéance marquée comme déposée.");
    }

    [Fact]
    public void Sans_regime_de_tva_l_ecran_dit_quoi_completer()
    {
        _tax.VatError = ErrorCodes.Tax.RegimeMissing;

        IRenderedComponent<VatReturnPage> page = Open<VatReturnPage>("tax/vat");

        page.Find("#tax-setup").TextContent.Should().Contain("régime");
        page.Find("#tax-setup-company").GetAttribute("href").Should().Be("settings/company");
    }

    [Fact]
    public void La_des_liste_les_clients_et_telecharge_son_fichier()
    {
        IRenderedComponent<DesPage> page = Open<DesPage>("tax/des?month=2026-09");

        page.Find("#des-line-1").TextContent.Should().Contain("DE123456789").And.Contain("Berlin Beratung GmbH");
        page.Find("#des-total").TextContent.Should().Be(TaxText.Euros(1401m));

        page.Find("#des-download").Click();

        page.WaitForAssertion(() => _files.Files.Should().ContainSingle());
        _files.Files[0].FileName.Should().Be("DES-2026-09.csv");
        _files.Files[0].MimeType.Should().Be("text/csv");
        this.ToastText().Should().NotBeEmpty();
    }

    [Fact]
    public void Le_calendrier_groupe_les_echeances_par_mois_et_signale_les_reglages_par_defaut()
    {
        IRenderedComponent<FiscalCalendarPage> page = Open<FiscalCalendarPage>("tax/calendar?year=2026");

        _tax.CalendarQueries.Should().Equal((new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
        page.Find("#calendar-defaults").TextContent.Should().NotBeEmpty();
        page.Find("#calendar-month-10").TextContent.Should().Be("Octobre 2026");
        IElement des = page.Find("#deadline-Des-2026-09-01");
        des.ClassList.Should().Contain("is-warning");
        des.QuerySelector("a")!.GetAttribute("href").Should().Be("tax/des?month=2026-09");

        page.Find("#deadline-Des-2026-09-01-mark").Click();

        page.WaitForAssertion(() => _tax.Marks.Should().ContainSingle().Which.Obligation.Should().Be(FiscalObligationDto.Des));
        this.ToastText().Should().Be("Échéance marquée comme déposée.");
    }

    [Fact]
    public void Les_reglages_s_enregistrent_avec_un_message_et_un_jour_hors_fourchette_est_refuse()
    {
        IRenderedComponent<FiscalSettingsPage> page = Open<FiscalSettingsPage>("tax/settings");

        page.Find("#fiscal-periodicity").Change(VatPeriodicityDto.Quarterly.ToString());
        page.Find("form").Submit();

        page.WaitForAssertion(() => _tax.Settings.Version.Should().Be(1));
        _tax.Settings.VatPeriodicity.Should().Be(VatPeriodicityDto.Quarterly);
        this.ToastText().Should().Be("Réglages fiscaux enregistrés.");

        page.Find("#fiscal-deadline-day").Change("30");
        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Markup.Should().Contain("entre le 15 et le 24"));
        _tax.Settings.Version.Should().Be(1);
    }
}

/// <summary>Déclarations fictives : réponses posées comme les rendrait le serveur.</summary>
internal sealed class FakeTaxGateway : ITaxGateway
{
    public static readonly Guid InvoiceId = Guid.Parse("0199a000-0000-7000-8000-0000000000aa");

    private static readonly TaxSourceResponse Source = new(InvoiceId, InvoiceKindDto.Invoice, "FAC-000001", new(2026, 9, 3), Guid.Empty, "Atelier Durand", 1000m, 200m);

    public List<DateOnly> VatDates { get; } = [];

    public List<(DateOnly From, DateOnly To)> CalendarQueries { get; } = [];

    public List<MarkFiscalFilingRequest> Marks { get; } = [];

    public string? VatError { get; set; }

    public FiscalSettingsResponse Settings { get; private set; } = new(12, 31, VatPeriodicityDto.Monthly, 15, true, true, null, 0);

    private DateTimeOffset? _vatFiled;

    public Task<OperationResult<VatReturnResponse>> GetVatReturnAsync(DateOnly date, CancellationToken cancellationToken)
    {
        VatDates.Add(date);
        if (VatError is { } code)
        {
            return Task.FromResult(OperationResult<VatReturnResponse>.Failure(code));
        }

        DateOnly from = new(date.Year, date.Month, 1);
        VatReturnResponse vat = new(
            VatReturnFormDto.Ca3, VatDeclarationRegimeDto.RealNormal, from, from.AddMonths(1).AddDays(-1), from.AddMonths(1).AddDays(14),
            [
                new("A1", "Taxable", "A1", null, 1000m, null, [Source]),
                new("Rate20", "Gross", "08", 20m, 1000m, 200m, [Source]),
                new("GrossTotal", "Totals", "16", null, null, 200m, []),
                new("NetDue", "Totals", "28", null, null, 200m, []),
            ],
            200m, 0m, 200m, 0m,
            [new("JournalMatch", TaxCheckSeverityDto.Ok, 200m, 200m), new("DeductibleMissing", TaxCheckSeverityDto.Warning)],
            [], false, _vatFiled);
        return Task.FromResult(OperationResult<VatReturnResponse>.Success(vat));
    }

    public Task<OperationResult<DesResponse>> GetDesAsync(int year, int month, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<DesResponse>.Success(Des(year, month)));

    public Task<OperationResult<DesDocument>> ExportDesAsync(int year, int month, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<DesDocument>.Success(new DesDocument($"DES-{year:D4}-{month:D2}.csv", Encoding.UTF8.GetBytes("ligne;numero_tva_client;montant_eur;periode\r\n"))));

    public Task<OperationResult<FiscalCalendarResponse>> GetCalendarAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        CalendarQueries.Add((from, to));
        FiscalDeadlineStateDto desState = Marks.Any(m => m.Obligation == FiscalObligationDto.Des && m.Filed) ? FiscalDeadlineStateDto.Filed : FiscalDeadlineStateDto.DueSoon;
        FiscalCalendarResponse calendar = new(from, to, new DateOnly(2026, 10, 9), VatDeclarationRegimeDto.RealNormal, false, Settings,
        [
            new(FiscalObligationDto.VatReturnCa3, new(2026, 9, 1), new(2026, 9, 30), new(2026, 10, 15), FiscalDeadlineStateDto.DueSoon, null, "VatDeadlineDay"),
            new(FiscalObligationDto.Des, new(2026, 9, 1), new(2026, 9, 30), new(2026, 10, 12), desState, null, "Des"),
            new(FiscalObligationDto.Cfe, new(2026, 1, 1), new(2026, 12, 31), new(2026, 12, 15), FiscalDeadlineStateDto.Upcoming, null, null),
        ]);
        return Task.FromResult(OperationResult<FiscalCalendarResponse>.Success(calendar));
    }

    public Task<OperationResult<IReadOnlyList<FiscalDeadlineResponse>>> GetUpcomingAsync(int days, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<IReadOnlyList<FiscalDeadlineResponse>>.Success([]));

    public Task<OperationResult<FiscalDeadlineResponse>> MarkFilingAsync(MarkFiscalFilingRequest request, CancellationToken cancellationToken)
    {
        Marks.Add(request);
        DateTimeOffset at = new(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);
        if (request.Obligation == FiscalObligationDto.VatReturnCa3)
        {
            _vatFiled = request.Filed ? at : null;
        }

        return Task.FromResult(OperationResult<FiscalDeadlineResponse>.Success(new FiscalDeadlineResponse(
            request.Obligation, request.PeriodStart, request.PeriodStart.AddMonths(1).AddDays(-1), request.PeriodStart.AddMonths(1).AddDays(14),
            request.Filed ? FiscalDeadlineStateDto.Filed : FiscalDeadlineStateDto.DueSoon, request.Filed ? at : null, null)));
    }

    public Task<OperationResult<FiscalSettingsResponse>> GetSettingsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<FiscalSettingsResponse>.Success(Settings));

    public Task<OperationResult<FiscalSettingsResponse>> UpdateSettingsAsync(UpdateFiscalSettingsRequest request, CancellationToken cancellationToken)
    {
        if (request.VatDeadlineDay is < 15 or > 24)
        {
            return Task.FromResult(OperationResult<FiscalSettingsResponse>.Failure([new ErrorDetail(ErrorCodes.Tax.DeadlineDayInvalid, "VatDeadlineDay")]));
        }

        Settings = new(request.FiscalYearEndMonth, request.FiscalYearEndDay, request.VatPeriodicity, request.VatDeadlineDay, request.CorporateIncomeTax, request.Cfe,
            new DateOnly(2026, 10, 9), Settings.Version + 1);
        return Task.FromResult(OperationResult<FiscalSettingsResponse>.Success(Settings));
    }

    private static DesResponse Des(int year, int month)
    {
        TaxSourceResponse source = new(InvoiceId, InvoiceKindDto.Invoice, "FAC-000101", new(year, month, 10), Guid.Empty, "Berlin Beratung GmbH", 1400.6m, 0m);
        return new DesResponse(year, month, new DateOnly(year, month, 1).AddMonths(1).AddDays(11),
            [new DesLineResponse(1, "DE123456789", "DE", Guid.Empty, "Berlin Beratung GmbH", 1401m, [source])],
            1401m, [new TaxCheckResponse("DesVatNumberMissing", TaxCheckSeverityDto.Ok, Count: 0)], null);
    }
}
