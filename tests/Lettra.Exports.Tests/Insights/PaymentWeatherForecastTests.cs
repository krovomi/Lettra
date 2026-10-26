using Lettra.Contracts.Insights;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Insights;

namespace Lettra.Exports.Tests.Insights;

/// <summary>
/// Météo des paiements : la météo d'un client tirée de son historique, la date d'encaissement attendue de chaque facture
/// due, le meilleur jour d'envoi et la prévision des huit semaines.
/// </summary>
public sealed class PaymentWeatherForecastTests
{
    // Vendredi : la semaine en cours commence le lundi 12 octobre.
    private static readonly DateOnly Aujourdhui = new(2026, 10, 16);
    private static readonly Guid Dupont = Guid.CreateVersion7();
    private static readonly Guid Durand = Guid.CreateVersion7();

    /// <summary>Une facture réglée <paramref name="retard"/> jours après son échéance (négatif : en avance).</summary>
    private static PaymentHistoryInvoice Reglee(int retard, decimal montant = 1000m, Guid? client = null, DateOnly? emise = null, int delai = 30)
    {
        DateOnly emission = emise ?? Aujourdhui.AddDays(-120);
        DateOnly echeance = emission.AddDays(delai);
        return new PaymentHistoryInvoice(
            Guid.CreateVersion7(), client ?? Dupont, client == Durand ? "Marie Durand" : "Dupont SARL", "FAC-" + Guid.CreateVersion7().ToString("N")[..6],
            emission, echeance, montant, 0m, [new PaymentHistoryEntry(echeance.AddDays(retard), montant)]);
    }

    /// <summary>Une facture encore due, d'échéance <paramref name="echeance"/>.</summary>
    private static PaymentHistoryInvoice Due(DateOnly? echeance, decimal reste = 500m, Guid? client = null, string numero = "FAC-2026-0100") =>
        new(Guid.CreateVersion7(), client ?? Dupont, client == Durand ? "Marie Durand" : "Dupont SARL", numero,
            (echeance ?? Aujourdhui).AddDays(-30), echeance, reste, reste, []);

    private static CustomerPaymentWeatherResponse Bulletin(params PaymentHistoryInvoice[] factures) =>
        PaymentWeatherForecast.Compute(factures, Aujourdhui).Customers.Single();

