using System.Globalization;
using Bunit;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Tax;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Insights;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Insights;

/// <summary>Écran « Et si… ? » : les leviers, l'effet sur treize semaines, la facture et l'embauche essayées, en bac à sable.</summary>
[Collection("culture")]
public sealed class WhatIfPageTests : BunitContext
{
    private static readonly DateOnly Today = new(2026, 10, 16);
    private readonly FakeInsightsGateway _insights = new();

    public WhatIfPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IInsightsGateway>(_insights);
        Services.AddSingleton<TimeProvider>(new ParisClock(new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
        _insights.WhatIf = Answer;
    }

    private static WhatIfResponse Answer(WhatIfRequest scenario)
    {
        decimal gap = (scenario.Invoice is null ? 0m : 10_000m) - (scenario.Hire is null ? 0m : 4_350m) - (scenario.ExtraDelayDays > 0 ? 1_200m : 0m);
        WhatIfWeekResponse[] weeks = [.. Enumerable.Range(0, WhatIfRequest.Weeks).Select(w => new WhatIfWeekResponse(new DateOnly(2026, 10, 19).AddDays(7 * w), 1_200m, w == 0 ? 0m : 1_200m, 0m, gap))];
        List<WhatIfEffectResponse> effects = [];
        if (scenario.ExtraDelayDays > 0)
        {
            effects.Add(new(WhatIfEffectKindDto.CollectionMoved, new DateOnly(2026, 11, 2), 1_200m, "FA-2026-0007", new DateOnly(2026, 10, 20)));
        }

        WhatIfInvoiceResponse? invoice = scenario.Invoice is { } trial
            ? new(trial.AmountExclTax, 2_000m, 12_000m, new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 15), new DateOnly(2026, 11, 30))
            : null;
        if (invoice is not null)
        {
            effects.Add(new(WhatIfEffectKindDto.VatPaid, new DateOnly(2027, 1, 15), -2_000m));
        }

        WhatIfHireResponse? hire = scenario.Hire is null ? null : new(2_900m, 4_350m, 34_800m);
        return new WhatIfResponse(Today, VatPeriodicityDto.Monthly, WhatIfVatDto.Monthly, 15_600m, 15_600m + gap, 0m, gap, weeks, effects, invoice, hire);
    }

    private IRenderedComponent<WhatIfPage> Open()
    {
        IRenderedComponent<WhatIfPage> page = Render<WhatIfPage>();
        page.WaitForState(() => page.FindAll("#whatif-result").Count > 0);
        return page;
    }

    [Fact]
    public void L_ecran_s_ouvre_sur_la_tresorerie_attendue_sans_scenario()
    {
        IRenderedComponent<WhatIfPage> page = Open();

        _insights.Scenarios.Should().Equal(new WhatIfRequest());
        page.Find("#whatif-sandbox").TextContent.Should().Contain("rien de ce que vous essayez ici n'est écrit");
        page.Find("#whatif-summary").TextContent.Should().Be("Aucun levier n'est choisi : voici la trésorerie attendue telle quelle.");
        page.FindAll("#whatif-weeks li").Should().HaveCount(WhatIfRequest.Weeks);
        page.Find("#whatif-effects-empty").Should().NotBeNull();
        page.Find("#whatif-vat option").TextContent.Should().Be("Celui du dossier (CA3 mensuelle)");
        page.FindAll("#whatif-amount, #whatif-salary").Should().BeEmpty("les leviers facture et embauche sont fermés");
    }

    [Fact]
    public void Facturer_avant_la_cloture_et_embaucher_montre_la_TVA_et_le_cout()
    {
        IRenderedComponent<WhatIfPage> page = Open();

        page.Find("#whatif-invoice-on").Change(true);
        page.Find("#whatif-amount").Change("10000");
        page.Find("#whatif-rate").Change("5.5");
        page.Find("#whatif-before").Click();
        page.Find("#whatif-hire-on").Change(true);
        page.Find("#whatif-salary").Change("2000");
        page.Find("#whatif-vat").Change(nameof(WhatIfVatDto.Quarterly));
        page.Find("#whatif-form").Submit();

        page.WaitForAssertion(() => _insights.Scenarios.Should().HaveCount(2));
        WhatIfRequest sent = _insights.Scenarios[^1];
        sent.Invoice.Should().Be(new WhatIfInvoiceRequest(10_000m, 5.5m, new DateOnly(2026, 10, 31), 30));
        sent.Hire.Should().Be(new WhatIfHireRequest(2_000m, new DateOnly(2026, 11, 1), 45m));
        sent.Vat.Should().Be(WhatIfVatDto.Quarterly);
        page.Find("#whatif-invoice").TextContent.Should()
            .Contain("chiffre d'affaires de Octobre 2026, sur l'exercice clos le 31/12/2026")
            .And.Contain("déclarée pour Octobre 2026 à Décembre 2026 et se paie le 15/01/2027")
            .And.Contain("arrivent vers le 30/11/2026");
        page.Find("#whatif-hire").TextContent.Should().Contain("sur les 13 semaines");
        page.Find("#whatif-gap").TextContent.Should().StartWith("+");
        page.Find("#whatif-summary").TextContent.Should().StartWith("Avec ce scénario, la trésorerie compte").And.Contain("de plus");
        page.Find("#whatif-effects .is-out").TextContent.Should().Contain("-2");
    }

    [Fact]
    public void Un_delai_accorde_montre_l_encaissement_deplace_puis_la_remise_a_zero_l_efface()
    {
        IRenderedComponent<WhatIfPage> page = Open();

        page.Find("#whatif-delay").Change("15");
        page.Find("#whatif-form").Submit();

        page.WaitForAssertion(() => page.Find("#whatif-effects").TextContent.Should().Contain("FA-2026-0007 encaissée plus tard (attendue le 20/10/2026)"));
        page.Find("#whatif-gap").ParentElement!.ClassList.Should().Contain("is-down");
        page.Find("#whatif-effects .is-moved").TextContent.Should().NotStartWith("+", "un encaissement déplacé n'est pas une entrée en plus");
        page.Find("#whatif-summary").TextContent.Should().Contain("de moins");

        page.Find("#whatif-reset").Click();

        page.WaitForAssertion(() => page.FindAll("#whatif-effects-empty").Should().ContainSingle());
        _insights.Scenarios[^1].Should().Be(new WhatIfRequest());
    }

    [Fact]
    public void Le_dernier_resultat_reste_quand_un_scenario_est_refuse()
    {
        IRenderedComponent<WhatIfPage> page = Open();
        _insights.WhatIf = _ => null;

        page.Find("#whatif-delay").Change("15");
        page.Find("#whatif-form").Submit();

        page.WaitForAssertion(() => _insights.Scenarios.Should().HaveCount(2));
        page.FindAll("#whatif-result").Should().ContainSingle();
    }
}
