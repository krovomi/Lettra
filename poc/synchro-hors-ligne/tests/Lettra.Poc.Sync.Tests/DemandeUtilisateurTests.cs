using Lettra.Poc.Sync.Client;

namespace Lettra.Poc.Sync.Tests;

/// <summary>Critère : au retour du réseau, l'application demande avant de synchroniser.</summary>
public sealed class DemandeUtilisateurTests : IDisposable
{
    private readonly World _world = new();
    public void Dispose() => _world.Dispose();

    private TestDevice PosteAvecTravailHorsLigne()
    {
        var poste = _world.Maui("P01");
        poste.Network.Online = false;
        for (var i = 0; i < 3; i++) poste.Invoicing.Validate(poste.Invoicing.CreateDraft($"Client {i}", Lines.Audit()));
        poste.Invoicing.CreateDraft("Brouillon", Lines.Audit());
        return poste;
    }

    [Fact]
    public async Task Au_retour_du_reseau_l_utilisateur_est_consulte_et_plus_tard_n_envoie_rien()
    {
        var poste = PosteAvecTravailHorsLigne();
        poste.Consent.Answer(SyncConsent.Later);

        var report = await poste.ReconnectAsync();

        Assert.Equal(SyncState.PendingConsent, report.State);
        var (summary, network) = Assert.Single(poste.Consent.Asked);
        Assert.Equal("3 factures validées, 1 modification de brouillon", summary.Describe());
        Assert.Equal(NetworkKind.Wifi, network);
        Assert.Equal(0, _world.State.EffectiveWrites);
        Assert.DoesNotContain(poste.Network.Log, p => p.StartsWith("/api/sync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Voir_le_detail_puis_synchroniser_maintenant()
    {
        var poste = PosteAvecTravailHorsLigne();
        poste.Consent.Answer(SyncConsent.ShowDetail, SyncConsent.Now);

        var report = await poste.ReconnectAsync();

        Assert.Equal(SyncState.UpToDate, report.State);
        var detail = Assert.Single(poste.Consent.DetailsShown);
        Assert.Contains(detail, d => d.Description == "Facture validée F-2026-P01-000001");
        Assert.Contains(detail, d => d.Description == "Nouveau brouillon pour Brouillon");
        Assert.Equal(2, poste.Consent.Asked.Count);
    }

    [Fact]
    public async Task Portail_captif_d_hotel_ce_n_est_pas_du_reseau()
    {
        var poste = PosteAvecTravailHorsLigne();
        poste.Network.CutOnce("/api/health", Fault.CaptivePortal);

        var report = await poste.ReconnectAsync();

        Assert.Equal(SyncState.Offline, report.State);
        Assert.Empty(poste.Consent.Asked);
    }

    [Fact]
    public async Task Preference_automatique_en_wifi_mais_question_en_reseau_mobile()
    {
        var poste = PosteAvecTravailHorsLigne();
        poste.Preference = SyncPreference.AutoOnWifi;

        poste.Network.Kind = NetworkKind.Cellular;
        poste.Consent.Answer(SyncConsent.Later);
        await poste.ReconnectAsync();
        Assert.Single(poste.Consent.Asked);
        Assert.Equal(0, _world.State.EffectiveWrites);

        poste.Network.Kind = NetworkKind.Wifi;
        var report = await poste.ReconnectAsync();
        Assert.Single(poste.Consent.Asked);   // pas de nouvelle question
        Assert.Equal(SyncState.UpToDate, report.State);
    }

    [Fact]
    public async Task Preference_jamais_en_reseau_mobile_ni_question_ni_envoi()
    {
        var poste = PosteAvecTravailHorsLigne();
        poste.Preference = SyncPreference.NeverOnCellular;
        poste.Network.Kind = NetworkKind.Cellular;

        var report = await poste.ReconnectAsync();

        Assert.Equal(SyncState.PendingConsent, report.State);
        Assert.Empty(poste.Consent.Asked);
        Assert.Equal(0, _world.State.EffectiveWrites);
    }

    [Fact]
    public async Task Rien_a_envoyer_pas_de_question_on_recoit_seulement()
    {
        var poste = _world.Maui("P01");
        var autre = _world.Maui("P02");
        autre.Invoicing.CreateDraft("Créé ailleurs", Lines.Audit());
        await autre.ReconnectAsync();

        var report = await poste.ReconnectAsync();

        Assert.Empty(poste.Consent.Asked);
        Assert.Equal(1, report.Pulled);
    }

    [Fact]
    public async Task Le_rappel_monte_si_les_factures_validees_attendent()
    {
        var poste = PosteAvecTravailHorsLigne();
        poste.Consent.Answer(SyncConsent.Later, SyncConsent.Later);
        await poste.ReconnectAsync();
        Assert.Equal(ReminderLevel.None, poste.Sync.Reminder());

        _world.Clock.Advance(TimeSpan.FromHours(25));
        Assert.Equal(ReminderLevel.Banner, poste.Sync.Reminder());

        _world.Clock.Advance(TimeSpan.FromHours(48));
        Assert.Equal(ReminderLevel.Alert, poste.Sync.Reminder());
    }
}
