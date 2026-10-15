using System.Globalization;
using Bunit;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Billing;
using Lettra.UI.Shared.Components.DataEntryGrid;
using Lettra.UI.Shared.Components.Sync;
using Lettra.UI.Shared.Drafts;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Billing;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Billing;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Sync;

/// <summary>IndexedDB du navigateur, en mémoire : elle survit au « rechargement » (un nouveau service sur le même magasin).</summary>
internal sealed class InMemoryDraftStore : IOfflineDraftStore
{
    public Dictionary<Guid, OfflineDraft> Drafts { get; } = [];

    public Dictionary<string, string> Cache { get; } = [];

    public Task<IReadOnlyList<OfflineDraft>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OfflineDraft>>([.. Drafts.Values]);

    public Task SaveAsync(OfflineDraft draft, CancellationToken cancellationToken)
    {
        Drafts[draft.LocalId] = draft;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid localId, CancellationToken cancellationToken)
    {
        Drafts.Remove(localId);
        return Task.CompletedTask;
    }

    public Task<string?> ReadCacheAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(Cache.TryGetValue(key, out string? value) ? value : null);

    public Task WriteCacheAsync(string key, string value, CancellationToken cancellationToken)
    {
        Cache[key] = value;
        return Task.CompletedTask;
    }
}

public sealed class OfflineDraftsTests : IDisposable
{
    private static readonly DocumentLineRequest Conseil = new() { Designation = "Conseil", UnitCode = "HUR", Quantity = 2m, UnitPriceExclTax = 100m };

    private readonly InMemoryDraftStore _store = new();
    private readonly FakeQuotesGateway _quotes = new();
    private readonly FakeInvoicesGateway _invoices = new();
    private readonly ServiceProvider _services;
    private OfflineDrafts _drafts;
    private DateTimeOffset _now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    public OfflineDraftsTests()
    {
        _services = new ServiceCollection()
            .AddSingleton<IQuotesGateway>(_quotes)
            .AddSingleton<IInvoicesGateway>(_invoices)
            .BuildServiceProvider();
        _drafts = Recharger();
    }

    private OfflineDrafts Recharger() => new(_store, _services, new StepClock(() => _now += TimeSpan.FromMinutes(1)));

    private static SaveQuoteRequest Devis(params DocumentLineRequest[] lines) =>
        new() { CustomerId = BillingFixtures.DupontId, VatRegime = VatRegimeDto.Standard, Lines = [.. lines] };

    [Fact]
    public async Task Les_brouillons_survivent_au_rechargement_et_partent_au_retour_du_reseau_les_plus_anciens_d_abord()
    {
        await _drafts.KeepQuoteAsync(null, null, Devis(Conseil), "Dupont SARL", CancellationToken.None);
        await _drafts.KeepInvoiceAsync(null, null, new SaveInvoiceRequest { CustomerId = BillingFixtures.DupontId, Lines = [Conseil] }, "Dupont SARL",
            CancellationToken.None);
        _drafts.Dispose();
        _drafts = Recharger();
        List<OfflineDraftSynced> arrives = [];
        _drafts.Synced += (_, e) => arrives.Add(e);

        OfflineDraftSyncReport bilan = await _drafts.SyncAsync(CancellationToken.None);

        bilan.Should().Be(new OfflineDraftSyncReport(2, 0, false));
        arrives.Select(a => a.Kind).Should().Equal(OfflineDraftKind.Quote, OfflineDraftKind.Invoice);
        _quotes.Creations.Should().ContainSingle().Which.Lines.Should().ContainSingle().Which.Designation.Should().Be("Conseil");
        _invoices.Invoices.Should().ContainSingle();
        _store.Drafts.Should().BeEmpty();
        _drafts.SyncedServerId(arrives[0].LocalId).Should().Be(_quotes.Quotes.Keys.Single());
    }

