using Lettra.Domain.Billing;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.Billing;

public sealed class OrderBookCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);
    private static readonly CustomerId Martin = new(Guid.CreateVersion7());
    private static readonly CustomerId Dupont = new(Guid.CreateVersion7());

    private static int _number;

    private static QuoteFact Fact(
        CustomerId customer, QuoteStatus status, int issuedDaysAgo, decimal amount = 500m, int validDays = 30, int? answeredAfter = null)
    {
        DateOnly issue = Today.AddDays(-issuedDaysAgo);
        return new QuoteFact(
            QuoteId.New(), $"DEV-{Interlocked.Increment(ref _number):000000}", customer, customer == Martin ? "Martin" : "Dupont", status, issue,
            issue.AddDays(validDays), answeredAfter is { } days ? issue.AddDays(days) : null, amount, amount * 1.2m);
    }

    private static QuoteFact Accepted(CustomerId customer, int issuedDaysAgo = 60, decimal amount = 500m, int answeredAfter = 3) =>
        Fact(customer, QuoteStatus.Accepted, issuedDaysAgo, amount, answeredAfter: answeredAfter);

    private static QuoteFact Refused(CustomerId customer, int issuedDaysAgo = 60, decimal amount = 500m, int answeredAfter = 3) =>
        Fact(customer, QuoteStatus.Refused, issuedDaysAgo, amount, answeredAfter: answeredAfter);

    [Theory]
    [InlineData(999.99, QuoteAmountBand.Small)]
    [InlineData(1_000, QuoteAmountBand.Medium)]
    [InlineData(4_999.99, QuoteAmountBand.Medium)]
    [InlineData(5_000, QuoteAmountBand.Large)]
    [InlineData(20_000, QuoteAmountBand.VeryLarge)]
    public void Les_tranches_de_montant_suivent_les_seuils_HT(decimal amount, QuoteAmountBand band) =>
        OrderBookCalculator.AmountBandOf(amount).Should().Be(band);

    [Theory]
    [InlineData(0, QuoteAgeBand.FirstWeek)]
    [InlineData(7, QuoteAgeBand.FirstWeek)]
    [InlineData(8, QuoteAgeBand.SecondWeek)]
    [InlineData(15, QuoteAgeBand.SecondWeek)]
    [InlineData(16, QuoteAgeBand.SameMonth)]
    [InlineData(30, QuoteAgeBand.SameMonth)]
    [InlineData(31, QuoteAgeBand.Older)]
    public void Les_tranches_d_anciennete_suivent_les_seuils_en_jours(int days, QuoteAgeBand band) =>
        OrderBookCalculator.AgeBandOf(days).Should().Be(band);

    [Fact]
    public void Chaque_tranche_d_anciennete_commence_le_lendemain_de_la_precedente() =>
        OrderBookCalculator.AgeBands.Select(OrderBookCalculator.FirstDayOf).Should().Equal(0, 8, 16, 31);

    [Fact]
    public void Sans_historique_chaque_devis_compte_pour_une_chance_sur_deux()
    {
        QuoteFact open = Fact(Martin, QuoteStatus.Validated, 5, 1_000m);

        OrderBookEstimate book = OrderBookCalculator.Estimate([open], Today);

        book.HasEnoughHistory.Should().BeFalse();
        book.GlobalRate.Should().Be(0.5m);
        book.HistoryFrom.Should().Be(Today.AddMonths(-24));
        WeightedQuote weighted = book.Quotes.Should().ContainSingle().Subject;
        weighted.Probability.Should().Be(0.5m);
        (weighted.Customer.UsesGlobalRate, weighted.Amount.UsesGlobalRate, weighted.Age.UsesGlobalRate).Should().Be((true, true, true));
        (weighted.WeightedExclTax, weighted.WeightedInclTax).Should().Be((500m, 600m));
        (book.OpenExclTax, book.WeightedExclTax, book.WeightedInclTax).Should().Be((1_000m, 500m, 600m));
    }

    [Fact]
    public void La_probabilite_est_la_moyenne_des_taux_du_client_du_montant_et_de_l_anciennete()
    {
        // Martin : 3 acceptés sur 3 ; Dupont : 0 sur 3. Tous les devis conclus sont de la même tranche de montant et ont
        // eu leur réponse au bout de 3 jours.
        QuoteFact[] history =
        [
            Accepted(Martin), Accepted(Martin), Accepted(Martin),
            Refused(Dupont), Refused(Dupont), Refused(Dupont),
        ];
        QuoteFact open = Fact(Martin, QuoteStatus.Validated, 2, 500m);

        OrderBookEstimate book = OrderBookCalculator.Estimate([.. history, open], Today);

        book.HasEnoughHistory.Should().BeTrue();
        (book.Overall.Accepted, book.Overall.Closed, book.GlobalRate).Should().Be((3, 6, 0.5m));
        WeightedQuote weighted = book.Quotes.Should().ContainSingle().Subject;
        (weighted.AgeDays, weighted.AmountBand, weighted.AgeBand).Should().Be((2, QuoteAmountBand.Small, QuoteAgeBand.FirstWeek));
        (weighted.Customer.Rate, weighted.Customer.UsesGlobalRate).Should().Be((1m, false));
        (weighted.Amount.Rate, weighted.Amount.Observed.Closed).Should().Be((0.5m, 6));
        (weighted.Age.Rate, weighted.Age.Observed.Closed).Should().Be((0.5m, 6));
        weighted.Probability.Should().Be(0.6667m);
        weighted.WeightedExclTax.Should().Be(333.35m);
    }

    [Fact]
    public void Un_critere_sur_moins_de_trois_devis_prend_le_taux_global()
    {
        QuoteFact[] history = [Accepted(Martin), Refused(Martin), Accepted(Dupont), Refused(Dupont)];
        QuoteFact open = Fact(Martin, QuoteStatus.Validated, 2, 25_000m);

        WeightedQuote weighted = OrderBookCalculator.Estimate([.. history, open], Today).Quotes.Single();

        weighted.Customer.Should().Be(new CriterionRate(new AcceptanceCount(1, 2), 0.5m, UsesGlobalRate: true));
        weighted.Amount.Should().Be(new CriterionRate(AcceptanceCount.None, 0.5m, UsesGlobalRate: true));
    }

    [Fact]
    public void L_anciennete_ne_compte_que_les_devis_restes_sans_reponse_aussi_longtemps()
    {
        // Trois devis acceptés en 2 jours, trois refusés au bout de 20 jours : un devis en attente depuis 18 jours ne se
        // compare qu'aux refusés.
        QuoteFact[] history =
        [
            Accepted(Martin, answeredAfter: 2), Accepted(Martin, answeredAfter: 2), Accepted(Martin, answeredAfter: 2),
            Refused(Dupont, answeredAfter: 20), Refused(Dupont, answeredAfter: 20), Refused(Dupont, answeredAfter: 20),
        ];
        QuoteFact open = Fact(Dupont, QuoteStatus.Validated, 18, 500m, validDays: 45);

        OrderBookEstimate book = OrderBookCalculator.Estimate([.. history, open], Today);

        book.ByAge.Select(b => (b.Band, b.Count.Accepted, b.Count.Closed)).Should().Equal(
            (QuoteAgeBand.FirstWeek, 3, 6), (QuoteAgeBand.SecondWeek, 0, 3), (QuoteAgeBand.SameMonth, 0, 3), (QuoteAgeBand.Older, 0, 0));
        WeightedQuote weighted = book.Quotes.Single();
        weighted.AgeBand.Should().Be(QuoteAgeBand.SameMonth);
        (weighted.Age.Rate, weighted.Age.UsesGlobalRate).Should().Be((0m, false));
        weighted.Customer.Rate.Should().Be(0m);
        weighted.Probability.Should().Be(0.1667m);
    }

    [Fact]
    public void Un_devis_expire_sans_reponse_compte_comme_non_accepte_et_un_facture_comme_accepte()
    {
        QuoteFact expired = Fact(Martin, QuoteStatus.Validated, 50, validDays: 30);
        QuoteFact invoiced = Fact(Martin, QuoteStatus.Invoiced, 50, answeredAfter: 5);
        QuoteFact draft = Fact(Martin, QuoteStatus.Draft, 50);

        OrderBookEstimate book = OrderBookCalculator.Estimate([expired, invoiced, draft], Today);

        (book.Overall.Accepted, book.Overall.Closed).Should().Be((1, 2));
        book.Quotes.Should().BeEmpty();
        book.ByAge.Single(b => b.Band == QuoteAgeBand.SameMonth).Count.Should().Be(new AcceptanceCount(0, 1));
    }

    [Fact]
    public void Un_devis_repondu_sans_jour_connu_compte_comme_conclu_le_jour_de_son_envoi()
    {
        QuoteFact[] history = [Fact(Martin, QuoteStatus.Accepted, 40), Fact(Martin, QuoteStatus.Refused, 40), Fact(Martin, QuoteStatus.Accepted, 40)];

        OrderBookEstimate book = OrderBookCalculator.Estimate(history, Today);

        book.ByAge.Select(b => b.Count.Closed).Should().Equal(3, 0, 0, 0);
    }

    [Fact]
    public void Les_devis_de_plus_de_24_mois_ne_comptent_pas()
    {
        QuoteFact old = Accepted(Martin, issuedDaysAgo: 800);

        OrderBookCalculator.Estimate([old], Today).Overall.Should().Be(AcceptanceCount.None);
    }

    [Fact]
    public void Les_devis_en_attente_sont_classes_par_montant_pondere_puis_par_numero()
    {
        QuoteFact small = Fact(Martin, QuoteStatus.Validated, 1, 100m);
        QuoteFact big = Fact(Dupont, QuoteStatus.Validated, 1, 900m);
        QuoteFact same = small with { Id = QuoteId.New(), Number = "DEV-999999" };
        QuoteFact lastDay = Fact(Martin, QuoteStatus.Validated, 30, 100m, validDays: 30);

        OrderBookEstimate book = OrderBookCalculator.Estimate([same, small, big, lastDay], Today);

        book.Quotes.Select(q => q.Quote.Number).Should().Equal(big.Number, small.Number, lastDay.Number, "DEV-999999");
    }

    [Fact]
    public void Un_devis_en_attente_est_envoye_sans_reponse_et_encore_valable()
    {
        OrderBookCalculator.IsOpen(Fact(Martin, QuoteStatus.Validated, 30, validDays: 30), Today).Should().BeTrue();
        OrderBookCalculator.IsOpen(Fact(Martin, QuoteStatus.Validated, 31, validDays: 30), Today).Should().BeFalse();
        OrderBookCalculator.IsOpen(Fact(Martin, QuoteStatus.Accepted, 1), Today).Should().BeFalse();
        OrderBookCalculator.IsOpen(Fact(Martin, QuoteStatus.Validated, 1) with { ValidUntil = null }, Today).Should().BeFalse();
        FluentActions.Invoking(() => OrderBookCalculator.IsOpen(null!, Today)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => OrderBookCalculator.Estimate(null!, Today)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Un_taux_sans_devis_conclu_est_absent()
    {
        AcceptanceCount.None.Rate.Should().BeNull();
        new AcceptanceCount(1, 3).Rate.Should().Be(0.3333m);
    }
}
