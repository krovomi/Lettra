using Lettra.Contracts.Insights;
using Lettra.Contracts.Tax;
using Lettra.Exports.Application.Insights;
using Lettra.Exports.Application.Tax;

namespace Lettra.Exports.Tests.Insights;

/// <summary>
/// Le simulateur « et si ? » : chaque levier (délai accordé, facture essayée, régime de TVA, embauche) change la
/// trésorerie attendue des treize semaines comme ses hypothèses le disent, et rien d'autre.
/// </summary>
public sealed class WhatIfSimulatorTests
{
    // Un lundi : les semaines simulées partent du 26 octobre 2026 et s'arrêtent avant le 25 janvier 2027.
    private static readonly DateOnly Today = new(2026, 10, 26);

    private static readonly ExpectedCollectionResponse[] Expected =
    [
        new(Guid.CreateVersion7(), "FA-1", new DateOnly(2026, 10, 20), 1_000m, new DateOnly(2026, 11, 2)),
        new(Guid.CreateVersion7(), "FA-2", new DateOnly(2027, 1, 1), 500m, new DateOnly(2027, 1, 20)),
        new(Guid.CreateVersion7(), "FA-3", new DateOnly(2026, 6, 1), 9_999m, new DateOnly(2026, 10, 30), Doubtful: true),
    ];

    private static WhatIfResponse Simulate(WhatIfRequest scenario, FiscalSettings? settings = null) =>
        WhatIfSimulator.Simulate(Expected, settings ?? FiscalSettings.Default, scenario, Today);

    private static WhatIfInvoiceRequest Invoice(DateOnly issued) => new(10_000m, 20m, issued, 30);

    [Fact]
    public void Sans_levier_la_tresorerie_attendue_reste_telle_quelle_sans_les_factures_douteuses()
    {
        WhatIfResponse view = Simulate(new WhatIfRequest());

        view.Weeks.Should().HaveCount(WhatIfRequest.Weeks);
        view.Weeks[0].WeekStart.Should().Be(Today);
        (view.BaselineIn, view.ScenarioIn, view.ScenarioOut, view.Gap).Should().Be((1_500m, 1_500m, 0m, 0m));
        view.Effects.Should().BeEmpty();
        view.AppliedVat.Should().Be(WhatIfVatDto.Monthly, "les réglages par défaut déclarent chaque mois");
        view.Invoice.Should().BeNull();
        view.Hire.Should().BeNull();
    }

