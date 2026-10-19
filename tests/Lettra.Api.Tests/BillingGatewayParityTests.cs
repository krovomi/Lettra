using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Demo;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Pricing;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Devis, factures, fiche entreprise et grilles tarifaires par la même interface, en HTTP et en direct : cycle de vie,
/// refus identiques, et conflit de version qui rend la pièce actuelle des deux côtés.
/// </summary>
public sealed class BillingGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<Guid> CustomerAsync(IServiceProvider services) =>
        (await services.GetRequiredService<ICustomersGateway>().CreateAsync(CustomerApiTests.Martin(), default)).Value!.Id;

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Un_devis_se_modifie_se_valide_s_accepte_puis_donne_un_acompte_et_sa_facture(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IQuotesGateway quotes = services.GetRequiredService<IQuotesGateway>();
        Guid customer = await CustomerAsync(services);
        await GatewayHarness.SaveCompanyProfileAsync(services);

        QuoteResponse draft = (await quotes.CreateAsync(new SaveQuoteRequest { CustomerId = customer, Subject = "Mission", Lines = [GatewayHarness.Conseil()] }, default)).Value!;
        OperationResult<QuoteResponse> updated = await quotes.UpdateAsync(
            draft.Id, new SaveQuoteRequest { CustomerId = customer, Subject = "Mission annuelle", Lines = [GatewayHarness.Conseil(2m)] }, draft.Version, default);
        OperationResult<QuoteResponse> stale = await quotes.UpdateAsync(
            draft.Id, new SaveQuoteRequest { CustomerId = customer, Lines = [GatewayHarness.Conseil()] }, draft.Version, default);
        OperationResult<ComplianceReportResponse> check = await quotes.CheckAsync(draft.Id, default);
        OperationResult<InvoiceResponse> tooEarly = await quotes.CreateInvoiceAsync(draft.Id, default);
        OperationResult<QuoteResponse> validated = await quotes.ValidateAsync(draft.Id, updated.Value!.Version, default);
        OperationResult<QuoteResponse> accepted = await quotes.SetOutcomeAsync(draft.Id, accepted: true, validated.Value!.Version, default);
        OperationResult<InvoiceResponse> downPayment = await quotes.CreateDownPaymentAsync(draft.Id, new CreateDownPaymentRequest { Percent = 30m }, default);
        OperationResult<InvoiceResponse> final = await quotes.CreateInvoiceAsync(draft.Id, default);

        updated.Value.Subject.Should().Be("Mission annuelle");
        updated.Value.Totals.TotalInclTax.Should().Be(240m);
        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        stale.Current!.Subject.Should().Be("Mission annuelle", "le refus porte le devis actuel, sur les deux transports");
        check.IsSuccess.Should().BeTrue(GatewayHarness.Codes(check));
        tooEarly.Errors.Select(e => e.Code).Should().ContainSingle().Which.Should().Be(ErrorCodes.Quote.NotAccepted);
        validated.Value.Number.Should().StartWith("DEV-");
        accepted.Value!.Status.Should().Be(QuoteStatusDto.Accepted);
        downPayment.IsSuccess.Should().BeTrue(GatewayHarness.Codes(downPayment));
        downPayment.Value!.Totals.TotalInclTax.Should().Be(72m);
        final.IsSuccess.Should().BeTrue(GatewayHarness.Codes(final));
        (await quotes.GetAsync(draft.Id, default)).Value!.FinalInvoiceId.Should().Be(final.Value!.Id);
        (await quotes.ListAsync(new BillingListQuery(Search: " ", CustomerId: customer), QuoteStatusDto.Invoiced, default)).Value!.Items
            .Should().ContainSingle(q => q.Id == draft.Id);
        (await quotes.ListAsync(new BillingListQuery(validated.Value.Number, From: Today.AddDays(-1), To: Today.AddDays(1)), null, default)).Value!.Items
            .Should().Contain(q => q.Id == draft.Id);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Un_devis_brouillon_se_supprime_et_un_devis_inconnu_est_refuse_de_meme(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IQuotesGateway quotes = services.GetRequiredService<IQuotesGateway>();
        Guid customer = await CustomerAsync(services);
        QuoteResponse draft = (await quotes.CreateAsync(new SaveQuoteRequest { CustomerId = customer, Lines = [GatewayHarness.Conseil()] }, default)).Value!;

        await StaleDeleteAsync(transport, () => quotes.DeleteAsync(draft.Id, draft.Version + 1, default));
        OperationResult<bool> deleted = await quotes.DeleteAsync(draft.Id, draft.Version, default);
        OperationResult<QuoteResponse> missing = await quotes.GetAsync(draft.Id, default);
        OperationResult<QuoteResponse> missingValidate = await quotes.ValidateAsync(Guid.CreateVersion7(), null, default);
        OperationResult<QuoteResponse> missingOutcome = await quotes.SetOutcomeAsync(Guid.CreateVersion7(), accepted: false, null, default);
        OperationResult<ComplianceReportResponse> missingCheck = await quotes.CheckAsync(Guid.CreateVersion7(), default);
        OperationResult<InvoiceResponse> missingDownPayment = await quotes.CreateDownPaymentAsync(Guid.CreateVersion7(), new CreateDownPaymentRequest { Percent = 10m }, default);

        deleted.Value.Should().BeTrue();
        missing.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Quote.NotFound);
        missingValidate.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Quote.NotFound);
        missingOutcome.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Quote.NotFound);
        missingCheck.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Quote.NotFound);
        missingDownPayment.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Quote.NotFound);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Une_facture_brouillon_se_modifie_se_controle_et_se_liste(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IInvoicesGateway invoices = services.GetRequiredService<IInvoicesGateway>();
        Guid customer = await CustomerAsync(services);
        await GatewayHarness.SaveCompanyProfileAsync(services);
        InvoiceResponse draft = (await invoices.CreateAsync(new SaveInvoiceRequest { CustomerId = customer, Lines = [GatewayHarness.Conseil()] }, default)).Value!;

        OperationResult<InvoiceResponse> updated = await invoices.UpdateAsync(
            draft.Id, new SaveInvoiceRequest { CustomerId = customer, Lines = [GatewayHarness.Conseil(3m)] }, draft.Version, default);
        OperationResult<InvoiceResponse> stale = await invoices.UpdateAsync(
            draft.Id, new SaveInvoiceRequest { CustomerId = customer, Lines = [GatewayHarness.Conseil()] }, draft.Version, default);
        OperationResult<ComplianceReportResponse> check = await invoices.CheckAsync(draft.Id, default);
        OperationResult<InvoiceResponse> notCredit = await invoices.ApproveCreditAsync(draft.Id, updated.Value!.Version, default);
        await StaleDeleteAsync(transport, () => invoices.DeleteAsync(draft.Id, draft.Version, default));
        OperationResult<PagedResponse<InvoiceSummary>> drafts = await invoices.ListAsync(
            new BillingListQuery(CustomerId: customer), InvoiceKindDto.Invoice, InvoiceStatusDto.Draft, default);

        updated.Value.Totals.TotalInclTax.Should().Be(360m);
        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        stale.Current!.Totals.TotalInclTax.Should().Be(360m, "le refus porte la facture actuelle, sur les deux transports");
        check.IsSuccess.Should().BeTrue(GatewayHarness.Codes(check));
        notCredit.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.SegregationOfDuties.NotACreditNote);
        drafts.Value!.Items.Should().ContainSingle(i => i.Id == draft.Id);
        (await invoices.ListAsync(new BillingListQuery(), null, InvoiceStatusDto.Issued, default)).Value!.Items.Should().NotContain(i => i.Id == draft.Id);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Une_facture_inconnue_est_refusee_de_meme(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IInvoicesGateway invoices = services.GetRequiredService<IInvoicesGateway>();
        Guid id = Guid.CreateVersion7();

        List<IReadOnlyList<ErrorDetail>> failures =
        [
            (await invoices.GetAsync(id, default)).Errors,
            (await invoices.UpdateAsync(id, new SaveInvoiceRequest { CustomerId = id, Lines = [GatewayHarness.Conseil()] }, 1, default)).Errors,
            (await invoices.CheckAsync(id, default)).Errors,
            (await invoices.ValidateAsync(id, null, default)).Errors,
            (await invoices.IssueAsync(id, null, default)).Errors,
            (await invoices.ApproveCreditAsync(id, null, default)).Errors,
            (await invoices.CreateCreditNoteAsync(id, new CreateCreditNoteRequest { Reason = "Erreur", Lines = [new CreditNoteLineRequest(1, 1m)] }, default)).Errors,
            (await invoices.GetEInvoiceAsync(id, EInvoiceFormatDto.Cii, default)).Errors,
            (await invoices.DeleteAsync(id, null, default)).Errors,
        ];

        failures.Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Invoice.NotFound));
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task La_fiche_entreprise_et_les_listes_de_reference_puis_un_conflit_de_version(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IBillingSettingsGateway settings = services.GetRequiredService<IBillingSettingsGateway>();
        await GatewayHarness.SaveCompanyProfileAsync(services);

        OperationResult<BillingReferenceData> data = await settings.GetReferenceDataAsync(default);
        CompanyProfileResponse profile = (await settings.GetCompanyProfileAsync(default)).Value!;
        SaveCompanyProfileRequest change = GatewayHarness.CompanyProfile();
        change.City = "Lyon";
        change.PostalCode = "69001";
        OperationResult<CompanyProfileResponse> stale = await settings.SaveCompanyProfileAsync(change, profile.Version + 1, default);
        OperationResult<CompanyProfileResponse> saved = await settings.SaveCompanyProfileAsync(change, profile.Version, default);
        OperationResult<CompanyProfileResponse> restored = await settings.SaveCompanyProfileAsync(GatewayHarness.CompanyProfile(), null, default);

        data.IsSuccess.Should().BeTrue(GatewayHarness.Codes(data));
        profile.Siren.Should().Be("552100554");
        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        stale.Current!.City.Should().Be("Paris", "le refus porte la fiche actuelle, sur les deux transports");
        saved.Value!.City.Should().Be("Lyon");
        restored.Value!.City.Should().Be("Paris");
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Une_grille_tarifaire_se_cree_s_applique_se_modifie_puis_se_supprime(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IPriceListsGateway lists = services.GetRequiredService<IPriceListsGateway>();
        Guid customer = await CustomerAsync(services);
        CatalogItemResponse item = (await services.GetRequiredService<ICatalogGateway>().CreateAsync(
            PartiesAndCatalogApiTests.Conseil("GRL-" + Guid.CreateVersion7().ToString("N")[^8..]), default)).Value!;
        SavePriceListRequest request = new()
        {
            Name = "Clients fidèles",
            CustomerIds = [customer],
            Entries = [new PriceListEntryRequest { CatalogItemId = item.Id, DiscountPercent = 10m }],
        };

        PriceListResponse created = (await lists.CreateAsync(request, default)).Value!;
        OperationResult<IReadOnlyList<ResolvedPrice>> resolved = await lists.ResolveAsync(customer, [item.Id], default);
        request.Name = "Clients fidèles 2026";
        OperationResult<PriceListResponse> updated = await lists.UpdateAsync(created.Id, request, created.Version, default);
        request.Name = "Trop tard";
        OperationResult<PriceListResponse> stale = await lists.UpdateAsync(created.Id, request, created.Version, default);
        OperationResult<PagedResponse<PriceListSummary>> page = await lists.ListAsync(1, 50, default);
        OperationResult<bool> deleted = await lists.DeleteAsync(created.Id, default);

        resolved.Value!.Should().ContainSingle().Which.Should().Be(new ResolvedPrice(item.Id, 85m, 10m, "FR-20", created.Id));
        updated.Value!.Name.Should().Be("Clients fidèles 2026");
        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        stale.Current!.Name.Should().Be("Clients fidèles 2026", "le refus porte la grille actuelle, sur les deux transports");
        page.Value!.Items.Should().Contain(p => p.Id == created.Id && p.CustomerCount == 1 && p.EntryCount == 1);
        deleted.Value.Should().BeTrue();
        (await lists.GetAsync(created.Id, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.PriceList.NotFound);
        (await lists.DeleteAsync(created.Id, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.PriceList.NotFound);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Une_grille_sans_nom_ou_vers_un_client_inconnu_est_refusee_de_meme(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IPriceListsGateway lists = services.GetRequiredService<IPriceListsGateway>();

        OperationResult<PriceListResponse> unnamed = await lists.CreateAsync(new SavePriceListRequest { Name = " " }, default);
        OperationResult<PriceListResponse> unknownCustomer = await lists.CreateAsync(new SavePriceListRequest { Name = "Fantôme", CustomerIds = [Guid.CreateVersion7()] }, default);

        unnamed.Errors.Select(e => e.Code).Should().ContainSingle().Which.Should().Be(ErrorCodes.PriceList.NameRequired);
        unknownCustomer.Errors.Select(e => e.Code).Should().ContainSingle().Which.Should().Be(ErrorCodes.PriceList.CustomerUnknown);
    }

    /// <summary>
    /// Suppression d'une pièce avec une version périmée. En direct : refus Concurrency.VersionMismatch. Par HTTP, ÉCART
    /// ACTUEL (bogue, non corrigé ici) : le 412 joint la pièce actuelle, que VersionedCalls.DeleteAsync
    /// (HttpBillingGateways.cs:33) fait relire comme un ProblemResponse par ToOperationResultAsync
    /// (HttpCustomersGateway.cs:97) ; le champ « status » texte du devis ou de la facture n'entre pas dans l'entier du
    /// ProblemResponse, et la JsonException remonte jusqu'à l'écran au lieu d'un refus.
    /// </summary>
    private static async Task StaleDeleteAsync(string transport, Func<Task<OperationResult<bool>>> delete)
    {
        if (transport == "http")
        {
            await delete.Should().ThrowAsync<System.Text.Json.JsonException>();
        }
        else
        {
            (await delete()).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        }
    }

    [Fact]
    public async Task Par_http_le_jeu_de_demonstration_est_ferme_sur_l_api_de_test()
    {
        // Pas de gateway de démonstration en direct : le poste isolé charge le jeu par sa propre composition.
        await using ServiceProvider services = _harness.HttpGateway(LettraApiFactory.DossierId);
        IDemoDataGateway demo = services.GetRequiredService<IDemoDataGateway>();

        OperationResult<DemoDataStatusResponse> status = await demo.GetStatusAsync(default);

        status.IsSuccess.Should().BeTrue(GatewayHarness.Codes(status));
        status.Value!.Available.Should().BeFalse("Lettra:Demo:Seed n'est pas activé");
    }

    [Fact]
    public async Task Serveur_injoignable_chaque_appel_rend_Api_Unreachable()
    {
        await using ServiceProvider services = GatewayHarness.Unreachable();
        IQuotesGateway quotes = services.GetRequiredService<IQuotesGateway>();
        IInvoicesGateway invoices = services.GetRequiredService<IInvoicesGateway>();
        IPlatformTransmissionGateway platform = services.GetRequiredService<IPlatformTransmissionGateway>();
        IBillingSettingsGateway settings = services.GetRequiredService<IBillingSettingsGateway>();
        IPriceListsGateway lists = services.GetRequiredService<IPriceListsGateway>();
        IReceivablesGateway receivables = services.GetRequiredService<IReceivablesGateway>();
        IDemoDataGateway demo = services.GetRequiredService<IDemoDataGateway>();
        Guid id = Guid.CreateVersion7();
        BillingListQuery query = new("x", id, Today, Today);

        List<IReadOnlyList<ErrorDetail>> failures =
        [
            (await quotes.ListAsync(query, QuoteStatusDto.Draft, default)).Errors,
            (await quotes.GetAsync(id, default)).Errors,
            (await quotes.CreateAsync(new SaveQuoteRequest(), default)).Errors,
            (await quotes.UpdateAsync(id, new SaveQuoteRequest(), 1, default)).Errors,
            (await quotes.DeleteAsync(id, null, default)).Errors,
            (await quotes.CheckAsync(id, default)).Errors,
            (await quotes.ValidateAsync(id, 1, default)).Errors,
            (await quotes.SetOutcomeAsync(id, accepted: true, 1, default)).Errors,
            (await quotes.CreateDownPaymentAsync(id, new CreateDownPaymentRequest(), default)).Errors,
            (await quotes.CreateInvoiceAsync(id, default)).Errors,
            (await invoices.ListAsync(query, InvoiceKindDto.CreditNote, InvoiceStatusDto.Issued, default)).Errors,
            (await invoices.GetAsync(id, default)).Errors,
            (await invoices.CreateAsync(new SaveInvoiceRequest(), default)).Errors,
            (await invoices.UpdateAsync(id, new SaveInvoiceRequest(), 1, default)).Errors,
            (await invoices.DeleteAsync(id, 1, default)).Errors,
            (await invoices.CheckAsync(id, default)).Errors,
            (await invoices.ValidateAsync(id, 1, default)).Errors,
            (await invoices.IssueAsync(id, 1, default)).Errors,
            (await invoices.CreateCreditNoteAsync(id, new CreateCreditNoteRequest(), default)).Errors,
            (await invoices.ApproveCreditAsync(id, 1, default)).Errors,
            (await invoices.GetEInvoiceAsync(id, EInvoiceFormatDto.Ubl, default)).Errors,
            (await platform.GetAsync(id, default)).Errors,
            (await platform.TransmitAsync(id, new TransmitInvoiceRequest(), default)).Errors,
            (await platform.RefreshAsync(id, default)).Errors,
            (await platform.ReportPaymentAsync(id, new ReportPaymentRequest(), default)).Errors,
            (await settings.GetReferenceDataAsync(default)).Errors,
            (await settings.GetCompanyProfileAsync(default)).Errors,
            (await settings.SaveCompanyProfileAsync(GatewayHarness.CompanyProfile(), null, default)).Errors,
            (await lists.ListAsync(1, 10, default)).Errors,
            (await lists.GetAsync(id, default)).Errors,
            (await lists.CreateAsync(new SavePriceListRequest(), default)).Errors,
            (await lists.UpdateAsync(id, new SavePriceListRequest(), 1, default)).Errors,
            (await lists.DeleteAsync(id, default)).Errors,
            (await lists.ResolveAsync(id, [id, Guid.CreateVersion7()], default)).Errors,
            (await receivables.GetSummaryAsync(default)).Errors,
            (await demo.GetStatusAsync(default)).Errors,
            (await demo.SeedAsync(default)).Errors,
        ];

        failures.Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable));
    }

    public void Dispose() => _harness.Dispose();
}