    [Theory]
    [InlineData(-5, 2, PaymentWeatherDto.Sunny)]
    [InlineData(1, 2, PaymentWeatherDto.Sunny)]
    [InlineData(4, 8, PaymentWeatherDto.Cloudy)]
    [InlineData(10, 25, PaymentWeatherDto.Rainy)]
    [InlineData(30, 45, PaymentWeatherDto.Stormy)]
    public void La_meteo_suit_le_retard_moyen_pondere_par_les_montants(int premier, int second, PaymentWeatherDto attendue)
    {
        CustomerPaymentWeatherResponse bulletin = Bulletin(Reglee(premier), Reglee(second));

        bulletin.Weather.Should().Be(attendue);
        bulletin.Samples.Should().Be(2);
        bulletin.AverageDaysLate.Should().Be((int)Math.Round((premier + second) / 2m, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Une_grosse_facture_en_retard_pese_plus_qu_une_petite_a_l_heure()
    {
        CustomerPaymentWeatherResponse bulletin = Bulletin(Reglee(0, 100m), Reglee(0, 100m), Reglee(40, 9800m));

        bulletin.AverageDaysLate.Should().Be(39);
        bulletin.TypicalDaysLate.Should().Be(0, "le retard habituel est la médiane, qui ne voit pas les montants");
        bulletin.OnTimePercent.Should().Be(67);
        bulletin.Weather.Should().Be(PaymentWeatherDto.Stormy);
    }

    [Fact]
    public void Une_facture_due_depuis_plus_de_deux_mois_suffit_a_l_orage()
    {
        CustomerPaymentWeatherResponse bulletin = Bulletin(Due(Aujourdhui.AddDays(-61)));

        bulletin.Weather.Should().Be(PaymentWeatherDto.Stormy, "même sans autre historique");
        bulletin.MaxDaysOverdue.Should().Be(61);
        bulletin.OnTimePercent.Should().Be(0);
        Bulletin(Due(Aujourdhui.AddDays(-60))).Weather.Should().Be(PaymentWeatherDto.Unknown, "soixante jours pile ne suffisent pas");
    }

    [Fact]
    public void Une_facture_echue_compte_pour_son_retard_a_ce_jour()
    {
        CustomerPaymentWeatherResponse bulletin = Bulletin(Reglee(0), Due(Aujourdhui.AddDays(-20), 1000m));

        bulletin.Samples.Should().Be(2);
        bulletin.AverageDaysLate.Should().Be(10);
        bulletin.Weather.Should().Be(PaymentWeatherDto.Cloudy);
        bulletin.OnTimePercent.Should().Be(50);
    }

    [Fact]
    public void Sans_deux_factures_payees_ou_echues_il_n_y_a_pas_de_meteo_et_l_echeance_fait_foi()
    {
        DateOnly echeance = Aujourdhui.AddDays(10);
        CustomerPaymentWeatherResponse bulletin = Bulletin(Reglee(25), Due(echeance));

        bulletin.Weather.Should().Be(PaymentWeatherDto.Unknown);
        bulletin.Samples.Should().Be(1, "la facture pas encore échue ne dit rien du payeur");
        bulletin.Expected.Should().ContainSingle().Which.ExpectedDate.Should().Be(echeance, "sans météo, pas de décalage");
        bulletin.OutstandingInclTax.Should().Be(500m);
    }

    [Fact]
    public void Une_facture_due_est_attendue_a_son_echeance_decalee_du_retard_habituel_jamais_avant_demain()
    {
        CustomerPaymentWeatherResponse bulletin = Bulletin(
            Reglee(12), Reglee(15), Reglee(20),
            Due(Aujourdhui.AddDays(5), 300m, numero: "FAC-2026-0101"),
            Due(Aujourdhui.AddDays(-3), 200m, numero: "FAC-2026-0102"),
            Due(Aujourdhui.AddDays(-40), 100m, numero: "FAC-2026-0103"));

        bulletin.TypicalDaysLate.Should().Be(15);
        bulletin.Expected.Select(e => (e.Number, e.OutstandingInclTax, e.ExpectedDate)).Should().Equal(
            ("FAC-2026-0103", 100m, Aujourdhui.AddDays(1)),
            ("FAC-2026-0102", 200m, Aujourdhui.AddDays(12)),
            ("FAC-2026-0101", 300m, Aujourdhui.AddDays(20)));
    }

    [Fact]
    public void Un_client_en_avance_n_avance_pas_ses_encaissements_attendus()
    {
        DateOnly echeance = Aujourdhui.AddDays(14);
        CustomerPaymentWeatherResponse bulletin = Bulletin(Reglee(-10), Reglee(-8), Due(echeance));

        bulletin.TypicalDaysLate.Should().Be(-10);
        bulletin.Expected.Single().ExpectedDate.Should().Be(echeance);
    }

    [Fact]
    public void Le_meilleur_jour_d_envoi_est_celui_apres_lequel_le_client_paie_le_plus_vite()
    {
        DateOnly mardi = new(2026, 6, 2);
        DateOnly vendredi = new(2026, 6, 5);
        CustomerPaymentWeatherResponse bulletin = Bulletin(
            Reglee(-20, emise: mardi), Reglee(-20, emise: mardi.AddDays(7)),
            Reglee(5, emise: vendredi), Reglee(3, emise: vendredi.AddDays(7)));

        bulletin.BestSendDay.Should().Be(DayOfWeek.Tuesday);
        bulletin.BestSendDayGainDays.Should().Be(24);
    }

    [Fact]
    public void Sans_comparaison_possible_ou_sans_ecart_net_aucun_jour_n_est_propose()
    {
        DateOnly mardi = new(2026, 6, 2);
        Bulletin(Reglee(0, emise: mardi), Reglee(5, emise: mardi.AddDays(7))).BestSendDay.Should().BeNull("un seul jour d'émission");
        Bulletin(Reglee(0, emise: mardi), Reglee(0, emise: mardi.AddDays(7)), Reglee(1, emise: mardi.AddDays(3)))
            .BestSendDay.Should().BeNull("un jour de gagné ne vaut pas un conseil");
        Bulletin(Reglee(0, emise: mardi), Reglee(9, emise: mardi.AddDays(3)))
            .BestSendDay.Should().BeNull("une seule facture le mardi ne fait pas une habitude");
    }

    [Fact]
    public void Les_reglements_de_plus_d_un_an_et_les_pieces_sans_montant_du_sont_ignores()
    {
        PaymentHistoryInvoice ancienne = Reglee(80, emise: Aujourdhui.AddDays(-500));
        PaymentHistoryInvoice creditee = Reglee(80) with { AmountDue = 0m };
        PaymentHistoryInvoice sansDate = Due(null) with { IssueDate = null };

        PaymentWeatherResponse meteo = PaymentWeatherForecast.Compute([ancienne, creditee, sansDate], Aujourdhui);

        meteo.Customers.Should().BeEmpty();
        meteo.Forecast.Should().HaveCount(PaymentWeatherResponse.ForecastWeeks).And.OnlyContain(w => w.ExpectedInclTax == 0m);
    }

    [Fact]
    public void Un_paiement_en_plusieurs_fois_compte_a_la_date_du_dernier()
    {
        DateOnly echeance = Aujourdhui.AddDays(-90);
        PaymentHistoryInvoice enDeuxFois = new(
            Guid.CreateVersion7(), Dupont, "Dupont SARL", "FAC-2026-0050", echeance.AddDays(-30), echeance, 1000m, 0m,
            [new PaymentHistoryEntry(echeance.AddDays(-2), 500m), new PaymentHistoryEntry(echeance.AddDays(18), 500m)]);

        CustomerPaymentWeatherResponse bulletin = Bulletin(enDeuxFois, Reglee(18));

        bulletin.AverageDaysLate.Should().Be(18);
        bulletin.Weather.Should().Be(PaymentWeatherDto.Rainy);
    }

    [Fact]
    public void La_prevision_range_les_encaissements_par_semaine_et_les_clients_par_gravite()
    {
        PaymentWeatherResponse meteo = PaymentWeatherForecast.Compute(
            [
                Reglee(0), Reglee(1), Due(Aujourdhui.AddDays(3), 400m),
                Due(Aujourdhui.AddDays(-70), 250m, Durand, "FAC-2026-0200"),
                Due(Aujourdhui.AddDays(100), 900m, Durand, "FAC-2026-0201"),
            ],
            Aujourdhui);

        meteo.Today.Should().Be(Aujourdhui);
        meteo.Customers.Select(c => c.Weather).Should().Equal(PaymentWeatherDto.Stormy, PaymentWeatherDto.Sunny);
        meteo.Customers[0].CustomerName.Should().Be("Marie Durand");
        meteo.Forecast[0].Should().Be(new CollectionForecastWeekResponse(new DateOnly(2026, 10, 12), 0m, 0), "la facture à 70 jours de retard est douteuse");
        meteo.Doubtful.Should().Be(250m);
        meteo.Customers[0].Expected.Should().Contain(e => e.Number == "FAC-2026-0200" && e.Doubtful);
        meteo.Forecast[1].Should().Be(new CollectionForecastWeekResponse(new DateOnly(2026, 10, 19), 400m, 1));
        meteo.Forecast.Skip(2).Should().OnlyContain(w => w.ExpectedInclTax == 0m && w.Invoices == 0);
        meteo.Later.Should().Be(900m);
    }

    [Fact]
    public void Un_dimanche_la_prevision_commence_le_lendemain()
    {
        DateOnly dimanche = new(2026, 10, 25);

        PaymentWeatherResponse meteo = PaymentWeatherForecast.Compute([Due(dimanche.AddDays(-2), 100m)], dimanche);

        meteo.Forecast[0].Should().Be(new CollectionForecastWeekResponse(new DateOnly(2026, 10, 26), 100m, 1));
    }

    [Fact]
    public void La_gravite_classe_les_meteos_de_l_orage_a_l_inconnu()
    {
        PaymentWeatherDto[] ordre = [PaymentWeatherDto.Unknown, PaymentWeatherDto.Sunny, PaymentWeatherDto.Cloudy, PaymentWeatherDto.Rainy, PaymentWeatherDto.Stormy];

        ordre.Select(PaymentWeatherForecast.Severity).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Le_cas_d_usage_calcule_au_jour_de_Paris()
    {
        // 23 h 30 UTC le 15 : déjà le 16 à Paris.
        GetPaymentWeatherHandler handler = new(new Reader([Reglee(0), Reglee(0)]), new Horloge(new DateTimeOffset(2026, 10, 15, 23, 30, 0, TimeSpan.Zero)));

        Result<PaymentWeatherResponse> meteo = await handler.HandleAsync(new GetPaymentWeatherQuery(new DossierId(Guid.CreateVersion7())), default);

        meteo.Value.Today.Should().Be(Aujourdhui);
        meteo.Value.Customers.Single().Weather.Should().Be(PaymentWeatherDto.Sunny);
        new GetPaymentWeatherQuery(new DossierId(Guid.CreateVersion7())).Permission.Should().Be(Lettra.Application.Abstractions.Identity.Permission.DossierLire);
    }

    private sealed class Reader(IReadOnlyList<PaymentHistoryInvoice> factures) : IPaymentHistoryReader
    {
        public Task<IReadOnlyList<PaymentHistoryInvoice>> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(factures);
    }

    private sealed class Horloge(DateTimeOffset maintenant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => maintenant;
    }
}
