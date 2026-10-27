using Lettra.Application.Abstractions.Cabinet;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;
using Lettra.Domain.Parties;
using Lettra.Exports.Application.Tax;

namespace Lettra.Exports.Tests.Tax;

/// <summary>Déclaration des honoraires (DAS 2) : exercice par défaut, lignes, seuil, échéance au calendrier, indicateur de la vue cabinet.</summary>
public sealed class FeeReturnTests
{
    private static readonly DateOnly Start = new(2025, 1, 1);
    private static readonly DateOnly End = new(2025, 12, 31);
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static FeePayee Payee(string name, decimal? outside = null, string? siret = "73282932000074", string country = "FR", string? iban = "FR76…", bool tracked = true) =>
        new(SupplierId.New(), name, siret, "2 chemin du Moulin, 45000 Orléans", country, iban,
            tracked ? FeeBeneficiary.Create(SupplierId.New(), new(FeeNature.Fees, "Avocat", outside is { } o ? new(Start, o) : null), Now).Value : null);

    private static FiscalDeadlineResponse Deadline(FiscalDeadlineStateDto state, DateOnly due) =>
        new(FiscalObligationDto.FeeReturnDas2, Start, End, due, state, null, "Ca12");

    [Fact]
    public void Par_defaut_le_dernier_exercice_clos()
    {
        FeeReturns.LastClosed(FiscalSettings.Default, new DateOnly(2026, 3, 1)).Should().Be((Start, End));
        FeeReturns.LastClosed(FiscalSettings.Default with { FiscalYearEndMonth = 6, FiscalYearEndDay = 30 }, new DateOnly(2026, 3, 1))
            .Should().Be((new DateOnly(2024, 7, 1), new DateOnly(2025, 6, 30)));
    }

