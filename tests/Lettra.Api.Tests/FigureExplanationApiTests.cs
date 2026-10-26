using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>
/// « Pourquoi ce chiffre ? » par l'API réelle (mode 2, SQLite) : l'explication retombe exactement sur le chiffre de
/// l'accueil qu'elle explique (chiffre d'affaires du mois, montant en retard) et liste la pièce qui vient d'être validée.
/// </summary>
public sealed class FigureExplanationApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static Uri Api(string path) => new(path, UriKind.Relative);

    private HttpClient Manager()
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    private static async Task<FigureExplanationResponse> WhyAsync(HttpClient client, string query)
    {
        using HttpResponseMessage response = await client.GetAsync(Api($"/api/v1/insights/why?{query}"));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.FigureExplanationResponse))!;
    }

    [Fact]
    public async Task L_explication_retombe_sur_les_chiffres_de_l_accueil()
    {
        using HttpClient manager = Manager();
        Guid invoiceId = await ExportApiTests.ValidatedInvoiceAsync(manager);

        FigureExplanationResponse revenue = await WhyAsync(manager, "figure=MonthRevenue");
        FigureExplanationResponse overdue = await WhyAsync(manager, "figure=Overdue");
        BillingDashboardResponse dashboard = (await manager.GetFromJsonAsync(Api("/api/v1/billing/dashboard"), ContractsJsonContext.Default.BillingDashboardResponse))!;
        ReceivablesSummaryResponse receivables = (await manager.GetFromJsonAsync(Api("/api/v1/receivables/summary"), ContractsJsonContext.Default.ReceivablesSummaryResponse))!;

        revenue.Value.Should().Be(dashboard.Months[^1].ExclTax);
        revenue.Month.Should().Be(dashboard.Months[^1].Month);
        revenue.Lines.Should().Contain(l => l.DocumentId == invoiceId && l.Kind == ExplanationLineKindDto.Invoice);
        revenue.Lines.Sum(l => l.Amount).Should().Be(revenue.Value);
        overdue.Value.Should().Be(receivables.OverdueInclTax);
        overdue.Lines.Should().HaveCount(receivables.OverdueCount);
    }

    [Fact]
    public async Task Un_mois_passe_s_explique_et_un_chiffre_inconnu_est_refuse()
    {
        using HttpClient manager = Manager();

        FigureExplanationResponse past = await WhyAsync(manager, "figure=MonthRevenue&month=2001-02-10");
        using HttpResponseMessage unknown = await manager.GetAsync(Api("/api/v1/insights/why?figure=42"));

        past.Month.Should().Be(new DateOnly(2001, 2, 1));
        past.Lines.Should().BeEmpty();
        past.Value.Should().Be(0m);
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Validation.Invalid);
    }
}
