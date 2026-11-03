using Lettra.Api.Portal;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Portal;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Portal;
using Lettra.Exports.Application.Reminders;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lettra.Api.Tests;

/// <summary>Pages de l'espace client rendues par le serveur : textes FR et EN, encodage, états des pièces, réglages.</summary>
public sealed class CustomerPortalPagesTests
{
    private static readonly DateTimeOffset Expires = new(2026, 11, 24, 23, 0, 0, TimeSpan.Zero);
    private static readonly Guid QuoteId = Guid.CreateVersion7();
    private static readonly Guid InvoiceId = Guid.CreateVersion7();

    private static PortalQuote Quote(QuoteStatusDto status = QuoteStatusDto.Validated, bool expired = false, bool canSign = true, QuoteSignatureResponse? signature = null, Guid? id = null) =>
        new(id ?? QuoteId, "DEV-000001", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "Refonte <du> site", status, expired, 500m, 100m, 600m, canSign,
            [new PortalQuoteLine("Conception", 5m, "HUR", 100m, 500m)], signature);

    private static PortalView View(IReadOnlyList<PortalInvoice>? invoices = null, IReadOnlyList<PortalQuote>? quotes = null, string? iban = "FR7630006000011234567890189",
        Guid? focus = null, decimal? downPayment = null, bool canPay = true) =>
        new("Cabinet <script>alert(1)</script>", "Paris", "Boulangerie \"Martin\"", iban, "AGRIFRPP", null,
            invoices ?? [new PortalInvoice(InvoiceId, "FAC-000007", InvoiceKindDto.Invoice, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), 120m, 120m, IsOverdue: true)],
            quotes ?? [], focus, downPayment, canPay, Expires);

    private static string Render(PortalView view, string? language = null, PortalBanner? banner = null) =>
        PortalPages.Portal(view, PortalTexts.For(language, null), "/espace-client/jeton", banner);

    [Fact]
    public void Les_textes_existent_dans_les_deux_langues()
    {
        PortalTexts.EnglishKeys.Should().BeEquivalentTo(PortalTexts.Keys);
        string[] seenByCustomer =
        [
            "Disabled", "LinkNotFound", "QuoteNotSignable", "SignerNameInvalid", "ConsentRequired", "AlreadySigned", "NothingToPay", "PaymentDeclined", "CheckoutNotFound",
        ];
        PortalTexts.Keys.Should().Contain(seenByCustomer.Select(c => "Error.Portal." + c), "chaque refus vu par le client a son texte").And.Contain("Error.Unexpected");
    }

    [Theory]
    [InlineData(null, null, "fr")]
    [InlineData(null, "en-GB,en;q=0.9", "en")]
    [InlineData("fr-FR", "en-GB", "fr")]
    [InlineData("en-US", "fr-FR", "en")]
    [InlineData(null, "de-DE", "fr")]
    public void La_langue_de_la_fiche_client_passe_avant_celle_du_navigateur(string? preferred, string? browser, string expected)
    {
        PortalTexts.For(preferred, browser).Language.Should().Be(expected);
    }

    [Fact]
    public void Montants_et_dates_suivent_la_langue_et_l_heure_de_Paris()
    {
        PortalTexts fr = PortalTexts.For("fr-FR", null);
        PortalTexts en = PortalTexts.For("en-US", null);

        fr.Money(1234.5m).Replace('\u202F', ' ').Replace('\u00A0', ' ').Should().Be("1 234,50 €", "espaces insécables du français");
        en.Money(1234.5m).Should().Be("€1,234.50");
        fr.Date(new DateOnly(2026, 10, 25)).Should().Be("25 octobre 2026");
        en.Date(new DateOnly(2026, 10, 25)).Should().Be("25 October 2026");
        fr.Date(Expires).Should().Be("25 novembre 2026 à 00:00");
        en.Date(Expires).Should().Be("November 25, 2026 at 12:00 AM");
        fr["Inconnue"].Should().Be("Inconnue");
    }

    [Fact]
    public void Les_donnees_du_cabinet_et_du_client_sont_encodees()
    {
        string html = Render(View(quotes: [Quote()]));

        html.Should().NotContain("<script>").And.Contain("Cabinet &lt;script&gt;alert(1)&lt;/script&gt;")
            .And.Contain("Boulangerie &quot;Martin&quot;").And.Contain("Refonte &lt;du&gt; site");
        html.Should().Contain("<html lang=\"fr\"").And.Contain("noindex").And.Contain("<meta name=\"referrer\" content=\"same-origin\">");
    }

    [Fact]
    public void Une_facture_due_se_paie_par_carte_ou_par_virement_avec_son_QR_code()
    {
        string html = Render(View());

        html.Should().Contain($"action=\"/espace-client/jeton/factures/{InvoiceId}/payer\"").And.Contain($"id=\"pay-{InvoiceId}\"");
        html.Should().Contain("<svg").And.Contain("FR76 3000 6000 0112 3456 7890 189").And.Contain("AGRIFRPP").And.Contain("FAC-000007");
        html.Should().Contain("ec-badge is-late");
    }

    [Fact]
    public void Sans_paiement_en_ligne_ni_IBAN_il_ne_reste_que_le_montant()
    {
        string html = Render(View(iban: null, canPay: false));

        html.Should().NotContain("/payer").And.NotContain("<svg").And.Contain("FAC-000007");
    }

    [Fact]
    public void Rien_a_payer_et_l_historique_des_factures_reglees()
    {
        string html = Render(View(invoices:
        [
            new PortalInvoice(InvoiceId, "FAC-000007", InvoiceKindDto.Invoice, new DateOnly(2026, 9, 1), null, 120m, 0m, false),
        ]));

        html.Should().Contain("id=\"portal-history\"").And.Contain("FAC-000007").And.NotContain("id=\"portal-total\"");
    }

    [Fact]
    public void Un_lien_de_signature_commence_par_son_devis_et_annonce_l_acompte()
    {
        string html = Render(View(quotes: [Quote()], focus: QuoteId, downPayment: 30m));

        html.IndexOf("id=\"portal-quotes\"", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("id=\"portal-invoices\"", StringComparison.Ordinal));
        html.Should().Contain("ec-card is-focus").And.Contain("id=\"down-payment-notice\"").And.Contain("180,00 €").And.Contain("<details class=\"ec-lines\" open>");
        html.Should().Contain($"action=\"/espace-client/jeton/devis/{QuoteId}/signer\"").And.Contain("name=\"consent\" value=\"true\" required");
    }

    [Fact]
    public void Chaque_etat_du_devis_a_son_badge_et_un_devis_signe_montre_sa_preuve()
    {
        QuoteSignatureResponse proof = new(QuoteId, "Marie Martin", Expires, new string('b', 64), "Signature simulée", "sig_sim_1", null);
        string html = Render(View(quotes:
        [
            Quote(QuoteStatusDto.Accepted, canSign: false, signature: proof),
            Quote(QuoteStatusDto.Refused, canSign: false, id: Guid.CreateVersion7()),
            Quote(QuoteStatusDto.Invoiced, canSign: false, id: Guid.CreateVersion7()),
            Quote(QuoteStatusDto.Validated, expired: true, canSign: false, id: Guid.CreateVersion7()),
            Quote(QuoteStatusDto.Validated, canSign: false, id: Guid.CreateVersion7()),
        ]));

        html.Should().Contain($"id=\"signed-{QuoteId}\"").And.Contain("Marie Martin").And.Contain("sig_sim_1").And.Contain("bbbbbbbbbbbbbbbb")
            .And.NotContain("id=\"sign-");
        foreach (string badge in new[] { "Accepted", "Refused", "Invoiced", "Expired", "Validated" })
        {
            html.Should().Contain(PortalTexts.For(null, null)[badge]);
        }
    }

    private static readonly IReadOnlyList<PortalPlanOption> PlanOptions =
    [
        new(2, [new(1, new DateOnly(2026, 11, 10), 60m, false), new(2, new DateOnly(2026, 12, 10), 60m, false)]),
        new(3, [new(1, new DateOnly(2026, 11, 5), 40m, false), new(2, new DateOnly(2026, 11, 25), 40m, false), new(3, new DateOnly(2026, 12, 15), 40m, false)]),
    ];

    [Fact]
    public void Une_facture_qu_on_peut_etaler_propose_l_echeancier_avec_engagement()
    {
        PortalInvoice invoice = new(InvoiceId, "FAC-000007", InvoiceKindDto.Invoice, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), 120m, 120m, true, null, PlanOptions);

        string html = Render(View(invoices: [invoice]));

        html.Should().Contain($"action=\"/espace-client/jeton/factures/{InvoiceId}/echeancier\"").And.Contain($"id=\"plan-form-{InvoiceId}\"")
            .And.Contain("Payer en plusieurs fois").And.Contain("En 2 fois").And.Contain("En 3 fois").And.Contain("Échéance 3 : 40,00").And.Contain("15 décembre 2026")
            .And.Contain("name=\"installments\" value=\"2\"").And.Contain("name=\"consent\" value=\"true\" required")
            .And.Contain("Cabinet &lt;script&gt;alert(1)&lt;/script&gt; vous permet d&#x27;étaler cette facture");
        html.Should().Contain($"id=\"plan-submit-{InvoiceId}\"");
    }

    [Fact]
    public void Un_echeancier_en_cours_montre_ses_echeances_et_qui_l_a_demande()
    {
        PortalPaymentPlan plan = new("Jeanne <Durand>", Expires, [new(1, new DateOnly(2026, 11, 10), 60m, true), new(2, new DateOnly(2026, 12, 10), 60m, false)]);
        PortalInvoice invoice = new(InvoiceId, "FAC-000007", InvoiceKindDto.Invoice, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), 120m, 60m, true, plan, []);

        string fr = Render(View(invoices: [invoice]));
        string en = Render(View(invoices: [invoice]), "en-US");

        fr.Should().Contain($"id=\"plan-{InvoiceId}\"").And.Contain("Votre échéancier").And.Contain("Réglée").And.Contain("Jeanne &lt;Durand&gt;")
            .And.NotContain("/echeancier\"");
        en.Should().Contain("Your payment plan").And.Contain("Instalment 2: €60.00, on 10 December 2026").And.Contain("Paid");
    }

    [Fact]
    public void L_echeancier_enregistre_est_annonce_et_ses_refus_ont_leur_texte()
    {
        string html = Render(View(), banner: new PortalBanner("Banner.PlanCreated", "FAC-000007", false));

        html.Should().Contain("Échéancier enregistré pour la facture FAC-000007");
        string[] refusals = ["Disabled", "NotProfessional", "NothingDue", "Disputed", "AlreadyPlanned", "BelowMinimum", "NoRoom", "InstallmentsInvalid", "RequesterInvalid", "ConsentRequired"];
        PortalTexts.Keys.Should().Contain(refusals.Select(c => "Error.PaymentPlans." + c));
    }

    [Fact]
    public void La_page_est_en_anglais_pour_un_client_anglophone()
    {
        string html = Render(View(quotes: [Quote()]), "en-US", new PortalBanner("Banner.Paid", "FAC-000007", false));

        html.Should().Contain("<html lang=\"en\"").And.Contain("Payment received for invoice FAC-000007").And.Contain("role=\"status\"");
    }

    [Fact]
    public void Un_bandeau_d_erreur_est_une_alerte()
    {
        string html = Render(View(), banner: new PortalBanner("Error.Portal.PaymentDeclined", null, true));

        html.Should().Contain("class=\"ec-banner is-error\" role=\"alert\"");
    }

    [Fact]
    public void La_page_de_paiement_simulee_preremplit_la_carte_de_test()
    {
        Checkout checkout = new("cs_sim_1", new CheckoutRequest(new DossierId(Guid.CreateVersion7()), Guid.CreateVersion7(), new Domain.Billing.InvoiceId(InvoiceId),
            "FAC-000007", 120m, "EUR", "Conseil <Lettra>", "/retour"), CheckoutState.Pending, null, "/paiement-simule/cs_sim_1");

        string html = PortalPages.SimulatedCheckout(checkout, PortalTexts.For(null, null), declined: true);

        html.Should().Contain("action=\"/paiement-simule/cs_sim_1\"").And.Contain("4242 4242 4242 4242").And.Contain("120,00 €")
            .And.Contain("Conseil &lt;Lettra&gt;").And.Contain("id=\"checkout-declined\"").And.Contain("ec-checkout");
        PortalPages.SimulatedCheckout(checkout, PortalTexts.For(null, null), declined: false).Should().NotContain("checkout-declined");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("https://lettra.test", true)]
    [InlineData("https://LETTRA.test", true)]
    [InlineData("https://pirate.example", false)]
    [InlineData("pas une origine", false)]
    [InlineData("null", false)]
    public void Un_formulaire_ne_se_poste_que_depuis_l_espace_client(string? origin, bool accepted)
    {
        DefaultHttpContext context = new();
        context.Request.Host = new HostString("lettra.test");
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        CustomerPortalHosting.SameOrigin(context).Should().Be(accepted);
    }

    private sealed class Host : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public Guid UserId { get; } = Guid.CreateVersion7();
    }

    private static ServiceProvider Services(Dictionary<string, string?> settings, bool withHost = true)
    {
        ServiceCollection services = new();
        services.AddLogging();
        if (withHost)
        {
            services.AddScoped<ICurrentUser, Host>();
        }

        services.AddCustomerPortal(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Sans_compte_de_service_l_identite_de_l_hote_reste_celle_de_la_requete()
    {
        using ServiceProvider provider = Services([]);
        using IServiceScope scope = provider.CreateScope();

        ICurrentUser user = scope.ServiceProvider.GetRequiredService<ICurrentUser>();
        user.UserId.Should().NotBeEmpty();
        user.IsAuthenticated.Should().BeTrue();
        provider.GetRequiredService<ICustomerPortalSettings>().IsEnabled.Should().BeFalse("sans compte de service, aucun lien ne peut être créé");
    }

    [Fact]
    public void Le_compte_de_service_agit_dans_la_portee_d_une_page_seulement()
    {
        Guid account = Guid.CreateVersion7();
        using ServiceProvider provider = Services(new() { ["Lettra:CustomerPortal:ServiceAccountId"] = account.ToString() });
        using IServiceScope page = provider.CreateScope();
        using IServiceScope request = provider.CreateScope();

        page.ServiceProvider.GetRequiredService<PortalActor>().UserId = account;

        page.ServiceProvider.GetRequiredService<ICurrentUser>().UserId.Should().Be(account);
        page.ServiceProvider.GetRequiredService<ICurrentUser>().IsAuthenticated.Should().BeTrue();
        request.ServiceProvider.GetRequiredService<ICurrentUser>().UserId.Should().NotBe(account);
        provider.GetRequiredService<ICustomerPortalSettings>().IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Un_second_branchement_ne_change_rien()
    {
        ServiceCollection services = new();
        services.AddScoped<ICurrentUser, Host>();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Lettra:CustomerPortal:ServiceAccountId"] = Guid.CreateVersion7().ToString() }).Build();

        services.AddCustomerPortal(configuration);
        int count = services.Count;
        services.AddCustomerPortal(configuration);

        services.Should().HaveCount(count);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "60")]
    [InlineData(null, "0")]
    [InlineData(null, "10001")]
    public void Des_reglages_impossibles_sont_refuses_au_demarrage(string? account, string permits)
    {
        using ServiceProvider provider = Services(new() { ["Lettra:CustomerPortal:ServiceAccountId"] = account, ["Lettra:CustomerPortal:PermitsPerMinute"] = permits });

        FluentActions.Invoking(() => provider.GetRequiredService<IOptions<CustomerPortalOptions>>().Value).Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void L_hote_de_l_api_dessine_le_qr_code_de_virement_joint_aux_rappels()
    {
        using ServiceProvider provider = Services([]);
        IPaymentQrCodeImage qr = provider.GetRequiredService<IPaymentQrCodeImage>();

        qr.Png("Conseil Lettra", "FR7630006000011234567890189", "AGRIFRPP", 120m, "FAC-000007")!.Take(4).Should().Equal(0x89, (byte)'P', (byte)'N', (byte)'G');
        qr.Png("Conseil Lettra", null, null, 120m, "FAC-000007").Should().BeNull("sans IBAN, pas de virement prérempli");
    }

    [Fact]
    public void Le_compte_de_service_demande_l_identite_de_l_hote()
    {
        FluentActions.Invoking(() => Services([], withHost: false))
            .Should().Throw<InvalidOperationException>().WithMessage("*ICurrentUser*");
    }

    private sealed class EmptyCatalog : ITenantCatalog
    {
        public ValueTask<TenantDescriptor?> FindAsync(DossierId dossierId, CancellationToken cancellationToken) => ValueTask.FromResult<TenantDescriptor?>(null);

        public ValueTask<bool> UserHasAccessAsync(Guid userId, DossierId dossierId, CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }

    [Fact]
    public async Task Un_espace_ferme_un_jeton_illisible_ou_un_dossier_inconnu_ne_lancent_rien()
    {
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        IServiceScopeFactory scopes = provider.GetRequiredService<IServiceScopeFactory>();
        int calls = 0;
        Task<Domain.Common.Result<int>> Work(DossierId dossier, string hash, Application.Abstractions.Messaging.ICommandDispatcher commands, CancellationToken ct)
        {
            calls++;
            return Task.FromResult(Domain.Common.Result<int>.Success(1));
        }

        PortalRunner closed = new(scopes, new EmptyCatalog(), Options.Create(new CustomerPortalOptions()));
        PortalRunner open = new(scopes, new EmptyCatalog(), Options.Create(new CustomerPortalOptions { ServiceAccountId = Guid.CreateVersion7() }));

        (await closed.RunAsync(PortalToken.Create(new DossierId(Guid.CreateVersion7())).Token, Work, default)).Errors[0].Code.Should().Be("Portal.Disabled");
        (await open.RunAsync("illisible", Work, default)).Errors[0].Code.Should().Be("Portal.LinkNotFound");
        (await open.RunAsync(PortalToken.Create(new DossierId(Guid.CreateVersion7())).Token, Work, default)).Errors[0].Code.Should().Be("Portal.LinkNotFound");
        calls.Should().Be(0);
    }
}
