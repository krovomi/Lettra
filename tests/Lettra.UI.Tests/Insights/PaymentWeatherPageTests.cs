using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Insights;
using Lettra.UI.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Insights;

/// <summary>Écran de la météo des paiements : chiffres clés, prévision par semaine, bulletins filtrés et triés.</summary>
[Collection("culture")]
public sealed class PaymentWeatherPageTests : BunitContext
{
    private static readonly DateOnly Today = new(2026, 10, 16);
    private readonly FakeInsightsGateway _insights = new();

    public PaymentWeatherPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IInsightsGateway>(_insights);
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<PaymentWeatherPage> Open()
    {
        IRenderedComponent<PaymentWeatherPage> page = Render<PaymentWeatherPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    private static CustomerPaymentWeatherResponse Customer(
        string name, PaymentWeatherDto weather, int average = 0, int maxOverdue = 0, DayOfWeek? bestDay = null, params ExpectedCollectionResponse[] expected) =>
        new(Guid.CreateVersion7(), name, weather, weather == PaymentWeatherDto.Unknown ? 1 : 4, average, average, 75, maxOverdue, bestDay, bestDay is null ? 0 : 6,
            expected.Sum(e => e.OutstandingInclTax), expected);

    private static ExpectedCollectionResponse Expected(string number, decimal amount, int inDays) =>
        new(Guid.CreateVersion7(), number, Today.AddDays(inDays - 5), amount, Today.AddDays(inDays));

    private static PaymentWeatherResponse Weather(params CustomerPaymentWeatherResponse[] customers)
    {
        DateOnly monday = new(2026, 10, 12);
        ExpectedCollectionResponse[] all = [.. customers.SelectMany(c => c.Expected)];
        ExpectedCollectionResponse[] counted = [.. all.Where(e => !e.Doubtful)];
        return new PaymentWeatherResponse(
            Today,
            customers,
            [
                .. Enumerable.Range(0, PaymentWeatherResponse.ForecastWeeks).Select(w =>
                {
                    ExpectedCollectionResponse[] inWeek = [.. counted.Where(e => e.ExpectedDate >= monday.AddDays(7 * w) && e.ExpectedDate < monday.AddDays(7 * (w + 1)))];
                    return new CollectionForecastWeekResponse(monday.AddDays(7 * w), inWeek.Sum(e => e.OutstandingInclTax), inWeek.Length);
                }),
            ],
            750m,
            all.Where(e => e.Doubtful).Sum(e => e.OutstandingInclTax));
    }

    [Fact]
    public void Les_bulletins_disent_la_meteo_sa_raison_le_meilleur_jour_et_les_encaissements_attendus()
    {
        _insights.Weather = Weather(
            Customer("Hôtel Lefèvre", PaymentWeatherDto.Stormy, average: 12, maxOverdue: 75, expected: [Expected("FAC-2026-0009", 900m, 1), Expected("FAC-2026-0003", 80m, 1) with { Doubtful = true }]),
            Customer("Dupont SARL", PaymentWeatherDto.Sunny, bestDay: DayOfWeek.Tuesday, expected: Expected("FAC-2026-0011", 300m, 10)),
            Customer("Marie Durand", PaymentWeatherDto.Unknown));

        IRenderedComponent<PaymentWeatherPage> page = Open();

        page.Find("#weather-stormy").TextContent.Should().Be("1");
        page.Find("#weather-sunny").TextContent.Should().Be("1");
        page.Find("#weather-four-weeks").TextContent.Should().Contain("200,00");
        page.Find("#weather-eight-weeks").TextContent.Should().Contain("200,00");
        page.FindAll("#weather-weeks li").Should().HaveCount(PaymentWeatherResponse.ForecastWeeks);
        page.Find("#weather-weeks li").ClassList.Should().Contain("is-current");
        page.Find("#weather-weeks li").TextContent.Should().Contain("Semaine du 12/10/2026").And.Contain("1 facture(s)");
        page.FindAll("#weather-weeks .lt-weather-bar > span").Select(b => b.GetAttribute("style")).Should().Contain("width:100%").And.Contain("width:33.3%", "le style CSS ne dépend pas de la langue");
        page.Find("#weather-later").TextContent.Should().Contain("750,00");
        page.Find("#weather-doubtful").TextContent.Should().Contain("80,00").And.Contain("60 jours");
        page.Markup.Should().Contain("douteuse, hors prévision");

        page.FindAll("#weather-cards > li").Should().HaveCount(3);
        page.Find("#weather-cards > li").ClassList.Should().Contain("is-stormy", "l'orage d'abord");
        page.Markup.Should().Contain("Une facture attend son paiement depuis 75 jours après l'échéance.")
            .And.Contain("Paie en moyenne à l'échéance.")
            .And.Contain("le mardi (payé 6 j plus tôt)")
            .And.Contain("Moins de deux factures payées ou échues")
            .And.Contain("FAC-2026-0011").And.Contain("attendu le 26/10/2026");
    }

    [Fact]
    public void Les_bulletins_se_cherchent_se_filtrent_et_se_trient()
    {
        _insights.Weather = Weather(
            Customer("Hôtel Lefèvre", PaymentWeatherDto.Rainy, average: 20),
            Customer("Dupont SARL", PaymentWeatherDto.Sunny),
            Customer("Atelier Bernard", PaymentWeatherDto.Cloudy, average: 6));

        IRenderedComponent<PaymentWeatherPage> page = Open();

        page.Find("#weather-count").TextContent.Should().Be("3 client(s) sur 3");
        page.Find("#weather-search").Input("hotel");
        page.Find("#weather-count").TextContent.Should().Be("1 client(s) sur 3");
        page.Find("#weather-filters-reset").Click();
        page.Find("#weather-filter").Change("Sunny");
        page.FindAll("#weather-cards > li").Should().ContainSingle().Which.TextContent.Should().Contain("Dupont SARL");
        page.Find("#weather-filter").Change(string.Empty);
        page.Find("#weather-sort").Change("Customer:Ascending");
        page.FindAll("#weather-cards h3").Select(h => h.TextContent).Should().Equal("Atelier Bernard", "Dupont SARL", "Hôtel Lefèvre");
        page.Find("#weather-search").Input("personne");
        page.FindAll("#weather-nomatch").Should().ContainSingle();
    }

    [Fact]
    public void Un_dossier_sans_historique_montre_un_etat_vide_et_une_erreur_s_affiche()
    {
        _insights.Weather = Weather();
        Open().FindAll("#weather-empty").Should().ContainSingle();

        _insights.Weather = null;
        IRenderedComponent<PaymentWeatherPage> failed = Open();
        failed.FindAll("#weather-empty").Should().ContainSingle();
        this.Toasts().Should().ContainSingle().Which.Kind.Should().Be(ToastKind.Error);
    }

    [Fact]
    public void Au_dela_de_vingt_quatre_clients_les_bulletins_se_paginent()
    {
        _insights.Weather = Weather([.. Enumerable.Range(1, 30).Select(i => Customer($"Client {i:00}", PaymentWeatherDto.Sunny))]);

        IRenderedComponent<PaymentWeatherPage> page = Open();

        page.FindAll("#weather-cards > li").Should().HaveCount(24);
        page.FindAll("#weather-pager button")[1].Click();
        page.FindAll("#weather-cards > li").Should().HaveCount(6);
    }

    private sealed class FakeInsightsGateway : IInsightsGateway
    {
        public PaymentWeatherResponse? Weather { get; set; }

        public Task<OperationResult<PaymentWeatherResponse>> GetPaymentWeatherAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Weather is null
                ? OperationResult<PaymentWeatherResponse>.Failure([new ErrorDetail(ErrorCodes.Tenancy.DossierNotFound)])
                : OperationResult<PaymentWeatherResponse>.Success(Weather));
    }
}