    [Fact]
    public async Task Une_copie_alteree_sur_l_appareil_n_est_pas_envoyee()
    {
        Guid id = await _drafts.KeepQuoteAsync(null, null, Devis(Conseil), "Dupont SARL", CancellationToken.None);
        _store.Drafts[id] = _store.Drafts[id] with { Payload = _store.Drafts[id].Payload.Replace("100", "1", StringComparison.Ordinal) };

        OfflineDraftSyncReport bilan = await _drafts.SyncAsync(CancellationToken.None);

        bilan.Should().Be(new OfflineDraftSyncReport(0, 1, false));
        _quotes.Creations.Should().BeEmpty();
        _store.Drafts[id].LastError.Should().Be(OfflineDrafts.CorruptedCode);
    }

    [Fact]
    public async Task Sans_reseau_l_envoi_s_arrete_et_garde_tout()
    {
        await _drafts.KeepQuoteAsync(null, null, Devis(Conseil), null, CancellationToken.None);
        await _drafts.KeepQuoteAsync(null, null, Devis(Conseil), null, CancellationToken.None);
        _quotes.Offline = true;

        OfflineDraftSyncReport bilan = await _drafts.SyncAsync(CancellationToken.None);

        bilan.Should().Be(new OfflineDraftSyncReport(0, 0, true));
        _store.Drafts.Values.Should().HaveCount(2).And.OnlyContain(d => d.LastError == null);
    }

    [Fact]
    public async Task Une_piece_modifiee_au_cabinet_entre_temps_reste_sur_l_appareil_avec_le_conflit()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Conseil);
        Guid id = await _drafts.KeepQuoteAsync(null, devis, Devis(Conseil with { Quantity = 3m }), "Dupont SARL", CancellationToken.None);
        // Seconde saisie hors ligne : la version de base reste celle de la première.
        await _drafts.KeepQuoteAsync(id, devis with { Version = 7 }, Devis(Conseil with { Quantity = 4m }), null, CancellationToken.None);
        _store.Drafts[id].BaseVersion.Should().Be(1);
        _quotes.Quotes[devis.Id] = _quotes.Quotes[devis.Id] with { Version = 2 };

        OfflineDraftSyncReport bilan = await _drafts.SyncAsync(CancellationToken.None);

        bilan.Should().Be(new OfflineDraftSyncReport(0, 1, false));
        _quotes.Updates.Should().ContainSingle().Which.Version.Should().Be(1);
        _store.Drafts[id].LastError.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
    }

    [Fact]
    public async Task Une_piece_existante_part_sur_sa_version_et_la_copie_disparait()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Conseil);
        await _drafts.KeepQuoteAsync(null, devis, Devis(Conseil with { Quantity = 3m }), "Dupont SARL", CancellationToken.None);

        (await _drafts.SyncAsync(CancellationToken.None)).Sent.Should().Be(1);

        _quotes.Quotes[devis.Id].Version.Should().Be(2);
        _quotes.Quotes[devis.Id].Lines.Single().Quantity.Should().Be(3m);
        _store.Drafts.Should().BeEmpty();
    }

    [Fact]
    public async Task Les_donnees_de_reference_lues_en_ligne_servent_hors_ligne()
    {
        OperationResult<BillingReferenceData> enLigne = OperationResult<BillingReferenceData>.Success(BillingFixtures.Reference);
        await _drafts.CachedAsync(OfflineDrafts.ReferenceDataKey, enLigne, Contracts.Serialization.ContractsJsonContext.Default.BillingReferenceData,
            CancellationToken.None);

        BillingReferenceData? horsLigne = await _drafts.CachedAsync(OfflineDrafts.ReferenceDataKey,
            OperationResult<BillingReferenceData>.Failure(ErrorCodes.Api.Unreachable),
            Contracts.Serialization.ContractsJsonContext.Default.BillingReferenceData, CancellationToken.None);
        BillingReferenceData? refuse = await _drafts.CachedAsync(OfflineDrafts.ReferenceDataKey,
            OperationResult<BillingReferenceData>.Failure(ErrorCodes.Security.Forbidden),
            Contracts.Serialization.ContractsJsonContext.Default.BillingReferenceData, CancellationToken.None);

        horsLigne!.VatRates.Select(r => r.Code).Should().Equal(BillingFixtures.Reference.VatRates.Select(r => r.Code));
        refuse.Should().BeNull("seul un serveur injoignable rend la copie gardée");
    }

    public void Dispose()
    {
        _drafts.Dispose();
        _services.Dispose();
    }

    private sealed class StepClock(Func<DateTimeOffset> next) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => next();
    }
}

