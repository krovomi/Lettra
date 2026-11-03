using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Features.Kpi;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Pilotage;
using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;
using Lettra.Domain.Prevention;
using Lettra.Domain.Vigilance;
using Lettra.Exports.Application.Insights;
using Lettra.Exports.Application.Insights.Prevention;

namespace Lettra.Exports.Tests.Insights;

/// <summary>
/// Radar de prévention : chaque signal s'allume selon sa règle et son seuil, dit sa raison par ses chiffres, ou dit ce
/// qui lui manque ; le niveau du dossier se déduit des signaux ; la vue cabinet ne reçoit que le niveau et le nombre.
/// </summary>
public sealed class PreventionRulesTests
{
    // Mardi : la semaine en cours commence le lundi 2 novembre.
    private static readonly DateOnly Aujourdhui = new(2026, 11, 3);
    private static readonly DateOnly Lundi = new(2026, 11, 2);
    private static readonly Guid Dupont = Guid.CreateVersion7();
    private static readonly Guid Durand = Guid.CreateVersion7();

    private static readonly PeerComparisonResponse SansSecteur = new(PeerComparisonStatusDto.NoSector, null, null, 0, null, []);

    private static PreventionSettings Seuils(
        decimal? solde = null, DateOnly? soldeAu = null, int horizon = 8, int hausse = 5, int ecart = 15, int part = 50, int facture = 20, int appel = 3,
        params PreventionSignal[] desactives)
    {
        PreventionSettings settings = PreventionSettings.Default();
        settings.Update(
                new PreventionThresholds(horizon, solde, solde is null ? null : soldeAu ?? Aujourdhui, hausse, ecart, part, facture, appel, desactives),
                Guid.CreateVersion7(),
                DateTimeOffset.UnixEpoch,
                Aujourdhui)
            .IsSuccess.Should().BeTrue();
        return settings;
    }

    private static PreventionInput Entree(
        PreventionSettings? seuils = null,
        IReadOnlyList<PaymentHistoryInvoice>? factures = null,
        IReadOnlyList<InvoiceKpiFact>? ventes = null,
        IReadOnlyList<PreventionPayable>? achats = null,
        IReadOnlyList<PreventionVatPayment>? tva = null,
        PreventionUnavailableDto tvaIndisponible = PreventionUnavailableDto.None,
        PeerComparisonResponse? pairs = null,
        IReadOnlyList<PreventionCustomerCheck>? clients = null) =>
        new(seuils ?? Seuils(), factures ?? [], new BillingKpiFacts(ventes ?? [], []), achats ?? [], tva ?? [], tvaIndisponible, pairs ?? SansSecteur, clients ?? []);

    private static PreventionSignalResponse Signal(PreventionInput entree, PreventionSignalDto signal) =>
        PreventionRules.Compute(entree, Aujourdhui).Signals.Single(s => s.Signal == signal);

    /// <summary>Une facture encore due, d'échéance <paramref name="echeance"/>.</summary>
    private static PaymentHistoryInvoice Due(DateOnly echeance, decimal reste, Guid? client = null) =>
        new(Guid.CreateVersion7(), client ?? Dupont, client == Durand ? "Durand" : "Dupont SARL", "FAC-1", echeance.AddDays(-30), echeance, reste, reste, []);

    /// <summary>Une facture émise le <paramref name="emise"/>, avec ses encaissements datés.</summary>
    private static PaymentHistoryInvoice Emise(DateOnly emise, decimal montant, params (DateOnly Date, decimal Montant)[] encaissements) =>
        new(
            Guid.CreateVersion7(), Dupont, "Dupont SARL", "FAC-" + emise.ToString("MMdd", System.Globalization.CultureInfo.InvariantCulture), emise, emise.AddDays(30),
            montant, montant - encaissements.Sum(e => e.Montant), [.. encaissements.Select(e => new PaymentHistoryEntry(e.Date, e.Montant))]);

    private static PreventionPayable Achat(DateOnly echeance, decimal ttc) => new(echeance, ttc);

    private static InvoiceKpiFact Vente(Guid client, decimal ht, DateOnly? emise = null, InvoiceKind nature = InvoiceKind.Invoice) =>
        new(nature, emise ?? Aujourdhui.AddMonths(-2), null, client, ht, ht * 1.2m, 0m, 0m);

