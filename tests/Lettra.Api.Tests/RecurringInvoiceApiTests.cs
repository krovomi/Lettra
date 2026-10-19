using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Serialization;
using Lettra.Domain.SharedKernel;

namespace Lettra.Api.Tests;

/// <summary>
/// Factures récurrentes par l'API réelle (mode 2, SQLite) : modèle, factures dues créées en brouillon ou validées avec
/// leur numéro et leur période (BT-73/74), pas de doublon d'une passe à l'autre, validation refusée laissée en
/// brouillon, échéancier figé, concurrence (If-Match), filtres et tris de la liste, droits.
/// </summary>
public sealed class RecurringInvoiceApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static Uri Api(string path) => new(path, UriKind.Relative);

    private static HttpClient InDossier(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    /// <summary>Trois mois à échoir, du premier du mois il y a deux mois au dernier jour du mois courant : trois factures dues.</summary>
    private static DateOnly Start
    {
        get
        {
            DateOnly today = LegalCalendar.DateOf(DateTimeOffset.UtcNow);
            return new DateOnly(today.Year, today.Month, 1).AddMonths(-2);
        }
    }

    private static SaveRecurringInvoiceRequest Template(Guid customerId, string name, bool validate = false) => new()
    {
        Name = name,
        CustomerId = customerId,
        Subject = "Tenue comptable",
        PaymentTerms = "Virement à 30 jours",
        Lines = [new DocumentLineRequest { Designation = "Forfait mensuel", Quantity = 1, UnitPriceExclTax = 250m, UnitCode = "C62", Kind = CatalogItemKindDto.Service }],
        Frequency = RecurrenceFrequencyDto.Monthly,
        Timing = BillingTimingDto.InAdvance,
        StartDate = Start,
        EndDate = Start.AddMonths(3).AddDays(-1),
        ValidateAutomatically = validate,
    };

    private static async Task<Guid> CustomerAsync(HttpClient client, string name)
    {
        await ExportApiTests.ValidatedInvoiceAsync(client);
        CreateCustomerRequest request = CustomerApiTests.Martin();
        request.Name = name;
        using HttpResponseMessage created = await client.PostAsJsonAsync(Api("/api/v1/customers"), request, ContractsJsonContext.Default.CreateCustomerRequest);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync(ContractsJsonContext.Default.CustomerResponse))!.Id;
    }

    private static async Task<RecurringInvoiceResponse> CreateAsync(HttpClient client, SaveRecurringInvoiceRequest request)
    {
        using HttpResponseMessage created = await client.PostAsJsonAsync(Api("/api/v1/recurring-invoices"), request, ContractsJsonContext.Default.SaveRecurringInvoiceRequest);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        created.Headers.ETag.Should().NotBeNull();
        return (await created.Content.ReadFromJsonAsync(ContractsJsonContext.Default.RecurringInvoiceResponse))!;
    }

    private static async Task<RecurringRunResponse> RunAsync(HttpClient client, Guid id)
    {
        using HttpResponseMessage run = await client.PostAsync(Api($"/api/v1/recurring-invoices/{id}/run"), null);
        run.StatusCode.Should().Be(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());
        return (await run.Content.ReadFromJsonAsync(ContractsJsonContext.Default.RecurringRunResponse))!;
    }

    private static async Task<RecurringInvoiceResponse> GetAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync(Api($"/api/v1/recurring-invoices/{id}"), ContractsJsonContext.Default.RecurringInvoiceResponse))!;

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, string path, HttpContent content, long? version)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, Api(path)) { Content = content };
        if (version is { } v)
        {
            request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{v}\""));
        }

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Les_factures_dues_sont_creees_en_brouillon_avec_leur_periode_et_une_seule_fois()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        Guid customer = await CustomerAsync(manager, "Cabinet Brouillon");
        RecurringInvoiceResponse template = await CreateAsync(manager, Template(customer, "Forfait brouillon"));
        template.IsDue.Should().BeTrue();
        template.NextPeriodStart.Should().Be(Start);
        template.CustomerName.Should().Be("Cabinet Brouillon");
        template.Totals.TotalExclTax.Should().Be(250m);

        RecurringRunResponse run = await RunAsync(manager, template.Id);

        run.CreatedCount.Should().Be(3);
        run.ValidatedCount.Should().Be(0);
        run.Created.Select(c => c.PeriodStart).Should().Equal(Start, Start.AddMonths(1), Start.AddMonths(2));
        InvoiceResponse first = (await manager.GetFromJsonAsync(Api($"/api/v1/invoices/{run.Created[0].InvoiceId}"), ContractsJsonContext.Default.InvoiceResponse))!;
        first.Status.Should().Be(InvoiceStatusDto.Draft);
        first.Number.Should().BeNull();
        first.BillingPeriodStart.Should().Be(Start);
        first.BillingPeriodEnd.Should().Be(Start.AddMonths(1).AddDays(-1));
        first.Subject.Should().StartWith("Tenue comptable (du ");

        (await RunAsync(manager, template.Id)).CreatedCount.Should().Be(0, "une période déjà facturée ne l'est pas deux fois");
        RecurringInvoiceResponse after = await GetAsync(manager, template.Id);
        after.Occurrences.Should().HaveCount(3);
        after.Occurrences[0].PeriodStart.Should().Be(Start.AddMonths(2), "la plus récente en premier");
        after.Occurrences.Should().OnlyContain(o => o.InvoiceStatus == InvoiceStatusDto.Draft);
        after.IsDue.Should().BeFalse();
        after.NextPeriodStart.Should().BeNull("la date de fin est atteinte");
    }

    [Fact]
    public async Task La_validation_automatique_numerote_les_factures_et_les_exporte_avec_leur_periode()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        Guid customer = await CustomerAsync(manager, "Cabinet Validé");
        RecurringInvoiceResponse template = await CreateAsync(manager, Template(customer, "Forfait validé", validate: true));

        RecurringRunResponse run = await RunAsync(manager, template.Id);

        run.ValidatedCount.Should().Be(3);
        run.Created.Should().OnlyContain(c => c.Validated && c.Number != null && c.ValidationError == null);
        run.Created.Select(c => c.Number).Should().BeInAscendingOrder(StringComparer.Ordinal);
        (await GetAsync(manager, template.Id)).Occurrences.Should().OnlyContain(o => o.InvoiceStatus == InvoiceStatusDto.Validated && o.InvoiceNumber != null);

        string ubl = await manager.GetStringAsync(Api($"/api/v1/invoices/{run.Created[0].InvoiceId}/e-invoice?format=Ubl"));
        ubl.Should().Contain("<cac:InvoicePeriod>").And.Contain($"<cbc:StartDate>{Start:yyyy-MM-dd}</cbc:StartDate>");
    }

    [Fact]
    public async Task Sans_droit_de_valider_les_factures_restent_en_brouillon_et_l_erreur_est_rendue()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        Guid customer = await CustomerAsync(manager, "Cabinet Collaborateur");
        RecurringInvoiceResponse template = await CreateAsync(manager, Template(customer, "Forfait collaborateur", validate: true));
        using HttpClient collaborator = InDossier(factory.ClientFor(LettraApiFactory.CollaborateurId));

        RecurringRunResponse run = await RunAsync(collaborator, template.Id);

        run.CreatedCount.Should().Be(3);
        run.ValidatedCount.Should().Be(0);
        run.Created.Should().OnlyContain(c => !c.Validated && c.Number == null && c.ValidationError != null);
    }

    [Fact]
    public async Task L_ecriture_exige_la_bonne_version_et_l_echeancier_se_fige_apres_une_facture()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        Guid customer = await CustomerAsync(manager, "Cabinet Versions");
        RecurringInvoiceResponse template = await CreateAsync(manager, Template(customer, "Forfait versions"));
        string path = $"/api/v1/recurring-invoices/{template.Id}";
        SaveRecurringInvoiceRequest changed = Template(customer, "Forfait renommé");

        using HttpResponseMessage missing = await PutAsync(manager, path, JsonContent.Create(changed, ContractsJsonContext.Default.SaveRecurringInvoiceRequest), null);
        missing.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
        using HttpResponseMessage stale = await PutAsync(manager, path, JsonContent.Create(changed, ContractsJsonContext.Default.SaveRecurringInvoiceRequest), template.Version + 5);
        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        using HttpResponseMessage renamed = await PutAsync(manager, path, JsonContent.Create(changed, ContractsJsonContext.Default.SaveRecurringInvoiceRequest), template.Version);
        renamed.StatusCode.Should().Be(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());
        (await renamed.Content.ReadFromJsonAsync(ContractsJsonContext.Default.RecurringInvoiceResponse))!.Name.Should().Be("Forfait renommé");

        await RunAsync(manager, template.Id);
        RecurringInvoiceResponse generated = await GetAsync(manager, template.Id);
        changed.StartDate = Start.AddDays(1);
        using HttpResponseMessage locked = await PutAsync(manager, path, JsonContent.Create(changed, ContractsJsonContext.Default.SaveRecurringInvoiceRequest), generated.Version);
        locked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await locked.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.RecurringInvoice.ScheduleLocked);
    }

    [Fact]
    public async Task Un_modele_se_met_en_pause_se_filtre_se_trie_et_se_supprime()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        Guid customer = await CustomerAsync(manager, "Cabinet Liste");
        RecurringInvoiceResponse alpha = await CreateAsync(manager, Template(customer, "Liste Alpha"));
        RecurringInvoiceResponse beta = await CreateAsync(manager, Template(customer, "Liste Beta"));

        using HttpResponseMessage paused = await PutAsync(manager, $"/api/v1/recurring-invoices/{beta.Id}/active",
            JsonContent.Create(new SetRecurringInvoiceActiveRequest { IsActive = false }, ContractsJsonContext.Default.SetRecurringInvoiceActiveRequest), beta.Version);
        paused.StatusCode.Should().Be(HttpStatusCode.OK, await paused.Content.ReadAsStringAsync());
        RecurringInvoiceResponse pausedBeta = (await paused.Content.ReadFromJsonAsync(ContractsJsonContext.Default.RecurringInvoiceResponse))!;
        pausedBeta.IsActive.Should().BeFalse();
        pausedBeta.NextIssueDate.Should().BeNull();

        PagedResponse<RecurringInvoiceSummary> byName = (await manager.GetFromJsonAsync(
            Api($"/api/v1/recurring-invoices?search=Liste&customerId={customer}&sort=Name&descending=true"), ContractsJsonContext.Default.PagedResponseRecurringInvoiceSummary))!;
        byName.Items.Select(i => i.Name).Should().Equal("Liste Beta", "Liste Alpha");
        PagedResponse<RecurringInvoiceSummary> due = (await manager.GetFromJsonAsync(
            Api($"/api/v1/recurring-invoices?customerId={customer}&dueOnly=true"), ContractsJsonContext.Default.PagedResponseRecurringInvoiceSummary))!;
        due.Items.Should().ContainSingle().Which.Id.Should().Be(alpha.Id);
        PagedResponse<RecurringInvoiceSummary> inactive = (await manager.GetFromJsonAsync(
            Api($"/api/v1/recurring-invoices?customerId={customer}&active=false&sort=Amount"), ContractsJsonContext.Default.PagedResponseRecurringInvoiceSummary))!;
        inactive.Items.Should().ContainSingle().Which.Id.Should().Be(beta.Id);
        (await RunAsync(manager, beta.Id)).CreatedCount.Should().Be(0, "un modèle en pause ne facture rien");

        using HttpRequestMessage delete = new(HttpMethod.Delete, Api($"/api/v1/recurring-invoices/{alpha.Id}"));
        delete.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{alpha.Version}\""));
        (await manager.SendAsync(delete)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await manager.GetAsync(Api($"/api/v1/recurring-invoices/{alpha.Id}"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task La_passe_du_dossier_cree_les_factures_de_tous_les_modeles_dus()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        Guid customer = await CustomerAsync(manager, "Cabinet Passe");
        RecurringInvoiceResponse template = await CreateAsync(manager, Template(customer, "Forfait passe"));

        using HttpResponseMessage run = await manager.PostAsync(Api("/api/v1/recurring-invoices/run"), null);

        run.StatusCode.Should().Be(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());
        RecurringRunResponse result = (await run.Content.ReadFromJsonAsync(ContractsJsonContext.Default.RecurringRunResponse))!;
        result.Created.Where(c => c.RecurringInvoiceId == template.Id).Should().HaveCount(3);
    }

    [Fact]
    public async Task Une_saisie_invalide_est_refusee_avec_ses_codes_et_le_portail_n_a_pas_acces()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        (await manager.PostAsJsonAsync(Api("/api/v1/recurring-invoices"), Template(Guid.CreateVersion7(), "Client inconnu"), ContractsJsonContext.Default.SaveRecurringInvoiceRequest))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        SaveRecurringInvoiceRequest invalid = Template(await CustomerAsync(manager, "Cabinet Invalide"), "Forfait invalide");
        invalid.EndDate = invalid.StartDate.AddDays(-1);
        invalid.Revision = PriceRevisionKindDto.SyntecIndex;

        using HttpResponseMessage refused = await manager.PostAsJsonAsync(Api("/api/v1/recurring-invoices"), invalid, ContractsJsonContext.Default.SaveRecurringInvoiceRequest);

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        string body = await refused.Content.ReadAsStringAsync();
        body.Should().Contain(ErrorCodes.RecurringInvoice.EndBeforeStart).And.Contain(ErrorCodes.RecurringInvoice.SyntecIndexInvalid);
        (await manager.GetAsync(Api($"/api/v1/recurring-invoices/{Guid.CreateVersion7()}"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await manager.PostAsync(Api($"/api/v1/recurring-invoices/{Guid.CreateVersion7()}/run"), null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using HttpClient portal = InDossier(factory.ClientFor(LettraApiFactory.PortailId));
        (await portal.PostAsync(Api("/api/v1/recurring-invoices/run"), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
