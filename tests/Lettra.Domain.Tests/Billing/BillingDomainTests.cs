using Lettra.Calculations;
using Lettra.Calculations.Compliance;
using Lettra.Domain.Billing;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.Billing;

public sealed class BillingDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly CustomerId Client = new(Guid.CreateVersion7());

    private static readonly PartySnapshot Martin = new(
        "Boulangerie Martin", "1 rue de la Paix", null, "75002", "Paris", "FR", "732829320", "73282932000074", "FR44732829320",
        null, null, null, IsBusiness: true);

    private static readonly PartySnapshot Berlin = Martin with { Name = "Bäckerei Schmidt GmbH", CountryCode = "DE", Siren = null, Siret = null, VatNumber = "DE123456789" };

    private static readonly SellerTerms Terms = new(
        new PartySnapshot("Conseil Lettra", "10 avenue de l'Opéra", null, "75001", "Paris", "FR", "552100554", "55210055400013", "FR96552100554",
            "SAS", 10_000m, "RCS Paris 552 100 554", IsBusiness: true),
        VatFranchise: false,
        VatOnDebits: false,
        PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours",
        LatePenaltyRate: 10m,
        EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé");

    private static LineDraft Service(decimal quantity, decimal price, string vat = "FR-20", string? exemption = null) =>
        new(null, null, "Conseil", null, CatalogItemKind.Service, "HUR", quantity, price, 0m, vat, exemption);

    private static LineDraft Good(decimal quantity, decimal price, string vat = "FR-5.5") =>
        new(null, null, "Livre", null, CatalogItemKind.Good, "C62", quantity, price, 0m, vat, null);

    private static Quote AcceptedQuote(params LineDraft[] lines)
    {
        Quote quote = Quote.Create(QuoteId.New(), new QuoteDraft(Client, Martin, null, VatRegime.Standard, "Mission", null, lines), User, Now).Value;
        quote.Validate("DEV-000001", Today, Martin, User, Now).IsSuccess.Should().BeTrue();
        quote.SetOutcome(true, User, Now).IsSuccess.Should().BeTrue();
        return quote;
    }

    private static Invoice Validated(Invoice invoice, string number)
    {
        invoice.Validate(number, Today, invoice.Customer, Terms, User, Now).IsSuccess.Should().BeTrue();
        invoice.Seal(new string('a', 64), Now);
        return invoice;
    }

    [Fact]
    public void Un_devis_valide_est_numerote_fige_et_valable_30_jours()
    {
        Quote quote = Quote.Create(QuoteId.New(), new QuoteDraft(Client, Martin, null, VatRegime.Standard, null, null, [Service(2, 100m)]), User, Now).Value;
        quote.Version.Should().Be(1);

        quote.Validate("DEV-000001", Today, Martin, User, Now).IsSuccess.Should().BeTrue();

        quote.Number.Should().Be("DEV-000001");
        quote.ValidUntil.Should().Be(Today.AddDays(30));
        quote.Version.Should().Be(2);
        quote.Update(new QuoteDraft(Client, Martin, null, VatRegime.Standard, null, null, [Service(1, 1m)]), User, Now)
            .Errors.Single().Code.Should().Be(BillingErrors.NotDraftCode);
        quote.IsExpired(Today.AddDays(31)).Should().BeTrue();
    }

    [Fact]
    public void La_version_avance_a_chaque_modification()
    {
        Quote quote = Quote.Create(QuoteId.New(), new QuoteDraft(Client, Martin, null, VatRegime.Standard, null, null, [Service(1, 10m)]), User, Now).Value;
        quote.Should().BeAssignableTo<IVersioned>();
        quote.Version.Should().Be(1);

        quote.Update(new QuoteDraft(Client, Martin, null, VatRegime.Standard, null, null, [Service(1, 12m)]), User, Now).IsSuccess.Should().BeTrue();

        quote.Version.Should().Be(2);
    }

    [Fact]
    public void Les_acomptes_ne_depassent_pas_le_ttc_du_devis()
    {
        Quote quote = AcceptedQuote(Service(10, 100m));

        quote.CanTakeDownPayment(1_200m).IsSuccess.Should().BeTrue();
        quote.RegisterDownPayment(1_000m, User, Now);
        quote.CanTakeDownPayment(200.01m).Errors.Single().Code.Should().Be(QuoteErrors.DownPaymentsExceedTotalCode);
    }

    [Fact]
    public void La_facture_finale_deduit_les_acomptes_valides_taux_par_taux()
    {
        Quote quote = AcceptedQuote(Service(4, 100m), Good(2, 19.99m));
        Invoice downPayment = Invoice.CreateDownPayment(InvoiceId.New(), quote, 0.3m, null, "Acompte 30 %", User, Now).Value;
        downPayment.Lines.Should().HaveCount(2).And.OnlyContain(l => l.Nature == RevenueNature.DownPayment);
        Validated(downPayment, "FAC-000001");
        quote.RegisterDownPayment(downPayment.TotalInclTax, User, Now);

        Invoice final = Invoice.CreateFinal(InvoiceId.New(), quote, [downPayment], d => $"Déduction acompte {d.Number}", User, Now).Value;

        final.TotalInclTax.Should().Be(quote.TotalInclTax - downPayment.TotalInclTax);
        final.Lines.Where(l => l.Nature == RevenueNature.DownPayment).Should().HaveCount(2).And.OnlyContain(l => l.Quantity < 0);
        final.QuoteNumber.Should().Be("DEV-000001");
        quote.AttachFinalInvoice(final.Id, User, Now).IsSuccess.Should().BeTrue();
        quote.Status.Should().Be(QuoteStatus.Invoiced);
        quote.AttachFinalInvoice(InvoiceId.New(), User, Now).Errors.Single().Code.Should().Be(QuoteErrors.AlreadyInvoicedCode);
    }

    [Fact]
    public void Un_avoir_ne_credite_pas_plus_que_le_solde_de_la_facture()
    {
        Invoice invoice = Validated(Invoice.Create(InvoiceId.New(), Client, Martin, Header(), [Service(3, 100m)], User, Now).Value, "FAC-000001");

        Invoice partial = Invoice.CreateCreditNote(InvoiceId.New(), invoice, new Dictionary<int, decimal> { [1] = 1m }, "Geste", User, Now).Value;
        partial.TotalInclTax.Should().Be(120m);
        partial.CreditedInvoiceNumber.Should().Be("FAC-000001");
        Validated(partial, "FAC-000002");
        invoice.RegisterCredit(partial.TotalInclTax, User, Now).IsSuccess.Should().BeTrue();

        Invoice.CreateCreditNote(InvoiceId.New(), invoice, null, null, User, Now)
            .Errors.Single().Code.Should().Be(InvoiceErrors.CreditExceedsBalanceCode);
        Invoice.CreateCreditNote(InvoiceId.New(), invoice, new Dictionary<int, decimal> { [1] = 2m }, null, User, Now).IsSuccess.Should().BeTrue();
        invoice.RegisterCredit(240.01m, User, Now).Errors.Single().Code.Should().Be(InvoiceErrors.CreditExceedsBalanceCode);
        Invoice.CreateCreditNote(InvoiceId.New(), partial, null, null, User, Now)
            .Errors.Single().Code.Should().Be(InvoiceErrors.CannotCreditCreditNoteCode);
    }

    [Fact]
    public void Une_piece_validee_porte_les_mentions_et_passe_le_controle()
    {
        Invoice invoice = Validated(Invoice.Create(InvoiceId.New(), Client, Martin, Header(), [Service(3, 100m)], User, Now).Value, "FAC-000001");

        invoice.DueDate.Should().Be(Today.AddDays(30));
        invoice.Seller!.Siren.Should().Be("552100554");
        invoice.LegalMentionList.Should().Contain(m => m.Contains("40 €", StringComparison.Ordinal) && m.Contains("recouvrement", StringComparison.Ordinal));
        invoice.LegalMentionList.Should().Contain(m => m.Contains("10", StringComparison.Ordinal) && m.Contains("Pénalités", StringComparison.Ordinal));
        ComplianceChecker.Check(invoice.ToComplianceDocument(null), Today).BlocksValidation.Should().BeFalse();
        invoice.Validate("FAC-000009", Today, Martin, Terms, User, Now).Errors.Single().Code.Should().Be(BillingErrors.NotDraftCode);
    }

    [Fact]
    public void L_autoliquidation_intracommunautaire_met_les_lignes_a_zero_avec_la_mention()
    {
        Invoice invoice = Invoice.Create(
            InvoiceId.New(), Client, Berlin, Header(VatRegime.ReverseChargeIntraEu), [Service(2, 500m)], User, Now).Value;
        Validated(invoice, "FAC-000001");

        invoice.Lines.Single().VatCategory.Should().Be(VatCategoryCodes.ReverseCharge);
        invoice.TotalVat.Should().Be(0m);
        invoice.VatSummaries.Single().ExemptionCode.Should().Be(VatTreatment.ReverseChargeExemption);
        invoice.LegalMentionList.Should().Contain(m => m.Contains("Autoliquidation", StringComparison.OrdinalIgnoreCase));
        ComplianceReport report = ComplianceChecker.Check(invoice.ToComplianceDocument(null), Today);
        report.Findings.Should().Contain(f => f.Code == MentionCodes.VatExemptionText && f.Severity == ComplianceSeverity.Ok);
        report.Findings.Should().Contain(f => f.Code == MentionCodes.CustomerVatNumber && f.Severity == ComplianceSeverity.Ok);
    }

    [Fact]
    public void Un_motif_d_exoneration_n_est_admis_qu_en_regime_normal()
    {
        Invoice exempt = Invoice.Create(InvoiceId.New(), Client, Martin, Header(), [Service(1, 80m, exemption: "VATEX-EU-132-1C")], User, Now).Value;
        exempt.Lines.Single().VatCategory.Should().Be(VatCategoryCodes.Exempt);
        exempt.TotalInclTax.Should().Be(80m);

        Result<Invoice> refused = Invoice.Create(
            InvoiceId.New(), Client, Martin, Header(VatRegime.Export), [Service(1, 80m, exemption: "VATEX-EU-132-1C")], User, Now);
        refused.Errors.Should().Contain(e => e.Code == BillingErrors.ExemptionOutsideStandardRegimeCode);
    }

    [Fact]
    public void La_saisie_accumule_les_erreurs_de_ligne()
    {
        Result<Invoice> refused = Invoice.Create(
            InvoiceId.New(), Client, Martin, Header(),
            [new LineDraft(null, null, " ", null, CatalogItemKind.Service, "XXX", 0m, -1m, 120m, "FR-99", null)], User, Now);

        refused.Errors.Select(e => e.Target).Should().Contain(["Lines[0].Designation", "Lines[0].Quantity", "Lines[0].UnitPriceExclTax"]);
    }

    private static InvoiceHeader Header(VatRegime regime = VatRegime.Standard) =>
        new(null, null, null, null, null, regime, "Mission", null);
}
