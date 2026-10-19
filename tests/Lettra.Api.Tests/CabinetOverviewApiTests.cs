using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>
/// Vue cabinet (vague 6) : le tableau de supervision ne montre que les dossiers ouverts à l'utilisateur et se lit dans le
/// catalogue après un calcul ; la revue de dossier se coche par période, avec qui et quand.
/// </summary>
public sealed class CabinetOverviewApiTests
{
    private static readonly Uri Overview = new("/api/v1/dossiers/overview", UriKind.Relative);
    private static readonly Uri Refresh = new("/api/v1/dossiers/overview/refresh", UriKind.Relative);

    [Fact]
    public async Task Le_tableau_liste_les_dossiers_accessibles_et_se_remplit_au_recalcul()
    {
        await using LettraApiFactory api = new();
        HttpClient manager = api.ClientFor(LettraApiFactory.ManagerId);

        CabinetOverviewResponse before = (await manager.GetFromJsonAsync(Overview, ContractsJsonContext.Default.CabinetOverviewResponse))!;
        before.Dossiers.Should().ContainSingle(d => d.DossierId == LettraApiFactory.DossierId)
            .Which.ComputedAt.Should().BeNull("rien n'a encore été calculé");

        using HttpResponseMessage refreshed = await manager.PostAsync(Refresh, null);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());
        CabinetOverviewRefreshResponse outcome = (await refreshed.Content.ReadFromJsonAsync(ContractsJsonContext.Default.CabinetOverviewRefreshResponse))!;
        outcome.Refreshed.Should().BeGreaterThanOrEqualTo(1);
        outcome.Failed.Should().Be(0);

        DossierOverviewRow row = (await manager.GetFromJsonAsync(Overview, ContractsJsonContext.Default.CabinetOverviewResponse))!
            .Dossiers.Single(d => d.DossierId == LettraApiFactory.DossierId);
        row.ComputedAt.Should().NotBeNull();
        row.Indicators.Select(i => i.Key).Should().Contain(
            [DossierIndicatorKeys.OverdueInvoices, DossierIndicatorKeys.PlatformRejections, DossierIndicatorKeys.ReviewPending]);
        row.Indicators.Single(i => i.Key == DossierIndicatorKeys.OverdueInvoices).Count.Should().Be(0, "le dossier n'a aucune pièce");
        row.Indicators.Single(i => i.Key == DossierIndicatorKeys.ReviewPending).Count.Should().Be(8, "la revue du mois dernier est à faire");

        HttpClient collaborateur = api.ClientFor(LettraApiFactory.CollaborateurId);
        (await collaborateur.GetFromJsonAsync(Overview, ContractsJsonContext.Default.CabinetOverviewResponse))!
            .Dossiers.Select(d => d.DossierId).Should().Equal(LettraApiFactory.DossierId);