[Collection("culture")]
public sealed class OfflineDraftEditorTests : BunitContext
{
    private const int Designation = 1, Prix = 4;

    private readonly InMemoryDraftStore _store = new();
    private readonly FakeQuotesGateway _quotes = new();
    private readonly FakeInvoicesGateway _invoices = new();
    private readonly FakeBillingSettingsGateway _settings = new();

    private static readonly DocumentLineRequest Conseil = new() { Designation = "Conseil", UnitCode = "HUR", Quantity = 2m, UnitPriceExclTax = 100m };

    public OfflineDraftEditorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IOfflineDraftStore>(_store);
        Services.AddOfflineDrafts();
        Services.AddSingleton<IQuotesGateway>(_quotes);
        Services.AddSingleton<IInvoicesGateway>(_invoices);
        Services.AddSingleton<IBillingSettingsGateway>(_settings);
        Services.AddSingleton<ICatalogGateway>(new FakeCatalogGateway());
        FakeCustomersGateway customers = new();
        customers.Customers.Add(new CustomerSummary(BillingFixtures.DupontId, CustomerKindDto.Company, "Dupont SARL", "732829320", "Lyon"));
        Services.AddSingleton<ICustomersGateway>(customers);
        Services.AddSingleton<IPriceListsGateway>(new FakePriceListsGateway());
        Services.AddSingleton<IFileDownloader>(new FakeFileDownloader());
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private static Task<CellCommitResult> Saisir(IRenderedComponent<QuoteEditor> page, int row, int col, string text)
    {
        IRenderedComponent<DataEntryGrid<DocumentLineRow>> grille = page.FindComponent<DataEntryGrid<DocumentLineRow>>();
        return page.InvokeAsync(() => grille.Instance.OnCellCommitted(row, col, text));
    }

    private static Task<CellCommitResult> Saisir(IRenderedComponent<InvoiceEditor> page, int row, int col, string text)
    {
        IRenderedComponent<DataEntryGrid<DocumentLineRow>> grille = page.FindComponent<DataEntryGrid<DocumentLineRow>>();
        return page.InvokeAsync(() => grille.Instance.OnCellCommitted(row, col, text));
    }

    [Fact]
    public async Task Un_nouveau_devis_sans_reseau_reste_sur_l_appareil_se_rouvre_et_part_au_retour_du_reseau()
    {
        // En ligne une première fois : les données de référence sont gardées pour la suite.
        Render<QuoteEditor>(p => p.Add(e => e.AutosaveDelay, TimeSpan.Zero)).WaitForAssertion(() => _store.Cache.Should().HaveCount(2));
        await DisposeComponentsAsync();
        _settings.Offline = _quotes.Offline = true;
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"quotes/new?customer={BillingFixtures.DupontId}");
        IRenderedComponent<QuoteEditor> page = Render<QuoteEditor>(p => p.Add(e => e.AutosaveDelay, TimeSpan.Zero));
        IRenderedComponent<DataEntryGrid<DocumentLineRow>> grille = page.FindComponent<DataEntryGrid<DocumentLineRow>>();
        await page.InvokeAsync(() => grille.Instance.OnCommand("Insert", 0, 0));
        await Saisir(page, 0, Prix, "1 200");

        await Saisir(page, 0, Designation, "Tenue de comptabilité");

        OfflineDraft brouillon = _store.Drafts.Values.Should().ContainSingle().Subject;
        brouillon.Kind.Should().Be(OfflineDraftKind.Quote);
        brouillon.Title.Should().Be("Dupont SARL");
        navigation.Uri.Should().EndWith($"/quotes/draft/{brouillon.LocalId}");