    [Fact]
    public void Un_delai_accorde_decale_chaque_encaissement_et_peut_le_sortir_des_semaines_simulees()
    {
        WhatIfResponse view = Simulate(new WhatIfRequest(ExtraDelayDays: 10));

        view.ScenarioIn.Should().Be(1_000m, "FA-2 glisse au 30 janvier, après les treize semaines");
        view.Gap.Should().Be(-500m);
        view.Weeks[1].Should().Be(new WhatIfWeekResponse(new DateOnly(2026, 11, 2), 1_000m, 0m, 0m, -1_000m));
        view.Weeks[2].CumulativeGap.Should().Be(0m);
        view.Effects.Select(e => (e.Kind, e.Label, e.Date, e.From)).Should().Equal(
            (WhatIfEffectKindDto.CollectionMoved, "FA-1", new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 2)),
            (WhatIfEffectKindDto.CollectionMoved, "FA-2", new DateOnly(2027, 1, 30), new DateOnly(2027, 1, 20)));
    }

    [Fact]
    public void La_facture_essayee_apporte_son_TTC_a_l_echeance_et_sa_TVA_sort_a_l_echeance_de_la_declaration()
    {
        WhatIfResponse view = Simulate(new WhatIfRequest(Invoice: Invoice(new DateOnly(2026, 10, 31))));

        view.Invoice.Should().Be(new WhatIfInvoiceResponse(
            10_000m, 2_000m, 12_000m, new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31),
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 30)));
        (view.ScenarioIn, view.ScenarioOut, view.Gap).Should().Be((13_500m, 2_000m, 10_000m));
        view.Effects.Select(e => (e.Kind, e.Amount)).Should().Equal(
            (WhatIfEffectKindDto.VatPaid, -2_000m),
            (WhatIfEffectKindDto.InvoiceCollected, 12_000m));
    }

    [Fact]
    public void Facturer_apres_la_cloture_change_l_exercice_et_la_periode_de_TVA()
    {
        FiscalSettings octobre = FiscalSettings.Default with { FiscalYearEndMonth = 10, FiscalYearEndDay = 31 };

        WhatIfInvoiceResponse before = Simulate(new WhatIfRequest(Invoice: Invoice(new DateOnly(2026, 10, 31))), octobre).Invoice!;
        WhatIfInvoiceResponse after = Simulate(new WhatIfRequest(Invoice: Invoice(new DateOnly(2026, 11, 1))), octobre).Invoice!;

        (before.FiscalYearEnd, before.VatDueDate).Should().Be((new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 15)));
        (after.FiscalYearEnd, after.VatDueDate).Should().Be((new DateOnly(2027, 10, 31), new DateOnly(2026, 12, 15)));
        after.RevenueMonth.Should().Be(new DateOnly(2026, 11, 1));
    }

    [Fact]
    public void Le_regime_de_TVA_essaye_change_la_periode_ou_supprime_la_TVA()
    {
        WhatIfRequest trial = new(Invoice: Invoice(new DateOnly(2026, 11, 3)));

        WhatIfResponse quarterly = Simulate(trial with { Vat = WhatIfVatDto.Quarterly });
        WhatIfResponse current = Simulate(trial, FiscalSettings.Default with { VatPeriodicity = VatPeriodicityDto.Quarterly, VatDeadlineDay = 24 });
        WhatIfResponse franchise = Simulate(trial with { Vat = WhatIfVatDto.Franchise });

        (quarterly.Invoice!.VatPeriodStart, quarterly.Invoice.VatPeriodEnd, quarterly.Invoice.VatDueDate)
            .Should().Be((new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 15)));
        quarterly.ScenarioOut.Should().Be(2_000m, "le 15 janvier tombe encore dans les treize semaines");
        current.AppliedVat.Should().Be(WhatIfVatDto.Quarterly);
        current.CurrentPeriodicity.Should().Be(VatPeriodicityDto.Quarterly);
        current.Invoice!.VatDueDate.Should().Be(new DateOnly(2027, 1, 24));
        (franchise.Invoice!.Vat, franchise.Invoice.TotalInclTax, franchise.Invoice.VatDueDate).Should().Be((0m, 10_000m, null));
        franchise.Effects.Should().NotContain(e => e.Kind == WhatIfEffectKindDto.VatPaid);
    }

    [Fact]
    public void L_embauche_coute_le_salaire_charge_au_prorata_le_premier_mois()
    {
        WhatIfResponse view = Simulate(new WhatIfRequest(Hire: new WhatIfHireRequest(2_000m, new DateOnly(2026, 11, 16), 50m)));

        view.Hire.Should().Be(new WhatIfHireResponse(3_000m, 4_500m, 36_000m));
        view.Effects.Select(e => (e.Date, e.Amount)).Should().Equal(
            (new DateOnly(2026, 11, 30), -1_500m),
            (new DateOnly(2026, 12, 31), -3_000m));
        view.Gap.Should().Be(-4_500m);
    }

    [Fact]
    public void Une_embauche_passee_ne_compte_que_les_salaires_a_venir()
    {
        WhatIfResponse view = Simulate(new WhatIfRequest(Hire: new WhatIfHireRequest(1_000m, new DateOnly(2026, 9, 1), 0m)));

        view.Effects.Select(e => e.Date).Should().Equal(new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 31));
        view.Hire!.CostInHorizon.Should().Be(3_000m);
    }

    [Fact]
    public void Un_scenario_hors_bornes_est_refuse_champ_par_champ()
    {
        WhatIfRequest scenario = new(
            ExtraDelayDays: 121,
            Invoice: new WhatIfInvoiceRequest(0m, 19.6m, Today.AddDays(-1), 61),
            Vat: (WhatIfVatDto)9,
            Hire: new WhatIfHireRequest(-1m, Today.AddYears(2), 101m));

        WhatIfSimulator.Invalid(scenario, Today).Should().Equal(
            "ExtraDelayDays", "Vat", "Invoice.AmountExclTax", "Invoice.VatRatePercent", "Invoice.IssueDate", "Invoice.PaymentTermDays",
            "Hire.MonthlyGrossSalary", "Hire.EmployerChargesPercent", "Hire.StartDate");
        WhatIfSimulator.Invalid(new WhatIfRequest(Invoice: Invoice(Today), Hire: new WhatIfHireRequest(1m, Today, 0m)), Today).Should().BeEmpty();
    }
}