    [Fact]
    public void Sans_rien_a_signaler_le_dossier_est_calme_et_la_vue_cabinet_ne_compte_aucun_signal()
    {
        PreventionRadarResponse radar = PreventionRules.Compute(Entree(), Aujourdhui);

        radar.Level.Should().Be(PreventionLevelDto.Calm);
        radar.ActiveSignals.Should().Be(0);
        radar.Signals.Select(s => s.Signal).Should().Equal(Enum.GetValues<PreventionSignalDto>());
        radar.Today.Should().Be(Aujourdhui);
        PreventionRules.Indicator(radar).Should().Be(new DossierIndicator(DossierIndicatorKeys.Prevention, 0, IndicatorLevel.Ok, null));
        PreventionRules.Summarize(radar).Should().BeEquivalentTo(new PreventionSummaryResponse(true, PreventionLevelDto.Calm, 0, [], null));
    }

    [Fact]
    public void La_tresorerie_demande_un_solde_bancaire_recent()
    {
        PreventionSignalResponse sansSolde = Signal(Entree(), PreventionSignalDto.Treasury);
        PreventionSignalResponse ancien = Signal(Entree(Seuils(solde: 5000m, soldeAu: Aujourdhui.AddDays(-46))), PreventionSignalDto.Treasury);

        sansSolde.State.Should().Be(PreventionSignalStateDto.NotEvaluated);
        sansSolde.Unavailable.Should().Be(PreventionUnavailableDto.NoCashBalance);
        ancien.Unavailable.Should().Be(PreventionUnavailableDto.CashBalanceOutdated);
        ancien.Figures.Should().ContainSingle().Which.Date.Should().Be(Aujourdhui.AddDays(-46));
    }

    [Fact]
    public void Un_solde_qui_passe_sous_zero_tot_dans_l_horizon_est_grave_et_dit_quand_et_combien()
    {
        PreventionInput entree = Entree(Seuils(solde: 1000m), achats: [Achat(Lundi.AddDays(8), 5000m)]);

        PreventionSignalResponse tresorerie = Signal(entree, PreventionSignalDto.Treasury);

        tresorerie.State.Should().Be(PreventionSignalStateDto.Call, "sous zéro dès la deuxième semaine sur huit");
        tresorerie.Impact.Should().Be(4000m);
        tresorerie.DueOn.Should().Be(Lundi.AddDays(7));
        tresorerie.Weeks.Should().HaveCount(8);
        tresorerie.Weeks[1].Should().Be(new PreventionWeekDto(Lundi.AddDays(7), 0m, 5000m, -4000m));
        tresorerie.Figures.Select(f => f.Name).Should().Equal("week", "lowestBalance", "cashBalance", "cashBalanceOn", "horizonWeeks");
        tresorerie.Figures[1].Number.Should().Be(-4000m);
    }

    [Fact]
    public void Un_solde_qui_passe_sous_zero_tard_dans_l_horizon_est_a_surveiller()
    {
        PreventionSignalResponse tresorerie = Signal(Entree(Seuils(solde: 1000m), achats: [Achat(Lundi.AddDays(36), 1500m)]), PreventionSignalDto.Treasury);

        tresorerie.State.Should().Be(PreventionSignalStateDto.Watch);
        tresorerie.DueOn.Should().Be(Lundi.AddDays(35));
    }

    [Fact]
    public void Les_encaissements_attendus_comblent_les_sorties_et_une_facture_echue_sort_tout_de_suite()
    {
        PreventionInput entree = Entree(
            Seuils(solde: 1000m),
            factures: [Due(Lundi.AddDays(9), 4500m)],
            achats: [Achat(Lundi.AddDays(10), 5000m), Achat(Aujourdhui.AddDays(-20), 400m)]);

        PreventionSignalResponse tresorerie = Signal(entree, PreventionSignalDto.Treasury);

        tresorerie.State.Should().Be(PreventionSignalStateDto.Clear);
        tresorerie.Weeks[0].Outflows.Should().Be(400m, "la facture fournisseur échue non payée sort la semaine en cours");
        tresorerie.Weeks[1].Should().Be(new PreventionWeekDto(Lundi.AddDays(7), 4500m, 5000m, 100m));
        tresorerie.Impact.Should().BeNull();
    }

    [Fact]
    public void La_tva_d_une_periode_close_sort_de_la_tresorerie_a_son_echeance()
    {
        PreventionInput entree = Entree(
            Seuils(solde: 3000m),
            tva: [new PreventionVatPayment(FiscalObligationDto.VatReturnCa3, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 24), 2000m)]);

