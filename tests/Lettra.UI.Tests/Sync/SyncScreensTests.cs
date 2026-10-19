using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Sync;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Sync;
using Lettra.UI.Shared.Services;
using Lettra.UI.Shared.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Tests.Sync;

/// <summary>Synchronisation simulée : l'adaptateur du SyncCoordinator vit côté poste, l'UI ne voit que ce contrat.</summary>
internal sealed class FakeSyncCenter : ISyncCenter
{
    public SyncSnapshot Current { get; set; } = SyncSnapshot.Initial;

    public SyncMode Mode { get; private set; } = SyncMode.AlwaysAsk;

    public event EventHandler? Changed;

    public int Runs { get; private set; }

    public List<SyncPendingItem> Pending { get; } = [];

    public List<SyncFieldDispute> Conflicts { get; } = [];

    public List<SyncRejectedItem> Rejected { get; } = [];

    public List<IReadOnlyList<SyncFieldChoice>> Resolutions { get; } = [];

    public void Publish(SyncSnapshot snapshot)
    {
        Current = snapshot;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<SyncCycleReport> SyncNowAsync(CancellationToken cancellationToken)
    {
        Runs++;
        SyncCycleReport report = new(SyncStatus.UpToDate, SyncFailureKind.None, Current.Pending, Pending.Count, 0, 0, 3, 0, false,
            new DateTimeOffset(2026, 10, 15, 14, 0, 0, TimeSpan.Zero));
        Pending.Clear();
        Publish(new SyncSnapshot(SyncStatus.UpToDate, SyncPendingSummary.Empty, SyncReminderLevel.None, Conflicts.Count, Rejected.Count, report));
        return Task.FromResult(report);
    }

    public Task SetModeAsync(SyncMode mode, CancellationToken cancellationToken)
    {
        Mode = mode;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SyncPendingItem>> ListPendingAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SyncPendingItem>>([.. Pending]);

    public Task<IReadOnlyList<SyncFieldDispute>> ListConflictsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SyncFieldDispute>>([.. Conflicts]);

    public Task<IReadOnlyList<SyncRejectedItem>> ListRejectedAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SyncRejectedItem>>([.. Rejected]);

    /// <summary>Refus à renvoyer (ex. version du cabinet pas encore reçue), sinon les conflits sont résolus.</summary>
    public string? ResolveError { get; set; }

    public Task<OperationResult<bool>> ResolveAsync(IReadOnlyList<SyncFieldChoice> choices, CancellationToken cancellationToken)
    {
        if (ResolveError is { } code)
        {
            return Task.FromResult(OperationResult<bool>.Failure([new ErrorDetail(code, "Designation")]));
        }

        Resolutions.Add(choices);
        Conflicts.RemoveAll(c => choices.Any(choice => choice.ConflictId == c.Id));
        return Task.FromResult(OperationResult<bool>.Success(true));
    }
}

[Collection("culture")]
public sealed class SyncScreensTests : BunitContext
{
    private static readonly DateTimeOffset Midi = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeSyncCenter _sync = new();

    public SyncScreensTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private void AvecSynchro() => Services.AddSingleton<ISyncCenter>(_sync);

    [Fact]
    public void Sans_synchronisation_la_pastille_et_le_rappel_n_affichent_rien()
    {
        Render<SyncBadge>().Markup.Trim().Should().BeEmpty();
        Render<SyncReminderBanner>().Markup.Trim().Should().BeEmpty();
        Render<SyncPage>().Find("#sync-unavailable").TextContent.Should().Be("Ce poste ne synchronise pas : tout est enregistré directement.");
    }

    [Fact]
    public void La_pastille_suit_l_etat_et_les_saisies_en_attente()
    {
        AvecSynchro();
        IRenderedComponent<SyncBadge> badge = Render<SyncBadge>();
        badge.Find("#sync-badge").TextContent.Should().Contain("Hors ligne");

        _sync.Publish(new SyncSnapshot(SyncStatus.PendingConsent, new SyncPendingSummary(2, 5, 12_000, 2), SyncReminderLevel.None, 1, 0, null));

        badge.WaitForAssertion(() => badge.Find("#sync-badge").TextContent.Should().Contain("En attente de votre accord").And.Contain("9 en attente").And.Contain("1 conflit(s)"));
        badge.Find("#sync-badge").GetAttribute("title").Should().Be("2 facture(s) validée(s), 2 création(s), 5 modification(s) (environ 12 Ko).");
        badge.Find("#sync-badge").ClassList.Should().Contain("is-pendingconsent");
    }

    [Fact]
    public async Task La_demande_part_maintenant_ou_plus_tard_sans_bloquer()
    {
        SyncConsentDialog dialog = Services.GetRequiredService<SyncConsentDialog>();
        IRenderedComponent<SyncConsentPanel> panel = Render<SyncConsentPanel>();
        panel.Markup.Trim().Should().BeEmpty();

        Task<SyncAnswer> premiere = dialog.AskAsync(new SyncPendingSummary(1, 0, 300, 1), SyncNetwork.Cellular, CancellationToken.None);
        panel.WaitForAssertion(() => panel.Find("#sync-prompt-summary").TextContent.Should().Be("1 facture(s) validée(s), 1 création(s) (environ 300 o)."));
        panel.Markup.Should().Contain("Vous êtes sur un réseau mobile.");
        panel.Find("[role=dialog]").GetAttribute("aria-modal").Should().Be("false");
        panel.Find("#sync-later").Click();
        (await premiere).Should().Be(SyncAnswer.Later);
        panel.WaitForAssertion(() => panel.Markup.Trim().Should().BeEmpty());

        Task<SyncAnswer> seconde = dialog.AskAsync(new SyncPendingSummary(0, 1, 2048, 0), SyncNetwork.Wifi, CancellationToken.None);
        panel.WaitForAssertion(() => panel.Find("#sync-now").Click());
        (await seconde).Should().Be(SyncAnswer.Now);
    }

    [Fact]
    public async Task Le_detail_liste_les_saisies_puis_la_demande_reste_ouverte()
    {
        SyncConsentDialog dialog = Services.GetRequiredService<SyncConsentDialog>();
        IRenderedComponent<SyncConsentPanel> panel = Render<SyncConsentPanel>();
        SyncPendingSummary resume = new(1, 0, 300, 1);

        Task<SyncAnswer> question = dialog.AskAsync(resume, SyncNetwork.Wifi, CancellationToken.None);
        panel.WaitForAssertion(() => panel.Find("#sync-detail").Click());
        (await question).Should().Be(SyncAnswer.ShowDetail);

        // Le cycle montre le détail puis repose la question (SyncCoordinator).
        await dialog.ShowDetailAsync(
            [
                new SyncPendingItem(1, SyncItemKind.Customer, SyncChangeKind.Creation, Guid.CreateVersion7(), "Dupont SARL", Midi),
                new SyncPendingItem(2, SyncItemKind.Invoice, SyncChangeKind.Validation, Guid.CreateVersion7(), "FAC-P01-000003", Midi),
            ],
            CancellationToken.None);
        Task<SyncAnswer> reposee = dialog.AskAsync(resume, SyncNetwork.Wifi, CancellationToken.None);

        panel.WaitForAssertion(() => panel.FindAll("#sync-prompt-detail li").Should().HaveCount(2));
        panel.Find("#sync-prompt-detail").TextContent.Should().Contain("Création · Client « Dupont SARL »").And.Contain("Validation · Facture « FAC-P01-000003 »");
        panel.FindAll("#sync-detail").Should().BeEmpty();
        panel.Find("#sync-now").Click();
        (await reposee).Should().Be(SyncAnswer.Now);
    }

    [Fact]
    public async Task Une_demande_abandonnee_par_le_cycle_disparait()
    {
        SyncConsentDialog dialog = Services.GetRequiredService<SyncConsentDialog>();
        IRenderedComponent<SyncConsentPanel> panel = Render<SyncConsentPanel>();
        using CancellationTokenSource cycle = new();

        Task<SyncAnswer> question = dialog.AskAsync(new SyncPendingSummary(0, 1, 10, 0), SyncNetwork.Wifi, cycle.Token);
        panel.WaitForAssertion(() => panel.Find("[role=dialog]"));
        await cycle.CancelAsync();

        await question.Invoking(q => q).Should().ThrowAsync<OperationCanceledException>();
        panel.WaitForAssertion(() => panel.Markup.Trim().Should().BeEmpty());
    }

    [Theory]
    [InlineData(SyncReminderLevel.Day, "status", "plus de 24 h")]
    [InlineData(SyncReminderLevel.ThreeDays, "alert", "ne part pas à la plateforme agréée")]
    public void Le_rappel_monte_d_un_cran_a_72_h(SyncReminderLevel niveau, string role, string texte)
    {
        AvecSynchro();
        _sync.Current = SyncSnapshot.Initial with { Reminder = niveau, Pending = new SyncPendingSummary(0, 0, 400, 1) };

        IRenderedComponent<SyncReminderBanner> banner = Render<SyncReminderBanner>();

        banner.Find("#sync-reminder").GetAttribute("role").Should().Be(role);
        banner.Find("#sync-reminder").TextContent.Should().Contain(texte);
        banner.Find("button").Click();
        _sync.Runs.Should().Be(1);
        banner.WaitForAssertion(() => banner.Markup.Trim().Should().BeEmpty());
    }

    [Fact]
    public void L_ecran_synchronise_liste_l_attente_et_garde_la_preference()
    {
        AvecSynchro();
        _sync.Pending.Add(new SyncPendingItem(1, SyncItemKind.CatalogItem, SyncChangeKind.Change, Guid.CreateVersion7(), "Conseil", Midi));
        _sync.Current = SyncSnapshot.Initial with { Status = SyncStatus.PendingConsent, Pending = new SyncPendingSummary(0, 1, 500, 0) };
        _sync.Rejected.Add(new SyncRejectedItem(2, SyncItemKind.Invoice, SyncChangeKind.Validation, Guid.CreateVersion7(), "FAC-P01-000004", [ErrorCodes.Sync.NumberOutOfOrder]));

        IRenderedComponent<SyncPage> page = Render<SyncPage>();

        page.Find("#sync-pending tbody").TextContent.Should().Contain("Modification").And.Contain("Article « Conseil »");
        page.Find("#sync-rejected tbody").TextContent.Should().Contain("Facture « FAC-P01-000004 »")
            .And.Contain("Une pièce précédente de ce poste manque ou a été refusée : celle-ci attend.");
        page.Find("input[value=AlwaysAsk]").HasAttribute("checked").Should().BeTrue();

        page.Find("input[value=AutoOnWifi]").Change(true);
        _sync.Mode.Should().Be(SyncMode.AutoOnWifi);

        page.Find("#sync-run").Click();
        page.WaitForAssertion(() => page.Find("#sync-last").TextContent.Should().Contain("1 envoyé(s), 3 reçu(s), 0 conflit(s), 0 refusé(s)"));
        page.Find("#sync-status").TextContent.Should().Be("À jour");
        page.Markup.Should().Contain("Rien n'attend sur ce poste.");
    }

    [Fact]
    public void Un_echec_dit_quoi_faire()
    {
        AvecSynchro();
        _sync.Current = SyncSnapshot.Initial with
        {
            Status = SyncStatus.Failed,
            LastReport = new SyncCycleReport(SyncStatus.Failed, SyncFailureKind.Unauthenticated, SyncPendingSummary.Empty, 0, 0, 0, 0, 0, false, Midi),
        };

        IRenderedComponent<SyncPage> page = Render<SyncPage>();

        page.Find("p.validation-message").TextContent.Should().Be("Votre session a expiré : reconnectez-vous, puis relancez la synchronisation.");
    }

    [Fact]
    public void Chaque_champ_dispute_est_tranche_avant_d_appliquer()
    {
        AvecSynchro();
        Guid cle = Guid.CreateVersion7();
        Guid client = Guid.CreateVersion7();
        const long email = 41, ville = 42;
        _sync.Conflicts.Add(new SyncFieldDispute(email, cle, SyncItemKind.Customer, client, "Dupont SARL", "Email", "a@dupont.fr", "b@dupont.fr", "c@dupont.fr", Midi));
        _sync.Conflicts.Add(new SyncFieldDispute(ville, cle, SyncItemKind.Customer, client, "Dupont SARL", "City", "Paris", "Lyon", null, Midi));

        IRenderedComponent<SyncConflicts> page = Render<SyncConflicts>();

        page.Find("h2").TextContent.Should().Be("Client « Dupont SARL »");
        page.Find("tr[data-field=Email] th").TextContent.Should().Be(Services.GetRequiredService<IStringLocalizer<SharedResource>>()["Customers.Field.Email"]);
        page.Find("tr[data-field=City]").TextContent.Should().Contain("(vide)", "la valeur vide du cabinet se lit");
        page.Find("button.apply").HasAttribute("disabled").Should().BeTrue("aucun choix n'est fait à la place de l'utilisateur");

        page.Find("tr[data-field=Email] input.keep-mine").Change(true);
        page.Find("button.apply").HasAttribute("disabled").Should().BeTrue();
        page.Find("tr[data-field=City] input.take-server").Change(true);
        page.Find("button.apply").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Conflit résolu."));
        _sync.Resolutions.Should().ContainSingle().Which.Should().BeEquivalentTo([new SyncFieldChoice(email, true), new SyncFieldChoice(ville, false)]);
        page.Find("#sync-conflicts-empty").Should().NotBeNull();
    }

    [Fact]
    public void Un_refus_de_resolution_s_affiche_et_garde_le_conflit()
    {
        AvecSynchro();
        _sync.ResolveError = ErrorCodes.Sync.EntityNotFound;
        _sync.Conflicts.Add(new SyncFieldDispute(7, Guid.CreateVersion7(), SyncItemKind.CatalogItem, Guid.CreateVersion7(), "Conseil", "Designation", "Conseil", "Conseil fiscal", "Conseil juridique", Midi));
        IRenderedComponent<SyncConflicts> page = Render<SyncConflicts>();

        page.Find("input.take-server").Change(true);
        page.Find("button.apply").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("L'élément modifié sur ce poste n'existe pas sur le serveur."));
        page.FindAll("section.lettra-sync-conflict").Should().ContainSingle();
        this.Toasts().Should().NotContain(t => t.Kind == ToastKind.Success);
    }

    [Fact]
    public void Les_tailles_s_ecrivent_en_octets_ko_et_mo()
    {
        IStringLocalizer<SharedResource> l = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        SyncText.Size(l, 900).Should().Be("900 o");
        SyncText.Size(l, 1025).Should().Be("2 Ko");
        SyncText.Size(l, 3 * 1024 * 1024 / 2).Should().Be("1,5 Mo");
    }
}
