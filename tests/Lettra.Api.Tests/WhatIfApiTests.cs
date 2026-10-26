using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Serialization;
using Lettra.Domain.SharedKernel;

namespace Lettra.Api.Tests;

/// <summary>
/// Le simulateur « et si ? » par l'API réelle (mode 2, SQLite) : un scénario se calcule sur la trésorerie attendue du
/// dossier, un scénario hors bornes est refusé champ par champ, et rien n'est écrit.
/// </summary>
public sealed class WhatIfApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static readonly Uri WhatIf = new("/api/v1/insights/what-if", UriKind.Relative);

    private HttpClient Manager()
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    [Fact]
    public async Task Un_scenario_se_simule_sur_la_tresorerie_attendue_du_dossier()
    {
        using HttpClient manager = Manager();
        await ExportApiTests.ValidatedInvoiceAsync(manager);
        DateOnly today = LegalCalendar.DateOf(DateTimeOffset.UtcNow);

        using HttpResponseMessage baseline = await manager.PostAsJsonAsync(WhatIf, new WhatIfRequest(), ContractsJsonContext.Default.WhatIfRequest);
        using HttpResponseMessage response = await manager.PostAsJsonAsync(
            WhatIf,
            new WhatIfRequest(Invoice: new WhatIfInvoiceRequest(1_000m, 20m, today, 0), Vat: WhatIfVatDto.Franchise),
            ContractsJsonContext.Default.WhatIfRequest);

        baseline.StatusCode.Should().Be(HttpStatusCode.OK, await baseline.Content.ReadAsStringAsync());
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        WhatIfResponse before = (await baseline.Content.ReadFromJsonAsync(ContractsJsonContext.Default.WhatIfResponse))!;
        WhatIfResponse after = (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.WhatIfResponse))!;
        before.Today.Should().Be(today);
        before.Gap.Should().Be(0m);
        after.BaselineIn.Should().Be(before.BaselineIn);
        after.Invoice!.TotalInclTax.Should().Be(1_000m, "en franchise, pas de TVA");
        after.Gap.Should().Be(1_000m);
        after.Weeks.Should().HaveCount(WhatIfRequest.Weeks);
    }

    [Fact]
    public async Task Un_scenario_hors_bornes_est_refuse_champ_par_champ()
    {
        using HttpClient manager = Manager();

        using HttpResponseMessage response = await manager.PostAsJsonAsync(
            WhatIf,
            new WhatIfRequest(ExtraDelayDays: -1, Hire: new WhatIfHireRequest(0m, LegalCalendar.DateOf(DateTimeOffset.UtcNow), 45m)),
            ContractsJsonContext.Default.WhatIfRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(ErrorCodes.Validation.Invalid).And.Contain("ExtraDelayDays").And.Contain("Hire.MonthlyGrossSalary");
    }
}
