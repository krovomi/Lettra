using System.Globalization;
using Bunit;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Reminders;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Insights;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Insights;

/// <summary>Écran « Fiche d'appel » : le client choisi, ce qu'il doit, les points à aborder en phrases et dans l'ordre.</summary>
[Collection("culture")]
public sealed class CallSheetPageTests : BunitContext
{
    private static readonly DateOnly Today = new(2026, 10, 26);
    private static readonly Guid Martin = Guid.CreateVersion7();
    private static readonly Guid Dupont = Guid.CreateVersion7();
    private readonly FakeInsightsGateway _insights = new();

    public CallSheetPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IInsightsGateway>(_insights);
        Services.AddSingleton<TimeProvider>(new ParisClock(new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
        CustomerPaymentWeatherResponse martin = new(Martin, "Boulangerie Martin", PaymentWeatherDto.Stormy, 3, 40, 19, 33, 117, null, 0, 2_300m, []);
        _insights.Weather = new PaymentWeatherResponse(
            Today,
            [
                new(Dupont, "Atelier Dupont", PaymentWeatherDto.Sunny, 4, 0, 0, 100, 0, null, 0, 0m, []),
                new(Guid.CreateVersion7(), "Café Lune", PaymentWeatherDto.Cloudy, 2, 5, 5, 50, 3, null, 0, 150m, []),
                martin,
            ],
            [],
            0m);
        _insights.CallSheets[Martin] = new CallSheetResponse(
            Today,
            Martin,
            "Boulangerie Martin",
            "compta@martin.fr",
            martin,
            2_300m,
            2_000m,
            [
                new(Guid.CreateVersion7(), "FA-2", new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), 1_200m, 117, null, ReminderLevelDto.Final, new DateOnly(2026, 9, 15)),
                new(Guid.CreateVersion7(), "FA-3", new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), 800m, 25, PlatformStatusDto.Disputed, null, null),
                new(Guid.CreateVersion7(), "FA-4", new DateOnly(2026, 10, 16), new DateOnly(2026, 11, 15), 300m, 0, null, null, null),
            ],
            [new(new DateOnly(2026, 9, 15), "FA-2", ReminderLevelDto.Final, ReminderChannelDto.Email)],
            [
                new(CallPointKindDto.Disputed, Number: "FA-3", Amount: 800m),
                new(CallPointKindDto.Doubtful, Count: 1, Days: 117, Amount: 1_200m),
                new(CallPointKindDto.FinalSent, Number: "FA-2", Count: 1),
                new(CallPointKindDto.LastReminder, Number: "FA-2", Date: new DateOnly(2026, 9, 15), Level: ReminderLevelDto.Final),
                new(CallPointKindDto.Indemnity, Count: 2, Amount: 80m),
                new(CallPointKindDto.Habit, Days: 19, Percent: 33),
                new(CallPointKindDto.LastPayment, Amount: 500m, Date: new DateOnly(2026, 6, 20)),
            ]);
    }

    private IRenderedComponent<CallSheetPage> Open(string address)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(address);
        IRenderedComponent<CallSheetPage> page = Render<CallSheetPage>();
        page.WaitForState(() => page.FindAll("#call-customer option").Count > 1);
        return page;
    }

    [Fact]
    public void Sans_client_choisi_la_liste_propose_ceux_qui_doivent_les_plus_en_retard_d_abord()
    {
        IRenderedComponent<CallSheetPage> page = Open("call-sheet");

        page.FindAll("#call-customer option").Skip(1).Select(o => o.TextContent.Split(" · ")[0]).Should().Equal("Boulangerie Martin", "Café Lune");
        page.FindAll("#call-empty").Should().ContainSingle();
        page.FindAll("#call-sheet").Should().BeEmpty();
    }

    [Fact]
    public void La_fiche_dit_les_points_dans_l_ordre_et_le_litige_en_tete()
    {
        IRenderedComponent<CallSheetPage> page = Open($"call-sheet?customer={Martin}");
        page.WaitForState(() => page.FindAll("#call-sheet").Count > 0);

        page.Find("#call-name").TextContent.Should().Be("Boulangerie Martin");
        page.Find("#call-email").GetAttribute("href").Should().Be("mailto:compta@martin.fr");
        page.Find("#call-overdue").TextContent.Should().Contain("000,00");
        string[] points = [.. page.FindAll("#call-points li").Select(l => l.TextContent.Trim().Replace('\u00A0', ' ').Replace('\u202F', ' '))];
        points.Should().HaveCount(7);
        points[0].Should().Be("La facture FA-3 (800,00 €) est contestée à la plateforme : réglez d'abord le litige avant de parler de paiement.");
        points[2].Should().Contain("mise en demeure");
        points[3].Should().Be("Dernière relance : Dernière relance le 15/09/2026, pour la facture FA-2.");
        points[4].Should().Contain("80,00 €").And.Contain("pour 2 factures");
        points[5].Should().Be("D'habitude, ce client paie 19 j après l'échéance ; 33 % de ses factures sont réglées à l'heure.");
        page.FindAll("#call-points li")[0].ClassList.Should().Contain("is-disputed");
        page.FindAll("#call-invoices li").Should().HaveCount(3);
        page.FindAll("#call-invoices li")[0].TextContent.Should().Contain("en retard de 117 j").And.Contain("Dernière relance le 15/09/2026").And.Contain("douteuse");
        page.FindAll("#call-invoices li")[1].TextContent.Should().Contain("En litige");
        page.FindAll("#call-invoices li")[2].TextContent.Should().Contain("échéance le 15/11/2026");
        page.Find("#call-reminders").TextContent.Should().Contain("FA-2").And.Contain("E-mail");
    }

    [Fact]
    public void Choisir_un_client_ouvre_sa_fiche_et_un_client_introuvable_le_dit()
    {
        IRenderedComponent<CallSheetPage> page = Open("call-sheet");

        page.Find("#call-customer").Change(Martin.ToString());

        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith($"call-sheet?customer={Martin}");

        IRenderedComponent<CallSheetPage> unknown = Open($"call-sheet?customer={Guid.CreateVersion7()}");
        unknown.FindAll("#call-sheet").Should().BeEmpty();
        unknown.FindAll("#call-empty").Should().BeEmpty("un client est demandé : la page ne propose pas d'en choisir un autre en grand");
    }
}