    [Fact]
    public void Les_virements_et_les_sommes_notees_s_additionnent_du_plus_paye_au_moins_paye()
    {
        FeePayee lawyer = Payee("Cabinet Durand", outside: 300m);
        FeePayee architect = Payee("Atelier Roux", siret: null);
        FeePayee foreign = Payee("Studio Bruxelles", siret: null, country: "BE", iban: null);
        FeePayee untracked = Payee("Papeterie", tracked: false);
        Dictionary<FeePayee, decimal> bank = new() { [lawyer] = 2_200m, [architect] = 2_400m };

        FeeReturnResponse view = FeeReturns.Build(Start, End, null, [untracked, foreign, architect, lawyer], p => bank.GetValueOrDefault(p), new DateOnly(2026, 2, 27));

        view.Rows.Select(r => r.Name).Should().Equal("Cabinet Durand", "Atelier Roux", "Studio Bruxelles");
        view.Rows[0].Should().Match<FeeBeneficiaryRow>(r => r.PaidByBank == 2_200m && r.PaidOutside == 300m && r.Total == 2_500m && r.ToDeclare && !r.SiretMissing && r.HasIban);
        view.Rows[1].Should().Match<FeeBeneficiaryRow>(r => r.Total == 2_400m && !r.ToDeclare && r.SiretMissing && r.Nature == FeeNatureDto.Fees && r.Profession == "Avocat");
        view.Rows[2].Should().Match<FeeBeneficiaryRow>(r => !r.SiretMissing && !r.HasIban, "établi hors de France, sans SIRET");
        view.Candidates.Should().ContainSingle().Which.Should().Be(new FeeReturnCandidate(untracked.Id.Value, "Papeterie"));
        (view.Threshold, view.BankLinesUntil, view.FiscalYearStart, view.FiscalYearEnd).Should().Be((2_400m, new DateOnly(2026, 2, 27), Start, End));
        FeeReturns.Build(new DateOnly(2023, 1, 1), new DateOnly(2023, 12, 31), null, [architect], p => bank.GetValueOrDefault(p), null)
            .Should().Match<FeeReturnResponse>(v => v.Threshold == 1_200m && v.Rows[0].ToDeclare, "avant 2024, le seuil était de 1 200 €");
        FluentActions.Invoking(() => FeeReturns.Row(untracked, 0m, Start, End)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => FeeReturns.Build(Start, End, null, null!, _ => 0m, null)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => FeeReturns.Build(Start, End, null, [], null!, null)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void La_vue_cabinet_annonce_la_das2_puis_l_exige_a_trente_jours_et_la_signale_en_retard()
    {
        FeePayee lawyer = Payee("Cabinet Durand", outside: 3_000m);
        DateOnly due = new(2026, 5, 5);
        static FeeReturnResponse View(FiscalDeadlineResponse? deadline, params FeePayee[] payees) => FeeReturns.Build(Start, End, deadline, payees, _ => 0m, null);

        FeeReturns.Indicator(View(Deadline(FiscalDeadlineStateDto.Upcoming, due), lawyer), new DateOnly(2026, 3, 1)).Should().Be(
            new DossierIndicator(DossierIndicatorKeys.FeeReturn, 1, IndicatorLevel.Info, due));
        FeeReturns.Indicator(View(Deadline(FiscalDeadlineStateDto.Upcoming, due), lawyer), new DateOnly(2026, 4, 5))!.Level.Should().Be(IndicatorLevel.Warning);
        FeeReturns.Indicator(View(Deadline(FiscalDeadlineStateDto.Overdue, due), lawyer), new DateOnly(2026, 6, 1))!.Level.Should().Be(IndicatorLevel.Critical);
        FeeReturns.Indicator(View(Deadline(FiscalDeadlineStateDto.Filed, due), lawyer), new DateOnly(2026, 4, 5)).Should().BeNull("déposée");
        FeeReturns.Indicator(View(Deadline(FiscalDeadlineStateDto.Past, due), lawyer), new DateOnly(2026, 6, 1)).Should().BeNull("avant le suivi");
        FeeReturns.Indicator(View(Deadline(FiscalDeadlineStateDto.Upcoming, due), Payee("Petit montant", outside: 100m)), new DateOnly(2026, 4, 5)).Should().BeNull("sous le seuil");
        FeeReturns.Indicator(View(null, lawyer), new DateOnly(2026, 4, 5)).Should().BeNull("sans fiche entreprise, pas de calendrier");
        FluentActions.Invoking(() => FeeReturns.Indicator(null!, Start)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Le_calendrier_montre_la_das2_avec_la_declaration_de_resultats_des_qu_un_beneficiaire_est_suivi()
    {
        FiscalCalendarInput input = new(FiscalSettings.Default, VatDeclarationRegime.RealNormal, false, new DateOnly(2026, 3, 1),
            new HashSet<DateOnly>(), new Dictionary<DateOnly, decimal>(), []);

        FiscalCalendar.Compute(input, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).Should().NotContain(d => d.Obligation == FiscalObligationDto.FeeReturnDas2);
        FiscalDeadlineResponse das2 = FiscalCalendar.Compute(input with { FeeBeneficiaries = true }, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))
            .Should().ContainSingle(d => d.Obligation == FiscalObligationDto.FeeReturnDas2).Subject;
        (das2.PeriodStart, das2.PeriodEnd, das2.DueDate, das2.Note).Should().Be((Start, End, new DateOnly(2026, 5, 5), "Ca12"));
        FiscalCalendar.Compute(input with { FeeBeneficiaries = true, Settings = FiscalSettings.Default with { FiscalYearEndMonth = 6, FiscalYearEndDay = 30 } },
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))
            .Single(d => d.Obligation == FiscalObligationDto.FeeReturnDas2).Should().Match<FiscalDeadlineResponse>(d =>
                d.PeriodEnd == new DateOnly(2026, 6, 30) && d.DueDate == new DateOnly(2026, 9, 30) && d.Note == "Ca12NonCalendar");
    }
}
