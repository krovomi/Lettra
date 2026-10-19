using System.Globalization;
using Bunit;
using Lettra.Contracts.Accounting;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Accounting;
using Lettra.UI.Shared.Pages.Banking;
using Lettra.UI.Tests.Consultation;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Banking;

/// <summary>Encaissements et rapprochement bancaire (vague 6) : saisie, contre-passation, propositions validées par l'utilisateur.</summary>
[Collection("culture")]
public sealed class BankingPagesTests : BunitContext
{
    private static readonly Guid Dupont = Guid.CreateVersion7();
    private static readonly Guid Martin = Guid.CreateVersion7();

    private readonly FakeBankingGateway _banking = new();
    private readonly FakeAccountingGateway _accounting = new();

    public BankingPagesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IBankingGateway>(_banking);
        Services.AddSingleton<IAccountingGateway>(_accounting);
        Services.AddSingleton<ICustomersGateway>(new FakeCustomersGateway());
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<TPage> Open<TPage>(string route, Action<ComponentParameterCollectionBuilder<TPage>>? parameters = null)
        where TPage : IComponent
    {
        Navigation.NavigateTo(route);
        IRenderedComponent<TPage> page = Render(parameters ?? (_ => { }));
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    private BankLineResponse Line(string label, decimal amount, OpenInvoiceResponse? invoice = null, MatchRuleDto rule = MatchRuleDto.InvoiceNumber)
    {
        BankLineResponse line = new(Guid.CreateVersion7(), 1, new DateOnly(2026, 10, 12), amount, label, null, null, null, BankLineStatusDto.Pending, null, null,
            invoice is null
                ? null
                : new MatchProposalResponse(rule, invoice.CustomerId, invoice.CustomerName, [new ProposedAllocationResponse(invoice.InvoiceId, invoice.Number, amount)], 0m, invoice.Number));
        _banking.Lines.Add(line);
        return line;
    }

    [Fact]
    public void Une_proposition_montre_sa_raison_et_se_valide_d_un_clic_avec_un_message()
    {
        OpenInvoiceResponse invoice = FakeBankingGateway.Invoice("F2026-0007", Dupont, "Dupont SARL", new DateOnly(2026, 10, 1), 1200m);
        BankLineResponse line = Line("VIR DUPONT SARL F2026-0007", 1200m, invoice);
        Line("FRAIS TENUE DE COMPTE", -12.50m);

        IRenderedComponent<ReconciliationPage> page = Open<ReconciliationPage>("bank");

        page.Find("#bank-pending").TextContent.Should().Be("2");
        page.Find("#bank-proposals").TextContent.Should().Be("1");
        page.Markup.Should().Contain("Le numéro de facture figure dans le libellé").And.Contain("F2026-0007");
        page.FindAll(".lt-banking-line-actions button").Should().Contain(b => b.TextContent.Trim() == "Écarter");

        page.FindAll(".lt-banking-line-actions button").First(b => b.TextContent.Trim() == "Valider").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("1 rapprochement(s) validé(s) et lettré(s)."));
        _banking.Confirmations.Should().ContainSingle().Which.Items.Should().ContainSingle()
            .Which.Should().Match<ConfirmMatchItem>(i => i.BankLineId == line.Id && i.CustomerId == Dupont && i.Allocations.Single().Amount == 1200m);
        page.Find("#bank-pending").TextContent.Should().Be("1");
    }

    [Fact]
    public void Les_propositions_se_valident_en_masse_et_une_operation_s_ecarte()
    {
        Line("VIR DUPONT F2026-0001", 100m, FakeBankingGateway.Invoice("F2026-0001", Dupont, "Dupont SARL", new DateOnly(2026, 9, 1), 100m));
        Line("VIR MARTIN", 80m, FakeBankingGateway.Invoice("F2026-0002", Martin, "Boulangerie Martin", new DateOnly(2026, 9, 5), 80m), MatchRuleDto.AmountAndName);
        BankLineResponse fees = Line("FRAIS", -3m);

        IRenderedComponent<ReconciliationPage> page = Open<ReconciliationPage>("bank");

        page.Find("#bank-confirm-selected").HasAttribute("disabled").Should().BeTrue("rien n'est appliqué sans l'accord de l'utilisateur");
        page.Find("#bank-select-all").Change(true);
        page.Find("#bank-confirm-selected").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("2 rapprochement(s) validé(s) et lettré(s)."));
        _banking.Confirmations.Single().Items.Should().HaveCount(2);

