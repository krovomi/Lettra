using System.Globalization;
using Bunit;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Banking;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Banking;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Banking;

/// <summary>Banque connectée : relier un compte, la page de la banque simulée, le retour, la synchronisation, la déconnexion.</summary>
[Collection("culture")]
public sealed class BankConnectionsTests : BunitContext
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeBankConnectionsGateway _bank = new() { Available = true };

    public BankConnectionsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IBankConnectionsGateway>(_bank);
        Services.AddSingleton<TimeProvider>(new ManualClock(Now));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void Sans_agregateur_le_panneau_ne_s_affiche_pas()
    {
        _bank.Available = false;

        IRenderedComponent<BankConnectionsPanel> panel = Render<BankConnectionsPanel>();

        panel.FindAll("#bank-connections").Should().BeEmpty();
        _bank.Syncs.Should().BeEmpty();
    }

    [Fact]
    public void Connecter_un_compte_mene_a_la_page_de_la_banque()
    {
        IRenderedComponent<BankConnectionsPanel> panel = Render<BankConnectionsPanel>();
        panel.Find("#bank-connections-provider").TextContent.Should().Be("Accès aux comptes par : Agrégateur simulé.");

        panel.Find("#bank-connect").Click();

        _bank.Started.Should().ContainSingle().Which.Should().BeEquivalentTo(new { InstitutionId = "sim-lumiere", ReplacesId = (Guid?)null });
        Navigation.Uri.Should().EndWith(_bank.RedirectUrl);
    }

    [Fact]
    public void Un_refus_au_depart_s_affiche_dans_le_toaster()
    {
        _bank.NextError = ErrorCodes.BankConnection.Unavailable;
        IRenderedComponent<BankConnectionsPanel> panel = Render<BankConnectionsPanel>();

        panel.Find("#bank-connect").Click();

        this.Toasts()[^1].Kind.Should().Be(ToastKind.Error);
        this.ToastText().Should().StartWith("Aucun agrégateur bancaire");
    }

    [Fact]
    public void Un_compte_relu_recemment_n_est_pas_relu_a_l_ouverture_mais_se_synchronise_sur_demande()
    {
        BankConnectionResponse connection = FakeBankConnectionsGateway.Connection(lastSync: Now.AddHours(-1));
        _bank.Connections.Add(connection);
        int synced = 0;

        IRenderedComponent<BankConnectionsPanel> panel = Render<BankConnectionsPanel>(p => p.Add(c => c.OnSynced, () => synced++));

        _bank.Syncs.Should().BeEmpty();
        panel.Find($"[data-connection='{connection.Id}']").TextContent.Should()
            .Contain("Banque Lumière (simulée)").And.Contain("FR76 •••• 0142").And.Contain("Accord valable jusqu'au 01/04/2027").And.Contain("2 nouvelle(s) opération(s)");

        panel.Find($"#bank-sync-{connection.Id:N}").Click();

        _bank.Syncs.Should().ContainSingle().Which.ConnectionId.Should().Be(connection.Id);
        this.ToastText().Should().Be("Banque relue : 3 nouvelle(s) opération(s), 1 proposition(s).");
        synced.Should().Be(1);
    }

    [Fact]
    public void A_l_ouverture_un_compte_relu_il_y_a_plus_de_six_heures_est_relu_sans_bruit_s_il_n_y_a_rien()
    {
        _bank.Connections.Add(FakeBankConnectionsGateway.Connection(lastSync: Now.AddHours(-7)));
        _bank.NextImported = 0;

        Render<BankConnectionsPanel>();

        _bank.Syncs.Should().ContainSingle().Which.OnlyDue.Should().BeTrue();
        this.Toasts().Should().BeEmpty("rien de nouveau : la relecture automatique reste discrète");
    }

    [Fact]
    public void Un_refus_de_relecture_automatique_ne_s_affiche_pas_mais_un_refus_sur_demande_si()
    {
        BankConnectionResponse connection = FakeBankConnectionsGateway.Connection();
        _bank.Connections.Add(connection);
        _bank.NextError = ErrorCodes.BankConnection.BankUnreachable;

        IRenderedComponent<BankConnectionsPanel> panel = Render<BankConnectionsPanel>();
        this.Toasts().Should().BeEmpty();

        _bank.NextError = ErrorCodes.BankConnection.BankUnreachable;
        panel.Find($"#bank-sync-{connection.Id:N}").Click();

        this.ToastText().Should().Be("La banque n'a pas répondu. La prochaine synchronisation réessaiera.");
    }

    [Fact]
    public void Un_accord_proche_de_l_echeance_se_renouvelle_aupres_de_la_meme_banque()
    {
        BankConnectionResponse connection = FakeBankConnectionsGateway.Connection(lastSync: Now, needsRenewal: true, expires: Now.AddDays(10));
        _bank.Connections.Add(connection);
        IRenderedComponent<BankConnectionsPanel> panel = Render<BankConnectionsPanel>();
        panel.Find($"[data-connection='{connection.Id}']").TextContent.Should().Contain("Accord à renouveler avant le 26/10/2026");

        panel.Find($"#bank-renew-{connection.Id:N}").Click();

        _bank.Started.Should().ContainSingle().Which.Should().BeEquivalentTo(new { InstitutionId = "sim-lumiere", ReplacesId = (Guid?)connection.Id });
    }

    [Fact]
    public void Un_accord_echu_ne_se_synchronise_plus_et_se_renouvelle()
    {
        BankConnectionResponse connection = FakeBankConnectionsGateway.Connection(BankConnectionStatusDto.Expired, needsRenewal: true);
        _bank.Connections.Add(connection);

        IRenderedComponent<BankConnectionsPanel> panel = Render<BankConnectionsPanel>();

        panel.Find($"[data-connection='{connection.Id}'] .lettra-status").TextContent.Should().Be("Accord échu : à renouveler");
        panel.FindAll($"#bank-sync-{connection.Id:N}").Should().BeEmpty();
        panel.FindAll($"#bank-renew-{connection.Id:N}").Should().ContainSingle();
        _bank.Syncs.Should().BeEmpty();
    }

    [Fact]
    public void Deconnecter_retire_le_compte_de_la_liste()
    {
        BankConnectionResponse connection = FakeBankConnectionsGateway.Connection(lastSync: Now);
        _bank.Connections.Add(connection);
        IRenderedComponent<BankConnectionsPanel> panel = Render<BankConnectionsPanel>();

        panel.Find($"#bank-revoke-{connection.Id:N}").Click();

        _bank.Revoked.Should().Equal(connection.Id);
        panel.FindAll("#bank-connections-list").Should().BeEmpty();
        this.ToastText().Should().Be("Banque Lumière (simulée) est déconnecté. Les opérations déjà lues restent.");
    }

    [Fact]
    public void La_banque_simulee_renvoie_l_accord_ou_le_refus_vers_la_page_de_retour()
    {
        Guid id = Guid.CreateVersion7();
        Navigation.NavigateTo($"bank/simulated-consent?connection={id}&bank=Banque%20Lumi%C3%A8re");
        IRenderedComponent<SimulatedBankConsentPage> page = Render<SimulatedBankConsentPage>();
        page.Find("#bank-consent-bank").TextContent.Should().Be("Banque Lumière");

        page.Find("#bank-consent-accept").Click();
        Navigation.Uri.Should().EndWith($"bank/connected?connection={id}&code=accorde");

        page.Find("#bank-consent-refuse").Click();
        Navigation.Uri.Should().EndWith($"bank/connected?connection={id}&code=refuse");
    }

    [Fact]
    public void Le_retour_de_la_banque_relie_le_compte_et_ramene_au_rapprochement()
    {
        Guid id = Guid.CreateVersion7();
        Navigation.NavigateTo($"bank/connected?connection={id}&code=accorde");

        Render<BankConnectedPage>();

        _bank.Completed.Should().Equal((id, "accorde"));
        this.ToastText().Should().Be("Banque Lumière (simulée) est relié : 3 opération(s) importée(s), 1 proposition(s).");
        Navigation.Uri.Should().EndWith("/bank");
    }

    [Fact]
    public void Un_refus_au_retour_s_affiche_avec_le_chemin_du_rapprochement()
    {
        _bank.Refuse = true;
        Navigation.NavigateTo($"bank/connected?connection={Guid.CreateVersion7()}&code=refuse");

        IRenderedComponent<BankConnectedPage> page = Render<BankConnectedPage>();

        page.Find("#bank-callback-error").TextContent.Should().Contain("Le compte n'a pas été relié").And.Contain("L'accord a été refusé");
        page.Find("#bank-callback-back").GetAttribute("href").Should().Be("bank");
        this.Toasts().Should().BeEmpty();
    }

    [Fact]
    public void Une_erreur_au_retour_s_affiche_avec_son_motif()
    {
        _bank.NextError = ErrorCodes.BankConnection.BankUnreachable;
        Navigation.NavigateTo($"bank/connected?connection={Guid.CreateVersion7()}&code=accorde");

        IRenderedComponent<BankConnectedPage> page = Render<BankConnectedPage>();

        page.Find("#bank-callback-error").TextContent.Should().Contain("Le compte n'a pas été relié").And.Contain("La banque n'a pas répondu");
    }

    [Fact]
    public void Un_retour_sans_connexion_est_refuse_sans_appel()
    {
        Navigation.NavigateTo("bank/connected?code=accorde");

        IRenderedComponent<BankConnectedPage> page = Render<BankConnectedPage>();

        _bank.Completed.Should().BeEmpty();
        page.Find("#bank-callback-error").TextContent.Should().Contain("introuvable");
    }
}
