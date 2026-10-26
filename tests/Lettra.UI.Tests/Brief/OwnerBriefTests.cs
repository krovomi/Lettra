using System.Globalization;
using Bunit;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Brief;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Brief;
using Lettra.UI.Tests.Banking;
using Lettra.UI.Tests.Design;
using Lettra.UI.Tests.Security;
using Lettra.UI.Tests.Tax;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Brief;

/// <summary>« En bref », l'écran du dirigeant : chiffres du mois tirés des écrans existants, et ce qui mérite un coup d'œil.</summary>
[Collection("culture")]
public sealed class OwnerBriefTests : BunitContext
{
    private static readonly DateOnly Today = new(2026, 10, 26);
    private static readonly Guid Martin = Guid.CreateVersion7();

    private readonly FakeBankingGateway _banking = new();
    private readonly FakeTaxGateway _tax = new();
    private readonly FixedDashboard _dashboard = new();

    public OwnerBriefTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IBankingGateway>(_banking);
        Services.AddSingleton<ITaxGateway>(_tax);
        Services.AddSingleton<IBillingDashboardGateway>(_dashboard);
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private static PaymentSummary Paid(int month, int day, decimal amount, string customer = "Boulangerie Martin", PaymentStatusDto status = PaymentStatusDto.Posted) =>
        new(Guid.CreateVersion7(), "ENC-00001", status, Martin, customer, new DateOnly(2026, month, day), amount, PaymentMethodDto.Transfer, null, ["FAC-000012"], null);

    private static BankLineResponse Line(int month, int day, decimal amount, BankLineStatusDto status = BankLineStatusDto.Pending) =>
        new(Guid.CreateVersion7(), 1, new DateOnly(2026, month, day), amount, "VIR", null, null, null, status, null, null, null);

    private void AddPayment(PaymentSummary p)
    {
        OpenInvoiceResponse invoice = FakeBankingGateway.Invoice(p.InvoiceNumbers[0], p.CustomerId, p.CustomerName, p.Date, p.Amount);
        _banking.Payments.Add(FakeBankingGateway.Payment(p.Number, p.CustomerId, p.CustomerName, invoice) with { Date = p.Date, Amount = p.Amount, Status = p.Status });
    }

    /// <summary>Le texte d'un élément, espaces insécables (milliers, devise) ramenés à des espaces simples.</summary>
    private static string Text(IRenderedComponent<OwnerBriefPage> page, string selector) =>
        page.Find(selector).TextContent.Replace('\u202F', ' ').Replace('\u00A0', ' ');