        PreventionSignalResponse tresorerie = Signal(entree, PreventionSignalDto.Treasury);

        tresorerie.Weeks.Single(w => w.Start == new DateOnly(2026, 11, 23)).Outflows.Should().Be(2000m);
        tresorerie.Weeks[^1].Balance.Should().Be(1000m);
    }

    /// <summary>Des ventes de 3 000 € par mois dont le paiement ralentit : délai moyen de 30, 45, 60 puis 75 jours.</summary>
    private static PaymentHistoryInvoice[] PaiementsQuiRalentissent() =>
    [
        Emise(new DateOnly(2026, 6, 1), 3000m, (new DateOnly(2026, 6, 20), 3000m)),
        Emise(new DateOnly(2026, 7, 1), 3000m, (new DateOnly(2026, 7, 15), 1000m), (new DateOnly(2026, 8, 15), 2000m)),
        Emise(new DateOnly(2026, 8, 1), 3000m, (new DateOnly(2026, 9, 15), 2000m), (new DateOnly(2026, 10, 15), 1000m)),
        Emise(new DateOnly(2026, 9, 1), 3000m, (new DateOnly(2026, 10, 20), 1000m)),
        Emise(new DateOnly(2026, 10, 1), 3000m),
    ];

    [Fact]
    public void Un_delai_de_paiement_qui_s_allonge_trois_mois_de_suite_allume_le_signal()
    {
        PreventionSignalResponse grave = Signal(Entree(factures: PaiementsQuiRalentissent()), PreventionSignalDto.PaymentDelay);
        PreventionSignalResponse surveille = Signal(Entree(Seuils(hausse: 20), factures: PaiementsQuiRalentissent()), PreventionSignalDto.PaymentDelay);
        PreventionSignalResponse calme = Signal(Entree(Seuils(hausse: 46), factures: PaiementsQuiRalentissent()), PreventionSignalDto.PaymentDelay);

        grave.Months.Select(m => m.DaysSalesOutstanding).Should().Equal(30m, 45m, 60m, 75m);
        grave.Months.Select(m => m.Month).Should().Equal(new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1));
        grave.State.Should().Be(PreventionSignalStateDto.Call, "45 jours de hausse, au moins trois fois le seuil de 5");
        grave.Impact.Should().Be(3000m, "45 jours de plus sur 6 000 € de ventes en 90 jours");
        grave.Figures.Select(f => f.Number).Should().Equal(30m, 75m, 45m, 5m, 3000m);
        surveille.State.Should().Be(PreventionSignalStateDto.Watch);
        calme.State.Should().Be(PreventionSignalStateDto.Clear);
        calme.Months.Should().HaveCount(4);
    }

    [Fact]
    public void Un_delai_qui_baisse_un_mois_ne_compte_pas_et_sans_ventes_il_n_y_a_pas_de_tendance()
    {
        PaymentHistoryInvoice[] factures = PaiementsQuiRalentissent();
        factures[3] = Emise(new DateOnly(2026, 9, 1), 3000m, (new DateOnly(2026, 9, 10), 3000m));

        Signal(Entree(factures: factures), PreventionSignalDto.PaymentDelay).State.Should().Be(PreventionSignalStateDto.Clear);
        Signal(Entree(), PreventionSignalDto.PaymentDelay).Unavailable.Should().Be(PreventionUnavailableDto.NotEnoughHistory);
        PreventionRules.DaysSalesOutstanding([], Aujourdhui).Should().BeNull();
    }

    private static PeerComparisonResponse Pairs(double evolution, double mediane = 0.05) => new(
        PeerComparisonStatusDto.Available, "43.39Z", "43", 6, DateTimeOffset.UnixEpoch,
        [new PeerMetricComparisonDto(PeerMetricDto.RevenueTrend, evolution, mediane, 6, PeerPositionDto.Worse)]);

    [Theory]
    [InlineData(-0.20, PreventionSignalStateDto.Watch)]
    [InlineData(-0.30, PreventionSignalStateDto.Call)]
    [InlineData(-0.05, PreventionSignalStateDto.Clear)]
    [InlineData(0.02, PreventionSignalStateDto.Clear)]
    public void Un_chiffre_d_affaires_qui_recule_loin_derriere_le_secteur_allume_le_signal(double evolution, PreventionSignalStateDto attendu)
    {
        PreventionSignalResponse pairs = Signal(Entree(pairs: Pairs(evolution)), PreventionSignalDto.PeerGap);

        pairs.State.Should().Be(attendu);
        pairs.Figures.Select(f => f.Name).Should().Equal("trend", "median", "gapPoints", "peers", "threshold");
        pairs.Figures[1].Number.Should().Be(5.0m);
    }

    [Theory]
    [InlineData(PeerComparisonStatusDto.NoSector, PreventionUnavailableDto.NoSector)]
    [InlineData(PeerComparisonStatusDto.NoCollection, PreventionUnavailableDto.NoCollection)]
    [InlineData(PeerComparisonStatusDto.TooFewPeers, PreventionUnavailableDto.TooFewPeers)]
    public void Sans_comparaison_possible_le_signal_des_pairs_dit_pourquoi(PeerComparisonStatusDto statut, PreventionUnavailableDto raison) =>
        Signal(Entree(pairs: new PeerComparisonResponse(statut, null, null, 0, null, [])), PreventionSignalDto.PeerGap).Unavailable.Should().Be(raison);

    [Fact]
    public void Sans_evolution_du_dossier_le_signal_des_pairs_n_est_pas_evalue()
    {
        PeerComparisonResponse sansValeur = Pairs(0) with { Metrics = [new PeerMetricComparisonDto(PeerMetricDto.RevenueTrend, null, 0.05, 6, null)] };
        PeerComparisonResponse sansIndicateur = Pairs(0) with { Metrics = [] };

        Signal(Entree(pairs: sansValeur), PreventionSignalDto.PeerGap).Unavailable.Should().Be(PreventionUnavailableDto.NoTrend);
        Signal(Entree(pairs: sansIndicateur), PreventionSignalDto.PeerGap).Unavailable.Should().Be(PreventionUnavailableDto.TooFewPeers);
    }

    private static PreventionVatPayment Ca3(DateOnly echeance, decimal montant) =>
        new(FiscalObligationDto.VatReturnCa3, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), echeance, montant);

    [Fact]
    public void Une_tva_plus_forte_que_les_encaissements_attendus_avant_l_echeance_allume_le_signal()
    {
        PreventionSignalResponse surveille = Signal(Entree(tva: [Ca3(new DateOnly(2026, 11, 24), 5000m)]), PreventionSignalDto.VatBeyondCollections);
        PreventionSignalResponse grave = Signal(Entree(tva: [Ca3(new DateOnly(2026, 11, 15), 5000m)]), PreventionSignalDto.VatBeyondCollections);
        PreventionSignalResponse couverte = Signal(
            Entree(factures: [Due(new DateOnly(2026, 11, 10), 6000m)], tva: [Ca3(new DateOnly(2026, 11, 24), 5000m)]), PreventionSignalDto.VatBeyondCollections);

        surveille.State.Should().Be(PreventionSignalStateDto.Watch);
        surveille.Impact.Should().Be(5000m);
        surveille.DueOn.Should().Be(new DateOnly(2026, 11, 24));
        surveille.Figures.Select(f => f.Name).Should().Equal("obligation", "dueDate", "vat", "collections", "periodStart", "periodEnd");
        surveille.Figures[0].Text.Should().Be("VatReturnCa3");
        grave.State.Should().Be(PreventionSignalStateDto.Call, "à moins de quinze jours");
        couverte.State.Should().Be(PreventionSignalStateDto.Clear);
    }

    [Fact]
    public void Sans_tva_connue_le_signal_dit_ce_qui_manque()
    {
        Signal(Entree(tvaIndisponible: PreventionUnavailableDto.VatFranchise), PreventionSignalDto.VatBeyondCollections).Unavailable
            .Should().Be(PreventionUnavailableDto.VatFranchise);
        Signal(Entree(tva: [Ca3(new DateOnly(2027, 1, 24), 5000m)]), PreventionSignalDto.VatBeyondCollections).Unavailable
            .Should().Be(PreventionUnavailableDto.NoVatDeadline, "l'échéance est au-delà de 45 jours");
    }

    [Fact]
    public void Un_premier_client_qui_pese_plus_que_le_seuil_allume_le_signal_avec_son_nom()
    {
        PreventionCustomerCheck[] clients = [new(Dupont, "Dupont SARL", false, null, null), new(Durand, "Durand", false, null, null)];
        InvoiceKpiFact[] ventes = [Vente(Dupont, 6000m), Vente(Durand, 4000m), Vente(Dupont, 500m, nature: InvoiceKind.CreditNote), Vente(Durand, 9000m, Aujourdhui.AddYears(-2))];

        PreventionSignalResponse dependance = Signal(Entree(ventes: ventes, clients: clients), PreventionSignalDto.TopCustomer);
        PreventionSignalResponse sousLeSeuil = Signal(Entree(Seuils(part: 60), ventes: ventes, clients: clients), PreventionSignalDto.TopCustomer);

        dependance.State.Should().Be(PreventionSignalStateDto.Watch);
        dependance.Figures[0].Text.Should().Be("Dupont SARL");
        dependance.Figures[1].Number.Should().Be(57.9m, "5 500 € sur 9 500 € : l'avoir compte, la vente d'il y a deux ans non");
        dependance.Parties.Should().ContainSingle().Which.Id.Should().Be(Dupont);
        dependance.Impact.Should().Be(5500m);
        sousLeSeuil.State.Should().Be(PreventionSignalStateDto.Clear);
        Signal(Entree(), PreventionSignalDto.TopCustomer).Unavailable.Should().Be(PreventionUnavailableDto.NoRevenue);
    }

    [Fact]
    public void Un_client_en_procedure_collective_qui_doit_encore_de_l_argent_est_grave_avec_la_date_de_declaration()
    {
        DateOnly publiee = new(2026, 10, 20);
        PreventionCustomerCheck[] clients = [new(Dupont, "Dupont SARL", true, LegalNoticeKind.CollectiveProceeding, publiee), new(Durand, "Durand", true, null, null)];

        PreventionSignalResponse grave = Signal(Entree(factures: [Due(Aujourdhui.AddDays(10), 1200m)], clients: clients), PreventionSignalDto.CustomersInProceedings);
        PreventionSignalResponse rienDu = Signal(Entree(clients: clients), PreventionSignalDto.CustomersInProceedings);
        PreventionSignalResponse aucun = Signal(Entree(clients: [new(Durand, "Durand", true, LegalNoticeKind.StruckOff, publiee)]), PreventionSignalDto.CustomersInProceedings);

        grave.State.Should().Be(PreventionSignalStateDto.Call);
        grave.Impact.Should().Be(1200m);
        grave.DueOn.Should().Be(new DateOnly(2026, 12, 20), "la créance se déclare dans les deux mois de la publication");
        grave.Parties.Should().ContainSingle().Which.Should().Be(new PreventionPartyDto(Dupont, "Dupont SARL", 1200m, publiee));
        grave.Figures.Select(f => f.Number).Should().Equal(1m, 1200m, 2m, 2m);
        rienDu.State.Should().Be(PreventionSignalStateDto.Watch);
        rienDu.DueOn.Should().BeNull();
        aucun.State.Should().Be(PreventionSignalStateDto.Clear);
    }

    [Fact]
    public void Sans_client_verifie_le_signal_des_procedures_invite_a_verifier()
    {
        PreventionSignalResponse signal = Signal(Entree(clients: [new(Dupont, "Dupont SARL", false, null, null)]), PreventionSignalDto.CustomersInProceedings);

        signal.Unavailable.Should().Be(PreventionUnavailableDto.NoCustomerChecked);
        signal.Figures.Should().ContainSingle().Which.Number.Should().Be(1m);
    }

    /// <summary>Trois mois facturés 10 000 € chacun dont <paramref name="encaisse"/> rentre dans le mois.</summary>
    private static PaymentHistoryInvoice[] Mois(decimal encaisse) =>
    [
        Emise(new DateOnly(2026, 8, 3), 10000m, (new DateOnly(2026, 8, 20), encaisse)),
        Emise(new DateOnly(2026, 9, 3), 10000m, (new DateOnly(2026, 9, 20), encaisse)),
        Emise(new DateOnly(2026, 10, 3), 10000m, (new DateOnly(2026, 10, 20), encaisse)),
    ];

    [Fact]
    public void Un_ecart_entre_facture_et_encaisse_qui_grandit_chaque_mois_allume_le_signal()
    {
        PreventionSignalResponse grave = Signal(Entree(factures: Mois(6000m)), PreventionSignalDto.BilledCollectedGap);
        PreventionSignalResponse surveille = Signal(Entree(Seuils(facture: 25), factures: Mois(6000m)), PreventionSignalDto.BilledCollectedGap);
        PaymentHistoryInvoice[] rattrapage = Mois(6000m);
        rattrapage[0] = Emise(new DateOnly(2026, 8, 3), 10000m, (new DateOnly(2026, 8, 20), 6000m), (new DateOnly(2026, 10, 25), 4000m));
        PreventionSignalResponse rattrape = Signal(Entree(factures: rattrapage), PreventionSignalDto.BilledCollectedGap);

        grave.State.Should().Be(PreventionSignalStateDto.Call, "40 % d'écart, le double du seuil de 20 %");
        grave.Impact.Should().Be(12000m);
        grave.Months.Select(m => (m.Billed, m.Collected)).Should().Equal((10000m, 6000m), (10000m, 6000m), (10000m, 6000m));
        grave.Figures.Select(f => f.Number).Should().Equal(30000m, 18000m, 12000m, 40.0m, 3m, 20m);
        surveille.State.Should().Be(PreventionSignalStateDto.Watch);
        rattrape.State.Should().Be(PreventionSignalStateDto.Clear, "octobre encaisse autant qu'il facture");
        Signal(Entree(), PreventionSignalDto.BilledCollectedGap).Unavailable.Should().Be(PreventionUnavailableDto.NotEnoughHistory);
    }

    [Fact]
    public void Un_signal_desactive_ne_compte_pas()
    {
        PreventionInput entree = Entree(Seuils(solde: 1000m, desactives: PreventionSignal.Treasury), achats: [Achat(Lundi.AddDays(8), 5000m)]);

        PreventionRadarResponse radar = PreventionRules.Compute(entree, Aujourdhui);

        radar.Signals[0].State.Should().Be(PreventionSignalStateDto.Disabled);
        radar.Level.Should().Be(PreventionLevelDto.Calm);
        radar.Settings.Disabled.Should().Equal(PreventionSignalDto.Treasury);
    }

    [Fact]
    public void Le_niveau_passe_a_appeler_au_nombre_de_signaux_regle_ou_des_un_signal_grave()
    {
        PreventionCustomerCheck[] clients = [new(Dupont, "Dupont SARL", true, LegalNoticeKind.CollectiveProceeding, Aujourdhui.AddDays(-5))];
        InvoiceKpiFact[] ventes = [Vente(Dupont, 6000m)];

        PreventionRadarResponse deux = PreventionRules.Compute(Entree(ventes: ventes, clients: clients), Aujourdhui);
        PreventionRadarResponse deuxSuffisent = PreventionRules.Compute(Entree(Seuils(appel: 2), ventes: ventes, clients: clients), Aujourdhui);
        PreventionRadarResponse grave = PreventionRules.Compute(Entree(factures: [Due(Aujourdhui.AddDays(5), 800m)], ventes: ventes, clients: clients), Aujourdhui);

        deux.ActiveSignals.Should().Be(2);
        deux.Level.Should().Be(PreventionLevelDto.Watch);
        deuxSuffisent.Level.Should().Be(PreventionLevelDto.Call);
        grave.Level.Should().Be(PreventionLevelDto.Call);
        PreventionRules.Indicator(grave).Should().Be(
            new DossierIndicator(DossierIndicatorKeys.Prevention, 2, IndicatorLevel.Critical, Aujourdhui.AddDays(-5).AddMonths(2)));
        PreventionRules.Indicator(deux).Level.Should().Be(IndicatorLevel.Warning);
        PreventionRules.Summarize(grave).Active.Should().Equal(PreventionSignalDto.CustomersInProceedings, PreventionSignalDto.TopCustomer);
    }

    [Fact]
    public void Les_seuils_rendus_sont_ceux_du_dossier()
    {
        PreventionSettingsResponse seuils = PreventionRules.Describe(Seuils(solde: 1234.567m, horizon: 10, appel: 4, desactives: [PreventionSignal.PeerGap, PreventionSignal.Treasury]));

        seuils.CashBalance.Should().Be(1234.57m);
        seuils.CashBalanceOn.Should().Be(Aujourdhui);
        seuils.TreasuryHorizonWeeks.Should().Be(10);
        seuils.CallSignalCount.Should().Be(4);
        seuils.Disabled.Should().Equal(PreventionSignalDto.Treasury, PreventionSignalDto.PeerGap);
        seuils.Version.Should().Be(1);
    }
}
