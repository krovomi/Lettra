using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Lettra.Application.Abstractions.Mail;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Serialization;
using Lettra.Domain.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Radar de prévention par l'API réelle (mode 2, SQLite) : réservé au cabinet (le client du portail n'en voit rien, la
/// carte de l'accueil lui revient cachée sans refus), seuils réglés par le manager seul, courrier préparé puis envoyé
/// par un port e-mail qui garde les messages.
/// </summary>
public sealed class PreventionApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static ContractsJsonContext Json => ContractsJsonContext.Default;

    private static HttpClient InDossier(HttpClient client, Guid? user = null)
    {
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        if (user is Guid id)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", LettraApiFactory.TokenFor(id));
        }

        return client;
    }

    [Fact]
    public async Task Le_cabinet_lit_le_radar_avec_un_signal_par_regle_et_ses_seuils()
    {
        using HttpClient collaborateur = InDossier(factory.CreateClient(), LettraApiFactory.CollaborateurId);

        using HttpResponseMessage response = await collaborateur.GetAsync("/api/v1/insights/prevention");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        PreventionRadarResponse radar = (await response.Content.ReadFromJsonAsync(Json.PreventionRadarResponse))!;
        radar.Signals.Select(s => s.Signal).Should().Equal(Enum.GetValues<PreventionSignalDto>());
        radar.Signals.Should().OnlyContain(s => s.State == PreventionSignalStateDto.Disabled || s.Figures.Count > 0 || s.Unavailable != PreventionUnavailableDto.None);
        radar.ActiveSignals.Should().Be(radar.Signals.Count(s => s.IsActive));
        radar.CanAdjust.Should().BeFalse("le collaborateur lit le radar sans régler les seuils");
    }

    [Fact]
    public async Task Le_client_du_portail_n_a_ni_le_radar_ni_la_carte_mais_l_accueil_ne_refuse_rien()
    {
        using HttpClient portail = InDossier(factory.CreateClient(), LettraApiFactory.PortailId);

        using HttpResponseMessage radar = await portail.GetAsync("/api/v1/insights/prevention");
        using HttpResponseMessage summary = await portail.GetAsync("/api/v1/insights/prevention/summary");
        using HttpResponseMessage letter = await portail.GetAsync("/api/v1/insights/prevention/letter");

        radar.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        letter.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        summary.StatusCode.Should().Be(HttpStatusCode.OK, "la carte de l'accueil ne doit pas laisser de refus dans la console");
        (await summary.Content.ReadFromJsonAsync(Json.PreventionSummaryResponse)).Should().BeEquivalentTo(PreventionSummaryResponse.Hidden);
    }

    [Fact]
    public async Task La_carte_de_l_accueil_resume_le_radar_pour_le_cabinet()
    {
        using HttpClient manager = InDossier(factory.CreateClient());

        PreventionSummaryResponse summary = (await manager.GetFromJsonAsync("/api/v1/insights/prevention/summary", Json.PreventionSummaryResponse))!;

        summary.Visible.Should().BeTrue();
        summary.ActiveSignals.Should().Be(summary.Active.Count);
    }

    [Fact]
    public async Task Le_manager_regle_les_seuils_et_le_radar_les_applique_aussitot()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        PreventionRadarResponse before = (await manager.GetFromJsonAsync("/api/v1/insights/prevention", Json.PreventionRadarResponse))!;
        before.CanAdjust.Should().BeTrue();
        UpdatePreventionSettingsRequest request = UpdatePreventionSettingsRequest.From(before.Settings);
        request.CashBalance = 12_500m;
        request.CashBalanceOn = before.Today;
        request.TreasuryHorizonWeeks = 6;
        request.Disabled = [PreventionSignalDto.PeerGap];

        using HttpResponseMessage saved = await manager.PutAsJsonAsync("/api/v1/insights/prevention/settings", request, Json.UpdatePreventionSettingsRequest);

        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        PreventionSettingsResponse settings = (await saved.Content.ReadFromJsonAsync(Json.PreventionSettingsResponse))!;
        settings.Version.Should().Be(before.Settings.Version + 1);
        PreventionRadarResponse after = (await manager.GetFromJsonAsync("/api/v1/insights/prevention", Json.PreventionRadarResponse))!;
        PreventionSignalResponse treasury = after.Signals.Single(s => s.Signal == PreventionSignalDto.Treasury);
        treasury.State.Should().NotBe(PreventionSignalStateDto.NotEvaluated);
        treasury.Weeks.Should().HaveCount(6);
        after.Signals.Single(s => s.Signal == PreventionSignalDto.PeerGap).State.Should().Be(PreventionSignalStateDto.Disabled);

        // Une seconde modification sur la version déjà dépassée est refusée.
        using HttpResponseMessage stale = await manager.PutAsJsonAsync("/api/v1/insights/prevention/settings", request, Json.UpdatePreventionSettingsRequest);
        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Les_seuils_hors_bornes_sont_refuses_et_le_collaborateur_ne_les_regle_pas()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        using HttpClient collaborateur = InDossier(factory.CreateClient(), LettraApiFactory.CollaborateurId);
        PreventionRadarResponse radar = (await manager.GetFromJsonAsync("/api/v1/insights/prevention", Json.PreventionRadarResponse))!;
        UpdatePreventionSettingsRequest request = UpdatePreventionSettingsRequest.From(radar.Settings);

        request.CallSignalCount = 1;
        using HttpResponseMessage invalid = await manager.PutAsJsonAsync("/api/v1/insights/prevention/settings", request, Json.UpdatePreventionSettingsRequest);
        request.CallSignalCount = 3;
        using HttpResponseMessage forbidden = await collaborateur.PutAsJsonAsync("/api/v1/insights/prevention/settings", request, Json.UpdatePreventionSettingsRequest);

        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Prevention.ThresholdInvalid);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Le_courrier_se_prepare_dans_la_langue_demandee_et_part_par_e_mail()
    {
        CapturedMail mail = new();
        using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(mail)));
        using HttpClient collaborateur = InDossier(host.CreateClient(), LettraApiFactory.CollaborateurId);

        PreventionLetterResponse letter = (await collaborateur.GetFromJsonAsync("/api/v1/insights/prevention/letter?language=en-US", Json.PreventionLetterResponse))!;
        using HttpResponseMessage sent = await collaborateur.PostAsJsonAsync(
            "/api/v1/insights/prevention/letter",
            new SendPreventionLetterRequest { To = "dirigeant@client.test", Subject = letter.Subject, Body = letter.Body + "\n\nPS : à bientôt." },
            Json.SendPreventionLetterRequest);
        using HttpResponseMessage invalid = await collaborateur.PostAsJsonAsync(
            "/api/v1/insights/prevention/letter", new SendPreventionLetterRequest { To = "pas-un-mail", Subject = "", Body = "" }, Json.SendPreventionLetterRequest);

        letter.Language.Should().Be("en-US");
        letter.CanSend.Should().BeTrue();
        letter.Subject.Should().StartWith("Meeting proposal");
        sent.StatusCode.Should().Be(HttpStatusCode.NoContent, await sent.Content.ReadAsStringAsync());
        EmailMessage message = mail.Sent.Should().ContainSingle().Subject;
        message.To.Value.Should().Be("dirigeant@client.test");
        message.TextBody.Should().EndWith("PS : à bientôt.");
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        string problem = await invalid.Content.ReadAsStringAsync();
        problem.Should().Contain(ErrorCodes.Prevention.SubjectRequired).And.Contain(ErrorCodes.Prevention.BodyRequired);
    }

    [Fact]
    public async Task Sans_serveur_d_e_mail_le_courrier_se_copie_a_la_main()
    {
        using HttpClient manager = InDossier(factory.CreateClient());

        PreventionLetterResponse letter = (await manager.GetFromJsonAsync("/api/v1/insights/prevention/letter", Json.PreventionLetterResponse))!;
        using HttpResponseMessage sent = await manager.PostAsJsonAsync(
            "/api/v1/insights/prevention/letter", new SendPreventionLetterRequest { To = "dirigeant@client.test", Subject = letter.Subject, Body = letter.Body }, Json.SendPreventionLetterRequest);

        letter.Language.Should().Be("fr-FR");
        letter.CanSend.Should().BeFalse();
        sent.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await sent.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Email.NotConfigured);
    }

    [Fact]
    public async Task La_vue_cabinet_montre_le_radar_au_cabinet_apres_actualisation()
    {
        using HttpClient manager = factory.CreateClient();

        using HttpResponseMessage refreshed = await manager.PostAsync("/api/v1/dossiers/overview/refresh", null);
        CabinetOverviewResponse overview = (await manager.GetFromJsonAsync("/api/v1/dossiers/overview", Json.CabinetOverviewResponse))!;

        refreshed.IsSuccessStatusCode.Should().BeTrue(await refreshed.Content.ReadAsStringAsync());
        overview.Dossiers.Single(d => d.DossierId == LettraApiFactory.DossierId).Indicators.Should().Contain(i => i.Key == DossierIndicatorKeys.Prevention);
    }

    private sealed class CapturedMail : IEmailSender
    {
        public bool IsConfigured => true;

        public ConcurrentQueue<EmailMessage> Sent { get; } = new();

        public Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Sent.Enqueue(message);
            return Task.FromResult(Result.Success());
        }
    }
}