    private IRenderedComponent<OwnerBriefPage> Open(ReceivablesSummaryResponse? receivables = null)
    {
        Services.AddSingleton<IReceivablesGateway>(new FakeReceivablesGateway(receivables));
        IRenderedComponent<OwnerBriefPage> page = Render<OwnerBriefPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    [Fact]
    public void Les_encaissements_du_mois_comptent_sans_les_contre_passations()
    {
        PaymentSummary[] payments = [Paid(10, 2, 1200m), Paid(10, 20, 300m), Paid(10, 21, 900m, status: PaymentStatusDto.Reversed), Paid(9, 28, 5000m)];

        OwnerBrief.Collected(payments, OwnerBrief.MonthStart(Today), Today).Should().Be(1500m);
        OwnerBrief.PaymentsFrom(Today).Should().Be(new DateOnly(2026, 9, 26), "les 30 derniers jours commencent avant le 1er du mois");
        OwnerBrief.PaymentsFrom(new DateOnly(2026, 10, 31)).Should().Be(new DateOnly(2026, 10, 1));
        OwnerBrief.BigPayments(payments, Today, 1000m).Select(p => p.Amount).Should().Equal(1200m, 5000m);
        OwnerBrief.BigPayments(payments, Today, 5000m).Should().ContainSingle();
    }

    [Fact]
    public void Les_mouvements_bancaires_du_mois_separent_entrees_et_sorties()
    {
        BankFlows flows = OwnerBrief.Flows([Line(10, 3, 1200m), Line(10, 5, -950m, BankLineStatusDto.Ignored), Line(10, 9, -84.60m), Line(9, 30, 9999m)],
            new DateOnly(2026, 10, 1), Today);

        flows.Should().Be(new BankFlows(1200m, 1034.60m));
        flows.Net.Should().Be(165.40m);
    }

    [Fact]
    public void Le_dirigeant_voit_les_chiffres_du_mois_d_un_coup_d_oeil()
    {
        AddPayment(Paid(10, 2, 1200m));
        AddPayment(Paid(10, 20, 300m));
        _banking.Lines.AddRange([Line(10, 3, 1200m), Line(10, 9, -84.60m)]);

        IRenderedComponent<OwnerBriefPage> page = Open(new ReceivablesSummaryResponse(Today, 1, 480m,
            [new OverdueInvoiceResponse(Guid.CreateVersion7(), "FAC-000007", "Dupont SARL", new DateOnly(2026, 9, 30), 26, 480m)], 0, 0, 0, 0));

        Text(page, "#brief-today").Should().Contain("octobre 2026");
        Text(page, "#brief-revenue .lt-kpi-value").Should().Contain("4 000");
        Text(page, "#brief-revenue .lt-trend").Should().Contain("+100");
        Text(page, "#brief-collected .lt-kpi-value").Should().Contain("1 500");
        Text(page, "#brief-vat .lt-kpi-head").Should().Contain("TVA à payer");
        Text(page, "#brief-vat .lt-kpi-value").Should().Contain("200");
        Text(page, "#brief-vat .lt-kpi-note").Should().Contain("15/11/2026");
        _tax.VatDates.Should().Equal(Today);
        Text(page, "#brief-bank .lt-kpi-value").Should().StartWith("+").And.Contain("115,40");
        Text(page, "#brief-bank .lt-kpi-note").Should().Contain("1 200,00").And.Contain("84,60");
        page.Find("#brief-overdue").ClassList.Should().Contain("is-alert");
        _banking.PaymentQueries.Single().From.Should().Be(new DateOnly(2026, 9, 26));
    }

    [Fact]
    public void A_surveiller_liste_les_gros_encaissements_au_seuil_choisi_et_les_retards()
    {
        AddPayment(Paid(10, 2, 1200m, "Hôtel du Lac"));
        AddPayment(Paid(10, 20, 300m));
        IRenderedComponent<OwnerBriefPage> page = Open(new ReceivablesSummaryResponse(Today, 1, 480m,
            [new OverdueInvoiceResponse(Guid.CreateVersion7(), "FAC-000007", "Dupont SARL", new DateOnly(2026, 9, 30), 26, 480m)], 0, 0, 0, 0));

        string[] Items() => [.. page.FindAll("#brief-watch-list li .lt-list-title").Select(t => t.TextContent.Trim())];
        Items().Should().Equal("Hôtel du Lac a payé", "Dupont SARL est en retard");
        page.Find("#brief-threshold").Change("500");
        Items().Should().HaveCount(2);
        page.Find("#brief-threshold").Change("10000");
        Items().Should().Equal("Dupont SARL est en retard");
    }

    [Fact]
    public void Sans_rien_a_signaler_l_ecran_le_dit()
    {
        IRenderedComponent<OwnerBriefPage> page = Open(new ReceivablesSummaryResponse(Today, 0, 0m, [], 0, 0, 0, 0));

        Text(page, "#brief-watch-none").Should().Contain("Rien à signaler");
        page.FindAll("#brief-bank").Should().BeEmpty("aucun relevé importé");
        page.Find("#brief-overdue").ClassList.Should().NotContain("is-alert");
    }

    [Fact]
    public void Une_lecture_refusee_masque_sa_carte_et_seule_une_panne_s_affiche()
    {
        _tax.VatError = ErrorCodes.Security.Forbidden;
        _dashboard.Error = ErrorCodes.Api.Unreachable;

        IRenderedComponent<OwnerBriefPage> page = Open();

        page.FindAll("#brief-vat").Should().BeEmpty();
        page.FindAll("#brief-revenue").Should().BeEmpty();
        page.FindAll("#brief-overdue").Should().BeEmpty();
        page.Find("#brief-collected").Should().NotBeNull();
        this.Toasts().Should().ContainSingle("seule la panne réseau est signalée");
    }

    /// <summary>Accueil figé : septembre à 2 000 €, octobre à 4 000 €.</summary>
    private sealed class FixedDashboard : IBillingDashboardGateway
    {
        public string? Error { get; set; }

        public Task<OperationResult<BillingDashboardResponse>> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Error is { } code
                ? OperationResult<BillingDashboardResponse>.Failure(code)
                : OperationResult<BillingDashboardResponse>.Success(new BillingDashboardResponse(
                    [.. Enumerable.Range(0, BillingDashboardResponse.MonthCount).Select(i => new MonthRevenueDto(new DateOnly(2026, 5 + i, 1), i == 4 ? 2000m : i == 5 ? 4000m : 0m))],
                    3, 0, 0m)));
    }
}