        page.FindAll(".lt-banking-line-actions button").Single(b => b.TextContent.Trim() == "Écarter").Click();
        page.WaitForAssertion(() => this.ToastText().Should().Be("Opération écartée."));
        _banking.Lines.Single(l => l.Id == fees.Id).Status.Should().Be(BankLineStatusDto.Ignored);
        page.Find("#bank-pending").TextContent.Should().Be("0");
    }

    [Fact]
    public void Un_encaissement_se_repartit_sur_les_factures_les_plus_anciennes_puis_s_enregistre()
    {
        OpenInvoiceResponse older = FakeBankingGateway.Invoice("F2026-0001", Dupont, "Dupont SARL", new DateOnly(2026, 9, 1), 100m);
        OpenInvoiceResponse newer = FakeBankingGateway.Invoice("F2026-0003", Dupont, "Dupont SARL", new DateOnly(2026, 10, 1), 80m);
        _banking.Open.AddRange([older, newer, FakeBankingGateway.Invoice("F2026-0002", Martin, "Boulangerie Martin", new DateOnly(2026, 9, 5), 50m)]);

        IRenderedComponent<PaymentEditor> page = Open<PaymentEditor>($"payments/new?customer={Dupont}");
        page.Find("#payment-amount").Change("150");

        page.FindAll("input.lt-banking-amount").Select(i => i.GetAttribute("value")).Should().Equal("100.00", "50.00");
        page.Find("#payment-save").HasAttribute("disabled").Should().BeFalse();
        page.Find("form.lt-banking-form").Submit();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Encaissement ENC-00001 enregistré et lettré A."));
        RecordPaymentRequest request = _banking.Recorded.Single();
        request.CustomerId.Should().Be(Dupont);
        request.Amount.Should().Be(150m);
        request.Allocations.Should().Equal(new PaymentAllocationRequest(older.InvoiceId, 100m), new PaymentAllocationRequest(newer.InvoiceId, 50m));
        Navigation.Uri.Should().Contain("payments/");
    }

    [Fact]
    public void Un_petit_ecart_sous_le_seuil_solde_la_facture()
    {
        OpenInvoiceResponse invoice = FakeBankingGateway.Invoice("F2026-0001", Dupont, "Dupont SARL", new DateOnly(2026, 9, 1), 100m);
        _banking.Open.Add(invoice);

        IRenderedComponent<PaymentEditor> page = Open<PaymentEditor>($"payments/new?customer={Dupont}");
        page.Find("#payment-amount").Change("99.60");

        page.Find("input.lt-banking-amount").GetAttribute("value").Should().Be("100.00");
        page.Markup.Should().Contain("Écart en moins passé en charge (658)");
    }

    [Fact]
    public void La_liste_montre_le_lettrage_et_la_fiche_contre_passe_avec_un_motif()
    {
        OpenInvoiceResponse invoice = FakeBankingGateway.Invoice("F2026-0001", Dupont, "Dupont SARL", new DateOnly(2026, 9, 1), 100m);
        PaymentResponse payment = FakeBankingGateway.Payment("ENC-00001", Dupont, "Dupont SARL", invoice);
        _banking.Payments.Add(payment);

        IRenderedComponent<PaymentsPage> list = Open<PaymentsPage>("payments");
        list.Find("#payments-table").TextContent.Should().Contain("ENC-00001").And.Contain("F2026-0001");
        list.Find(".lt-lettering").TextContent.Should().Be("A");

        IRenderedComponent<PaymentDetail> detail = Open<PaymentDetail>($"payments/{payment.Id}", p => p.Add(x => x.Id, payment.Id));
        detail.Find("#payment-lettering").TextContent.Should().Be("A");
        detail.Find("#payment-reverse").Click();
        detail.Find("#payment-reverse-confirm").HasAttribute("disabled").Should().BeTrue("le motif est obligatoire");
        detail.Find("#payment-reverse-reason").Input("Chèque impayé");
        detail.Find("#payment-reverse-confirm").Click();

        detail.WaitForAssertion(() => this.ToastText().Should().Be("Encaissement ENC-00001 contre-passé."));
        _banking.Reversals.Single().Request.Reason.Should().Be("Chèque impayé");
        detail.Find("#payment-status").TextContent.Should().Be("Contre-passé");
    }

    [Fact]
    public void Le_journal_montre_les_encaissements_avec_leur_lettrage_et_un_lien_vers_la_fiche()
    {
        Guid paymentId = Guid.CreateVersion7();
        _accounting.Entries.Add(new JournalEntryResponse(paymentId, InvoiceKindDto.Invoice, "ENC-00001", new DateOnly(2026, 10, 10), Dupont, "Dupont SARL", 100m,
            [new JournalLineResponse("512", 100m, 0m, null), new JournalLineResponse("411", 0m, 100m, null)], "BQ", "A"));
        _accounting.Entries.Add(new JournalEntryResponse(paymentId, InvoiceKindDto.Invoice, "ENC-00001", new DateOnly(2026, 10, 11), Dupont, "Dupont SARL", 100m,
            [new JournalLineResponse("411", 100m, 0m, null), new JournalLineResponse("512", 0m, 100m, null)], "BQ", null, IsReversal: true));

        IRenderedComponent<JournalPage> page = Open<JournalPage>("accounting/journal?from=2026-01-01&to=2026-12-31");

        page.FindAll(".lt-journal-head a").Select(a => a.GetAttribute("href")).Should().Contain($"payments/{paymentId}");
        page.FindAll(".lt-journal-head").Select(r => r.TextContent).Should().Contain(t => t.Contains("Encaissement (BQ)") && t.Contains('A'))
            .And.Contain(t => t.Contains("Contre-passation (BQ)"));
        page.Find("#journal-expand-all").Click();
        page.Markup.Should().Contain("Banque");
    }
}