        HttpClient other = api.ClientFor(LettraApiFactory.AutreCabinetAdminId);
        (await other.GetFromJsonAsync(Overview, ContractsJsonContext.Default.CabinetOverviewResponse))!
            .Dossiers.Should().NotContain(d => d.DossierId == LettraApiFactory.DossierId, "un autre cabinet ne voit pas ce dossier");
    }

    [Fact]
    public async Task La_revue_se_coche_se_decoche_et_se_retrouve_dans_le_tableau()
    {
        await using LettraApiFactory api = new();
        HttpClient manager = InDossier(api, LettraApiFactory.ManagerId);
        string period = PreviousMonth();

        DossierReviewResponse empty = (await manager.GetFromJsonAsync(
            new Uri($"/api/v1/review/{period}", UriKind.Relative), ContractsJsonContext.Default.DossierReviewResponse))!;
        empty.Period.Should().Be(period);
        empty.Items.Should().HaveCount(8).And.OnlyContain(i => !i.Checked);

        using HttpResponseMessage put = await manager.PutAsJsonAsync(
            new Uri($"/api/v1/review/{period}/items/banque.rapprochee", UriKind.Relative),
            new SetDossierReviewItemRequest { Checked = true, Note = "  Relevé de fin de mois rapproché.  " },
            ContractsJsonContext.Default.SetDossierReviewItemRequest);
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        DossierReviewItemDto ticked = (await put.Content.ReadFromJsonAsync(ContractsJsonContext.Default.DossierReviewResponse))!
            .Items.Single(i => i.Key == "banque.rapprochee");
        ticked.Checked.Should().BeTrue();
        ticked.CheckedByMe.Should().BeTrue();
        ticked.CheckedAt.Should().NotBeNull();
        ticked.Note.Should().Be("Relevé de fin de mois rapproché.");

        (await manager.PostAsync(Refresh, null)).EnsureSuccessStatusCode();
        (await api.ClientFor(LettraApiFactory.ManagerId).GetFromJsonAsync(Overview, ContractsJsonContext.Default.CabinetOverviewResponse))!
            .Dossiers.Single(d => d.DossierId == LettraApiFactory.DossierId)
            .Indicators.Single(i => i.Key == DossierIndicatorKeys.ReviewPending).Count.Should().Be(7);

        // Un collaborateur du dossier voit qui a coché ; décocher efface la coche.
        HttpClient collaborateur = InDossier(api, LettraApiFactory.CollaborateurId);
        DossierReviewItemDto seen = (await collaborateur.GetFromJsonAsync(
            new Uri($"/api/v1/review/{period}", UriKind.Relative), ContractsJsonContext.Default.DossierReviewResponse))!
            .Items.Single(i => i.Key == "banque.rapprochee");
        seen.Checked.Should().BeTrue();
        seen.CheckedByMe.Should().BeFalse();

        using HttpResponseMessage unticked = await collaborateur.PutAsJsonAsync(
            new Uri($"/api/v1/review/{period}/items/banque.rapprochee", UriKind.Relative),
            new SetDossierReviewItemRequest { Checked = false },
            ContractsJsonContext.Default.SetDossierReviewItemRequest);
        unticked.StatusCode.Should().Be(HttpStatusCode.OK, await unticked.Content.ReadAsStringAsync());
        (await unticked.Content.ReadFromJsonAsync(ContractsJsonContext.Default.DossierReviewResponse))!
            .Items.Should().OnlyContain(i => !i.Checked);
    }

    [Fact]
    public async Task La_revue_refuse_une_periode_ou_un_point_inconnus_et_le_portail_client()
    {
        await using LettraApiFactory api = new();
        HttpClient manager = InDossier(api, LettraApiFactory.ManagerId);

        using HttpResponseMessage badPeriod = await manager.GetAsync(new Uri("/api/v1/review/2026-13", UriKind.Relative));
        badPeriod.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badPeriod.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Review.PeriodInvalid);

        using HttpResponseMessage unknown = await manager.PutAsJsonAsync(
            new Uri("/api/v1/review/2026-T3/items/inconnu", UriKind.Relative),
            new SetDossierReviewItemRequest { Checked = true },
            ContractsJsonContext.Default.SetDossierReviewItemRequest);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using HttpResponseMessage tooLong = await manager.PutAsJsonAsync(
            new Uri("/api/v1/review/2026-T3/items/pa.suivie", UriKind.Relative),
            new SetDossierReviewItemRequest { Checked = true, Note = new string('x', SetDossierReviewItemRequest.MaxNoteLength + 1) },
            ContractsJsonContext.Default.SetDossierReviewItemRequest);
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using HttpResponseMessage portal = await InDossier(api, LettraApiFactory.PortailId).PutAsJsonAsync(
            new Uri("/api/v1/review/2026-T3/items/pa.suivie", UriKind.Relative),
            new SetDossierReviewItemRequest { Checked = true },
            ContractsJsonContext.Default.SetDossierReviewItemRequest);
        portal.StatusCode.Should().Be(HttpStatusCode.Forbidden, "le client du portail ne coche pas la revue du cabinet");
    }

    private static HttpClient InDossier(LettraApiFactory api, Guid user)
    {
        HttpClient client = api.ClientFor(user);
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    private static string PreviousMonth()
    {
        DateTime today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris")).DateTime;
        DateTime previous = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
        return $"{previous:yyyy-MM}";
    }
}