        // Rechargement, toujours sans réseau : la saisie revient depuis l'appareil.
        await DisposeComponentsAsync();
        page = Render<QuoteEditor>(p => p.Add(e => e.LocalId, brouillon.LocalId).Add(e => e.AutosaveDelay, TimeSpan.Zero));
        page.WaitForAssertion(() => page.Find("#document-status").TextContent.Should().Contain("repris de cet appareil"));
        page.FindAll(".lg-row[data-r]").Should().HaveCount(1);
        page.Find("#total-excl-tax").TextContent.Trim().Should().Be(GridNumber.FormatAmount(1200m, CultureInfo.GetCultureInfo("fr-FR")));

        // Le bandeau tente l'envoi à l'ouverture : toujours pas de réseau, le brouillon attend.
        IRenderedComponent<OfflineDraftsBanner> bandeau = Render<OfflineDraftsBanner>();
        bandeau.WaitForAssertion(() => bandeau.Find("#offline-drafts").TextContent.Should().Contain("1 brouillon"));
        _quotes.Creations.Should().BeEmpty();

        // Retour du réseau : le bandeau l'envoie et l'éditeur passe au devis du serveur.
        _settings.Offline = _quotes.Offline = false;
        bandeau.Find("#offline-drafts-send").Click();

        bandeau.WaitForAssertion(() => _store.Drafts.Should().BeEmpty());
        _quotes.Creations.Should().ContainSingle().Which.Lines.Should().ContainSingle().Which.UnitPriceExclTax.Should().Be(1200m);
        navigation.Uri.Should().EndWith($"/quotes/{_quotes.Quotes.Keys.Single()}");
        bandeau.FindAll("#offline-drafts").Should().BeEmpty();
    }

    [Fact]
    public async Task Une_facture_modifiee_sans_reseau_reste_sur_l_appareil_puis_s_enregistre_en_ligne()
    {
        InvoiceResponse facture = _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Draft, null, Conseil);
        IRenderedComponent<InvoiceEditor> page = Render<InvoiceEditor>(p => p.Add(e => e.Id, facture.Id).Add(e => e.AutosaveDelay, TimeSpan.Zero));
        page.WaitForAssertion(() => page.FindAll(".lg-row[data-r]").Should().HaveCount(1));
        _invoices.Offline = true;

        await Saisir(page, 0, Prix, "150");

        OfflineDraft brouillon = _store.Drafts.Values.Should().ContainSingle().Subject;
        brouillon.ServerId.Should().Be(facture.Id);
        brouillon.BaseVersion.Should().Be(1);
        page.WaitForAssertion(() => page.Find("#document-status").TextContent.Should().Contain("enregistré sur cet appareil"));

        _invoices.Offline = false;
        await Saisir(page, 0, Prix, "160");

        page.WaitForAssertion(() => _store.Drafts.Should().BeEmpty("l'enregistrement en ligne remplace la copie de l'appareil"));
        _invoices.Invoices[facture.Id].Lines.Single().UnitPriceExclTax.Should().Be(160m);
    }

    [Fact]
    public async Task Une_piece_rouverte_en_ligne_reprend_la_saisie_gardee_sur_l_appareil()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Conseil);
        await Services.GetRequiredService<OfflineDrafts>().KeepQuoteAsync(null, devis,
            new SaveQuoteRequest { CustomerId = devis.CustomerId, VatRegime = VatRegimeDto.Standard, Lines = [Conseil with { Quantity = 5m }] },
            "Dupont SARL", CancellationToken.None);

        IRenderedComponent<QuoteEditor> page = Render<QuoteEditor>(p => p.Add(e => e.Id, devis.Id).Add(e => e.AutosaveDelay, TimeSpan.Zero));

        page.WaitForAssertion(() => page.Find("#document-status").TextContent.Should().Contain("repris de cet appareil"));
        page.Find("#total-excl-tax").TextContent.Trim().Should().Be(GridNumber.FormatAmount(500m, CultureInfo.GetCultureInfo("fr-FR")));
    }
}
