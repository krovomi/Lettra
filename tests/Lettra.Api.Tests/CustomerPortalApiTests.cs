using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lettra.Api.Portal;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Portal;
using Lettra.Contracts.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Espace client par l'API réelle (mode 2, SQLite) : le cabinet crée le lien, le client ouvre ses pages sans compte,
/// paie par carte sur la page du prestataire simulé (refus puis accord), signe un devis et reçoit l'acompte ; un jeton
/// faux, une origine étrangère et un lien révoqué sont refusés. Les pages sont montées comme le fera <c>Program.cs</c>.
/// </summary>
public sealed class CustomerPortalApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    public static readonly Guid ServiceAccountId = new("0199e3a0-0000-7000-8000-00000000e0ec");

    private static Uri Api(string path) => new(path, UriKind.Relative);

    /// <summary>Le compte de service de l'espace client et son rôle, déclarés au registre des droits comme dans la démo.</summary>
    private static readonly Dictionary<string, string?> PortalSettings = new()
    {
        ["Lettra:CustomerPortal:ServiceAccountId"] = ServiceAccountId.ToString(),
        ["Lettra:Authorization:Roles:0:Cabinet"] = LettraApiFactory.CabinetId.ToString(),
        ["Lettra:Authorization:Roles:0:Key"] = "espace-client",
        ["Lettra:Authorization:Roles:0:WidestScope"] = "Cabinet",
        ["Lettra:Authorization:Roles:0:Permissions:0"] = "PortailConsulter",
        ["Lettra:Authorization:Roles:0:Permissions:1"] = "DevisAccepter",
        ["Lettra:Authorization:Roles:0:Permissions:2"] = "DossierLire",
        ["Lettra:Authorization:Roles:0:Permissions:3"] = "TiersConsulter",
        ["Lettra:Authorization:Roles:0:Permissions:4"] = "DevisCreer",
        ["Lettra:Authorization:Roles:0:Permissions:5"] = "FactureSaisir",
        ["Lettra:Authorization:Roles:0:Permissions:6"] = "FactureValider",
        ["Lettra:Authorization:Roles:0:Permissions:7"] = "FactureEmettre",
        ["Lettra:Authorization:Users:6:Id"] = ServiceAccountId.ToString(),
        ["Lettra:Authorization:Users:6:Cabinet"] = LettraApiFactory.CabinetId.ToString(),
        ["Lettra:Authorization:Users:6:State"] = "Active",
        ["Lettra:Authorization:Users:6:Assignments:0:Role"] = "espace-client",
        ["Lettra:Authorization:Users:6:Assignments:0:Scope"] = "Cabinet",
        ["Lettra:Authorization:Users:6:Assignments:0:ScopeId"] = LettraApiFactory.CabinetId.ToString(),
    };

    /// <summary>
    /// L'API avec l'espace client ouvert : services et pages, en tête du pipeline. Sa propre base : la clé du cabinet de
    /// test est tirée à chaque hôte, celle de la base partagée ne s'ouvrirait pas.
    /// </summary>
    private WebApplicationFactory<Program> Portal() => factory.WithWebHostBuilder(builder =>
    {
        string database = Path.Combine(Directory.CreateTempSubdirectory("lettra-espace-client-").FullName, "api.db");
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(PortalSettings).AddInMemoryCollection(LettraApiFactory.DossierSettings(database)));
        builder.ConfigureTestServices(services =>
        {
            services.AddCustomerPortal(new ConfigurationBuilder().AddInMemoryCollection(PortalSettings).Build());
            services.AddTransient<IStartupFilter, PortalPagesStartup>();
        });
    });

    private sealed class PortalPagesStartup : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.MapWhen(c => c.Request.Path.StartsWithSegments("/espace-client") || c.Request.Path.StartsWithSegments("/paiement-simule"), portal =>
            {
                portal.UseRouting();
                portal.UseRateLimiter();
                portal.UseEndpoints(endpoints => endpoints.MapCustomerPortal());
            });
            next(app);
        };
    }

    private static HttpClient InDossier(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    /// <summary>Le navigateur du client : sans compte, sans suivre les redirections (on les vérifie).</summary>
    private static HttpClient Visitor(WebApplicationFactory<Program> app)
    {
        HttpClient client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("fr-FR"));
        return client;
    }

    private static async Task<(Guid Customer, Guid Invoice, Guid Quote)> DocumentsAsync(HttpClient manager)
    {
        Guid invoice = await ExportApiTests.ValidatedInvoiceAsync(manager);
        Guid customer = (await manager.GetFromJsonAsync(Api($"/api/v1/invoices/{invoice}"), ContractsJsonContext.Default.InvoiceResponse))!.CustomerId;
        using HttpResponseMessage created = await manager.PostAsJsonAsync(Api("/api/v1/quotes"), new SaveQuoteRequest
        {
            CustomerId = customer,
            Subject = "Refonte du site",
            Lines = [new DocumentLineRequest { Designation = "Conception", Quantity = 5, UnitPriceExclTax = 100m, UnitCode = "HUR", Kind = CatalogItemKindDto.Service }],
        }, ContractsJsonContext.Default.SaveQuoteRequest);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Guid quote = (await created.Content.ReadFromJsonAsync(ContractsJsonContext.Default.QuoteResponse))!.Id;
        using HttpRequestMessage validate = new(HttpMethod.Post, Api($"/api/v1/quotes/{quote}/validate"));
        validate.Headers.IfMatch.Add(EntityTagHeaderValue.Any);
        (await manager.SendAsync(validate)).StatusCode.Should().Be(HttpStatusCode.OK);
        return (customer, invoice, quote);
    }

    private static async Task<PortalLinkResponse> LinkAsync(HttpClient manager, CreatePortalLinkRequest request)
    {
        using HttpResponseMessage created = await manager.PostAsJsonAsync(Api("/api/v1/portal-links"), request, ContractsJsonContext.Default.CreatePortalLinkRequest);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync(ContractsJsonContext.Default.PortalLinkResponse))!;
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    private static string Location(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return response.Headers.Location!.OriginalString;
    }

    [Fact]
    public async Task Le_client_paie_sa_facture_par_carte_apres_un_premier_refus()
    {
        using WebApplicationFactory<Program> app = Portal();
        using HttpClient manager = InDossier(app.CreateClient());
        (Guid customer, Guid invoice, _) = await DocumentsAsync(manager);
        PortalLinkResponse link = await LinkAsync(manager, new CreatePortalLinkRequest { CustomerId = customer });
        using HttpClient visitor = Visitor(app);

        using HttpResponseMessage page = await visitor.GetAsync(Api("/" + link.Path));
        string html = await page.Content.ReadAsStringAsync();
        page.StatusCode.Should().Be(HttpStatusCode.OK, html);
        page.Headers.CacheControl!.NoStore.Should().BeTrue("les pièces du client ne restent pas en cache");
        page.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        html.Should().Contain("<html lang=\"fr\"").And.Contain("Bonjour Boulangerie Martin").And.Contain($"id=\"pay-{invoice}\"").And.Contain("Conseil Lettra");

        // Un formulaire posté depuis un autre site est refusé.
        using HttpRequestMessage foreign = new(HttpMethod.Post, Api($"/{link.Path}/factures/{invoice}/payer")) { Content = Form() };
        foreign.Headers.Add("Origin", "https://pirate.example");
        (await visitor.SendAsync(foreign)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Comme un navigateur : le formulaire annonce l'origine de la page.
        using HttpRequestMessage pay = new(HttpMethod.Post, Api($"/{link.Path}/factures/{invoice}/payer")) { Content = Form() };
        pay.Headers.Add("Origin", visitor.BaseAddress!.GetLeftPart(UriPartial.Authority));
        string checkout = Location(await visitor.SendAsync(pay));
        checkout.Should().StartWith("/paiement-simule/cs_sim_");
        string form = await visitor.GetStringAsync(Api(checkout));
        form.Should().Contain("id=\"checkout-form\"").And.Contain("Conseil Lettra").And.Contain("4242 4242 4242 4242");

        Location(await visitor.PostAsync(Api(checkout), Form(("card", "4000 0000 0000 0002"), ("decision", "pay")))).Should().Be(checkout + "?refus=1");
        (await visitor.GetStringAsync(Api(checkout + "?refus=1"))).Should().Contain("id=\"checkout-declined\"");

        string back = Location(await visitor.PostAsync(Api(checkout), Form(("card", "4242 4242 4242 4242"), ("decision", "pay"))));
        back.Should().Be($"/{link.Path}/paiement/{checkout["/paiement-simule/".Length..]}");
        string done = Location(await visitor.GetAsync(Api(back)));
        done.Should().StartWith($"/{link.Path}?paye=");
        (await visitor.GetStringAsync(Api(done))).Should().Contain("id=\"portal-banner\"").And.Contain("Paiement reçu");
        Location(await visitor.GetAsync(Api(back))).Should().StartWith($"/{link.Path}?paye=", "revenir deux fois n'encaisse pas deux fois");

        InvoiceResponse paid = (await manager.GetFromJsonAsync(Api($"/api/v1/invoices/{invoice}"), ContractsJsonContext.Default.InvoiceResponse))!;
        paid.PaidInclTax.Should().Be(paid.Totals.TotalInclTax);
        Location(await visitor.PostAsync(Api($"/{link.Path}/factures/{invoice}/payer"), Form())).Should().Contain("erreur=Portal.NothingToPay");
        (await visitor.GetAsync(Api(checkout))).StatusCode.Should().Be(HttpStatusCode.NotFound, "un paiement traité ne se rejoue pas");
    }

    [Fact]
    public async Task Annuler_ou_trois_refus_ramenent_a_l_espace_client_sans_rien_encaisser()
    {
        using WebApplicationFactory<Program> app = Portal();
        using HttpClient manager = InDossier(app.CreateClient());
        (Guid customer, Guid invoice, _) = await DocumentsAsync(manager);
        PortalLinkResponse link = await LinkAsync(manager, new CreatePortalLinkRequest { CustomerId = customer });
        using HttpClient visitor = Visitor(app);

        string cancelled = Location(await visitor.PostAsync(Api($"/{link.Path}/factures/{invoice}/payer"), Form()));
        string pending = Location(await visitor.GetAsync(Api(Location(await visitor.PostAsync(Api(cancelled), Form(("decision", "cancel")))))));
        pending.Should().Contain("attente=");
        (await visitor.GetStringAsync(Api(pending))).Should().Contain("n&#x27;est pas terminé");

        string blocked = Location(await visitor.PostAsync(Api($"/{link.Path}/factures/{invoice}/payer"), Form()));
        for (int i = 1; i < 3; i++)
        {
            Location(await visitor.PostAsync(Api(blocked), Form(("card", "4000000000000002"), ("decision", "pay")))).Should().EndWith("?refus=1");
        }

        string back = Location(await visitor.PostAsync(Api(blocked), Form(("card", "4000000000000002"), ("decision", "pay"))));
        string error = Location(await visitor.GetAsync(Api(back)));
        error.Should().Contain("erreur=Portal.PaymentDeclined");
        (await visitor.GetStringAsync(Api(error))).Should().Contain("refusé par votre banque");
        (await manager.GetFromJsonAsync(Api($"/api/v1/invoices/{invoice}"), ContractsJsonContext.Default.InvoiceResponse))!.PaidInclTax.Should().Be(0m);
    }

    [Fact]
    public async Task Le_client_signe_le_devis_et_l_acompte_est_facture()
    {
        using WebApplicationFactory<Program> app = Portal();
        using HttpClient manager = InDossier(app.CreateClient());
        (Guid customer, _, Guid quote) = await DocumentsAsync(manager);
        PortalLinkResponse link = await LinkAsync(manager, new CreatePortalLinkRequest { CustomerId = customer, QuoteId = quote, DownPaymentPercent = 30m });
        using HttpClient visitor = Visitor(app);

        string html = await visitor.GetStringAsync(Api("/" + link.Path));
        html.Should().Contain($"id=\"sign-{quote}\"").And.Contain("id=\"down-payment-notice\"").And.Contain("Refonte du site");
        html.IndexOf("id=\"portal-quotes\"", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("id=\"portal-invoices\"", StringComparison.Ordinal),
            "un lien de signature commence par son devis");

        Location(await visitor.PostAsync(Api($"/{link.Path}/devis/{quote}/signer"), Form(("signer", "Marie Martin"))))
            .Should().Contain("erreur=Portal.ConsentRequired");
        string signed = Location(await visitor.PostAsync(Api($"/{link.Path}/devis/{quote}/signer"), Form(("signer", "Marie Martin"), ("consent", "true"))));
        signed.Should().Contain("signe=").And.Contain("acompte=1");

        string after = await visitor.GetStringAsync(Api(signed));
        after.Should().Contain($"id=\"signed-{quote}\"").And.Contain("Marie Martin").And.Contain("Facture d&#x27;acompte").And.NotContain($"id=\"sign-{quote}\"");

        QuoteSignatureResponse proof = (await manager.GetFromJsonAsync(Api($"/api/v1/quotes/{quote}/signature"), ContractsJsonContext.Default.QuoteSignatureResponse))!;
        proof.SignerName.Should().Be("Marie Martin");
        InvoiceResponse downPayment = (await manager.GetFromJsonAsync(Api($"/api/v1/invoices/{proof.DownPaymentInvoiceId}"), ContractsJsonContext.Default.InvoiceResponse))!;
        downPayment.Kind.Should().Be(InvoiceKindDto.DownPayment);
        downPayment.Status.Should().Be(InvoiceStatusDto.Validated);
        downPayment.CreatedBy.Should().Be(ServiceAccountId, "l'acompte porte l'identité du compte de service");
    }

    [Fact]
    public async Task Un_jeton_faux_un_lien_revoque_et_un_paiement_inconnu_donnent_une_page_introuvable()
    {
        using WebApplicationFactory<Program> app = Portal();
        using HttpClient manager = InDossier(app.CreateClient());
        (Guid customer, _, _) = await DocumentsAsync(manager);
        PortalLinkResponse link = await LinkAsync(manager, new CreatePortalLinkRequest { CustomerId = customer });
        using HttpClient visitor = Visitor(app);
        visitor.DefaultRequestHeaders.AcceptLanguage.Clear();
        visitor.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-GB"));

        using HttpResponseMessage fake = await visitor.GetAsync(Api("/espace-client/pas-un-jeton"));
        fake.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await fake.Content.ReadAsStringAsync()).Should().Contain("<html lang=\"en\"");
        (await visitor.GetAsync(Api("/paiement-simule/cs_sim_inconnu"))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        IReadOnlyList<PortalLinkSummary> links = (await manager.GetFromJsonAsync(Api($"/api/v1/portal-links?customerId={customer}"),
            ContractsJsonContext.Default.IReadOnlyListPortalLinkSummary))!;
        links.Should().ContainSingle(l => l.Id == link.Id).Which.LastOpenedAt.Should().BeNull();
        (await visitor.GetAsync(Api("/" + link.Path))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await manager.GetFromJsonAsync(Api($"/api/v1/portal-links?customerId={customer}"), ContractsJsonContext.Default.IReadOnlyListPortalLinkSummary))!
            .Single(l => l.Id == link.Id).LastOpenedAt.Should().NotBeNull("le cabinet voit que le client a ouvert son lien");

        (await manager.PostAsync(Api($"/api/v1/portal-links/{link.Id}/revoke"), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await visitor.GetAsync(Api("/" + link.Path))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await manager.PostAsync(Api($"/api/v1/portal-links/{link.Id}/revoke"), null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Sans_compte_de_service_l_espace_client_est_ferme()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        (Guid customer, _, Guid quote) = await DocumentsAsync(manager);

        using HttpResponseMessage refused = await manager.PostAsJsonAsync(Api("/api/v1/portal-links"),
            new CreatePortalLinkRequest { CustomerId = customer }, ContractsJsonContext.Default.CreatePortalLinkRequest);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Portal.Disabled);
        (await manager.GetAsync(Api($"/api/v1/quotes/{quote}/signature"))).StatusCode.Should().Be(HttpStatusCode.NoContent, "pas encore signé en ligne");
        (await manager.GetFromJsonAsync(Api($"/api/v1/portal-links?customerId={customer}"), ContractsJsonContext.Default.IReadOnlyListPortalLinkSummary))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Les_liens_exigent_leurs_permissions()
    {
        using HttpClient collaborator = InDossier(factory.ClientFor(LettraApiFactory.CollaborateurId));

        using HttpResponseMessage created = await collaborator.PostAsJsonAsync(Api("/api/v1/portal-links"),
            new CreatePortalLinkRequest { CustomerId = Guid.CreateVersion7() }, ContractsJsonContext.Default.CreatePortalLinkRequest);

        created.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await collaborator.PostAsync(Api($"/api/v1/portal-links/{Guid.CreateVersion7()}/revoke"), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
