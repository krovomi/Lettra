using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;
using Lettra.Exports.Application.Tax;

namespace Lettra.Exports.Tests.Tax;

/// <summary>Calendrier fiscal : échéances selon le régime et les réglages, états, acomptes de CA12, jours fériés.</summary>
public sealed class FiscalCalendarTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);

    private static FiscalCalendarInput Input(
        VatDeclarationRegime? regime,
        FiscalSettings? settings = null,
        bool franchise = false,
        IReadOnlySet<DateOnly>? desMonths = null,
        IReadOnlyDictionary<DateOnly, decimal>? vat = null,
        IReadOnlyList<FiscalFiling>? filings = null) =>
        new(settings ?? FiscalSettings.Default, regime, franchise, Today, desMonths ?? new HashSet<DateOnly>(), vat ?? new Dictionary<DateOnly, decimal>(), filings ?? []);

    private static IReadOnlyList<FiscalDeadlineResponse> Year(FiscalCalendarInput input) =>
        FiscalCalendar.Compute(input, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

    [Fact]
    public void Au_reel_normal_une_ca3_par_mois_au_jour_limite_des_reglages()
    {
        IReadOnlyList<FiscalDeadlineResponse> deadlines = Year(Input(VatDeclarationRegime.RealNormal, FiscalSettings.Default with { VatDeadlineDay = 21 }));

        List<FiscalDeadlineResponse> ca3 = [.. deadlines.Where(d => d.Obligation == FiscalObligationDto.VatReturnCa3)];
        ca3.Should().HaveCount(12).And.OnlyContain(d => d.DueDate.Day == 21);
        ca3.Single(d => d.DueDate.Month == 10).PeriodStart.Should().Be(new DateOnly(2026, 9, 1));
        deadlines.Should().Contain(d => d.Obligation == FiscalObligationDto.Cfe && d.DueDate == new DateOnly(2026, 12, 15));
        deadlines.Where(d => d.Obligation == FiscalObligationDto.CorporateTaxInstallment).Select(d => d.DueDate).Should()
            .Equal(new DateOnly(2026, 3, 15), new DateOnly(2026, 6, 15), new DateOnly(2026, 9, 15), new DateOnly(2026, 12, 15));
        deadlines.Should().Contain(d => d.Obligation == FiscalObligationDto.CorporateTaxBalance && d.DueDate == new DateOnly(2026, 5, 15));
    }

    [Fact]
    public void Au_trimestre_une_ca3_par_trimestre_civil()
    {
        IReadOnlyList<FiscalDeadlineResponse> deadlines = Year(Input(VatDeclarationRegime.RealNormal, FiscalSettings.Default with { VatPeriodicity = VatPeriodicityDto.Quarterly }));

        deadlines.Where(d => d.Obligation == FiscalObligationDto.VatReturnCa3).Select(d => (d.PeriodStart, d.DueDate)).Should().Equal(
            (new DateOnly(2025, 10, 1), new DateOnly(2026, 1, 15)),
            (new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 15)),
            (new DateOnly(2026, 4, 1), new DateOnly(2026, 7, 15)),
            (new DateOnly(2026, 7, 1), new DateOnly(2026, 10, 15)));
    }

    [Fact]
    public void Au_reel_simplifie_la_ca12_et_ses_acomptes_de_55_et_40_pour_cent()
    {
        FiscalCalendarInput input = Input(VatDeclarationRegime.RealSimplified, vat: new Dictionary<DateOnly, decimal> { [new DateOnly(2025, 12, 31)] = 4000m });

        IReadOnlyList<FiscalDeadlineResponse> deadlines = Year(input);

        // CA12 de 2025 : 1er mai 2026 férié, samedi 2 et dimanche 3 non ouvrés, 2e jour ouvré le mardi 5 mai.
        deadlines.Should().ContainSingle(d => d.Obligation == FiscalObligationDto.VatReturnCa12).Which.DueDate.Should().Be(new DateOnly(2026, 5, 5));
        deadlines.Where(d => d.Obligation == FiscalObligationDto.VatInstallment).Select(d => (d.DueDate, d.Note)).Should().Equal(
            (new DateOnly(2026, 7, 15), "Installment55"), (new DateOnly(2026, 12, 15), "Installment40"));
        deadlines.Should().NotContain(d => d.Obligation == FiscalObligationDto.VatReturnCa3);
        FiscalCalendar.Installments(input, new DateOnly(2026, 1, 1)).Select(i => i.Amount).Should().Equal(2200m, 1600m);
    }

    [Fact]
    public void Sous_mille_euros_de_tva_les_acomptes_sont_dispenses()
    {
        FiscalCalendarInput input = Input(VatDeclarationRegime.RealSimplified, vat: new Dictionary<DateOnly, decimal> { [new DateOnly(2025, 12, 31)] = 600m });

        Year(input).Where(d => d.Obligation == FiscalObligationDto.VatInstallment).Should().OnlyContain(d => d.State == FiscalDeadlineStateDto.Exempt);
        FiscalCalendar.Installments(input, new DateOnly(2026, 1, 1)).Should().OnlyContain(i => i.Exempt && i.Amount == 0m);
    }

    [Fact]
    public void Un_exercice_decale_decale_la_ca12_l_is_et_la_liasse()
    {
        FiscalSettings june = FiscalSettings.Default with { FiscalYearEndMonth = 6, FiscalYearEndDay = 30 };

        IReadOnlyList<FiscalDeadlineResponse> deadlines = Year(Input(VatDeclarationRegime.RealSimplified, june));

        deadlines.Should().Contain(d => d.Obligation == FiscalObligationDto.VatReturnCa12 && d.PeriodEnd == new DateOnly(2026, 6, 30) && d.DueDate == new DateOnly(2026, 9, 30));
        deadlines.Should().Contain(d => d.Obligation == FiscalObligationDto.CorporateTaxBalance && d.PeriodEnd == new DateOnly(2026, 6, 30) && d.DueDate == new DateOnly(2026, 10, 15));
        deadlines.Should().Contain(d => d.Obligation == FiscalObligationDto.TaxReturn && d.DueDate == new DateOnly(2026, 9, 30));
    }

    [Fact]
    public void En_franchise_ni_tva_ni_des()
    {
        IReadOnlyList<FiscalDeadlineResponse> deadlines = Year(Input(null, franchise: true, desMonths: new HashSet<DateOnly> { new(2026, 9, 1) }));

        deadlines.Select(d => d.Obligation).Should().NotContain([FiscalObligationDto.VatReturnCa3, FiscalObligationDto.VatReturnCa12, FiscalObligationDto.Des]);
        deadlines.Should().Contain(d => d.Obligation == FiscalObligationDto.Cfe);
    }

    [Fact]
    public void Proche_dans_les_sept_jours_en_retard_seulement_depuis_le_suivi_et_deposee_une_fois_marquee()
    {
        FiscalSettings tracked = FiscalSettings.Default with { TrackedFrom = new DateOnly(2026, 9, 1), Version = 1 };
        FiscalFiling filed = new(FiscalObligationDto.VatReturnCa3, new DateOnly(2026, 7, 1), new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero), Guid.Empty);
        FiscalCalendarInput input = Input(VatDeclarationRegime.RealNormal, tracked, desMonths: new HashSet<DateOnly> { new(2026, 9, 1) }, filings: [filed]);

        IReadOnlyList<FiscalDeadlineResponse> deadlines = Year(input);

        FiscalDeadlineResponse Ca3(int month) => deadlines.Single(d => d.Obligation == FiscalObligationDto.VatReturnCa3 && d.DueDate.Month == month);
        Ca3(8).State.Should().Be(FiscalDeadlineStateDto.Filed);
        Ca3(8).FiledAt.Should().Be(filed.FiledAt);
        Ca3(7).State.Should().Be(FiscalDeadlineStateDto.Past, "avant le début du suivi, Lettra ne crie pas au retard");
        Ca3(9).State.Should().Be(FiscalDeadlineStateDto.Overdue);
        Ca3(10).State.Should().Be(FiscalDeadlineStateDto.DueSoon, "le 15 octobre est à six jours du 9");
        Ca3(11).State.Should().Be(FiscalDeadlineStateDto.Upcoming);
        deadlines.Single(d => d.Obligation == FiscalObligationDto.Des).Should().Match<FiscalDeadlineResponse>(
            d => d.DueDate == new DateOnly(2026, 10, 12) && d.State == FiscalDeadlineStateDto.DueSoon);
    }
}
