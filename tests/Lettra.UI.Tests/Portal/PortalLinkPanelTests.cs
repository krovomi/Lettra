using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Portal;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Portal;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Lettra.UI.Tests.Portal;

/// <summary>Espace client côté cabinet : créer le lien, le copier, voir les liens envoyés, en révoquer un.</summary>
[Collection("culture")]
public sealed class PortalLinkPanelTests : BunitContext
{
    private static readonly Guid Client = Guid.CreateVersion7();
    private readonly FakePortalGateway _portal = new();

    public PortalLinkPanelTests()
    {
        Services.AddLettraUi();
        Services.AddSingleton<IPortalGateway>(_portal);
        Services.AddSingleton<TimeProvider>(new ManualClock(_portal.Now));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<PortalLinkPanel> RenderPanel(bool showLinks = true, bool forInvoice = false) =>
        Render<PortalLinkPanel>(p => p.Add(c => c.CustomerId, Client).Add(c => c.ShowLinks, showLinks).Add(c => c.ForInvoice, forInvoice));

    [Fact]
    public void Creer_un_lien_d_espace_client_le_montre_avec_sa_validite_et_l_ajoute_a_la_liste()
    {
        IRenderedComponent<PortalLinkPanel> panel = RenderPanel();
        panel.FindAll("#portal-links-list").Should().BeEmpty();

        panel.Find("#portal-links-create").Click();

        panel.Find("#portal-links-url").GetAttribute("value").Should().Be("https://lettra.test/espace-client/jeton-1");
        panel.Find("#portal-links-open").GetAttribute("target").Should().Be("_blank");
        panel.Find("#portal-links-expires").TextContent.Should().StartWith("Valable jusqu'au 15/11/2026.");
        panel.FindAll("#portal-links-list li").Should().ContainSingle().Which.TextContent.Should()
            .Contain("Espace client").And.Contain("pas encore ouvert").And.Contain("Actif jusqu'au 15/11/2026");
        _portal.Created.Should().ContainSingle().Which.Should().BeEquivalentTo(new { CustomerId = Client, QuoteId = (Guid?)null, DownPaymentPercent = (decimal?)null });
        this.ToastText().Should().Be("Lien d'espace client créé.");
    }

    [Fact]
    public void Un_lien_de_paiement_part_d_une_facture()
    {
        IRenderedComponent<PortalLinkPanel> panel = RenderPanel(showLinks: false, forInvoice: true);

        panel.Find("#portal-links-create").Click();

        this.ToastText().Should().Be("Lien de paiement créé.");
        panel.FindAll("#portal-links-list").Should().BeEmpty("la liste n'est montrée que sur la fiche client");
        panel.FindAll("#portal-links-down-payment").Should().BeEmpty("l'acompte ne se demande qu'à la signature d'un devis");
    }

    [Fact]
    public void Copier_met_l_adresse_dans_le_presse_papiers()
    {
        JSInterop.SetupVoid("navigator.clipboard.writeText", "https://lettra.test/espace-client/jeton-1").SetVoidResult();
        IRenderedComponent<PortalLinkPanel> panel = RenderPanel();
        panel.Find("#portal-links-create").Click();

        panel.Find("#portal-links-copy").Click();

        this.ToastText().Should().Be("Lien copié : collez-le dans votre message au client.");
    }

    [Fact]
    public void Un_presse_papiers_refuse_laisse_l_adresse_a_selectionner()
    {
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true).SetException(new JSException("refusé"));
        IRenderedComponent<PortalLinkPanel> panel = RenderPanel();
        panel.Find("#portal-links-create").Click();

        panel.Find("#portal-links-copy").Click();

        this.ToastText().Should().Be("Copie impossible : sélectionnez l'adresse dans le champ.");
        panel.Find("#portal-links-url").Should().NotBeNull();
    }

    [Fact]
    public void Revoquer_un_lien_le_ferme_et_le_dit()
    {
        Guid id = Guid.CreateVersion7();
        _portal.Links.Add(new PortalLinkSummary(id, Guid.CreateVersion7(), 30m, _portal.Now.AddDays(-2), _portal.Now.AddDays(28),
            _portal.Now.AddDays(-1), null, IsActive: true));
        IRenderedComponent<PortalLinkPanel> panel = RenderPanel();
        panel.Find("#portal-links-list li").TextContent.Should().Contain("Signature de devis · acompte de 30 %").And.Contain("ouvert le");

        panel.Find($"#portal-links-revoke-{id:N}").Click();

        _portal.Revoked.Should().Equal(id);
        panel.Find("#portal-links-list li").TextContent.Should().Contain("Révoqué");
        panel.FindAll($"#portal-links-revoke-{id:N}").Should().BeEmpty();
        this.ToastText().Should().Be("Lien révoqué : il ne s'ouvre plus.");
    }

    [Fact]
    public void Un_lien_expire_ne_se_revoque_plus()
    {
        _portal.Links.Add(new PortalLinkSummary(Guid.CreateVersion7(), null, null, _portal.Now.AddDays(-40), _portal.Now.AddDays(-10), null, null, IsActive: false));

        IRenderedComponent<PortalLinkPanel> panel = RenderPanel();

        panel.Find("#portal-links-list li").TextContent.Should().Contain("Expiré");
        panel.FindAll("#portal-links-list button").Should().BeEmpty();
    }

    [Fact]
    public void Un_refus_de_revocation_s_affiche()
    {
        Guid id = Guid.CreateVersion7();
        _portal.Links.Add(new PortalLinkSummary(id, null, null, _portal.Now, _portal.Now.AddDays(30), null, null, IsActive: true));
        IRenderedComponent<PortalLinkPanel> panel = RenderPanel();
        _portal.NextError = ErrorCodes.Portal.LinkNotFound;

        panel.Find($"#portal-links-revoke-{id:N}").Click();

        _portal.Revoked.Should().BeEmpty();
        this.Toasts()[^1].Kind.Should().Be(ToastKind.Error);
    }

    [Fact]
    public void Sans_serveur_l_espace_client_est_ferme_et_le_bouton_disparait()
    {
        IRenderedComponent<PortalLinkPanel> panel = RenderPanel();
        _portal.NextError = ErrorCodes.Portal.Disabled;

        panel.Find("#portal-links-create").Click();

        panel.Find("#portal-links-disabled").TextContent.Should().StartWith("L'espace client demande le serveur du cabinet");
        panel.FindAll("#portal-links-create").Should().BeEmpty();
        this.ToastText().Should().Be("L'espace client n'est pas ouvert sur cette installation (il demande le serveur du cabinet).");
    }

    [Fact]
    public void Le_panneau_est_traduit_en_anglais()
    {
        LettraCultures.Apply(CultureInfo.GetCultureInfo("en-US"));
        Guid quote = Guid.CreateVersion7();

        IRenderedComponent<PortalLinkPanel> panel = Render<PortalLinkPanel>(p => p.Add(c => c.CustomerId, Client).Add(c => c.QuoteId, quote).Add(c => c.Id, "quote-portal"));
        panel.Find("#quote-portal-create").Click();

        this.ToastText().Should().Be("Signature link created.");
        _portal.Created.Should().ContainSingle().Which.DownPaymentPercent.Should().Be(30m, "30 % d'acompte est proposé par défaut");
    }
}
