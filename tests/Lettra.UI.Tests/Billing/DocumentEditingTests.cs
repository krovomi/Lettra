using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Pricing;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared.Billing;
using Lettra.UI.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace Lettra.UI.Tests.Billing;

public sealed class DocumentEditingTests
{
    [Fact]
    public async Task La_langue_du_pdf_suit_le_client_et_ne_change_pas_sans_client_ni_annuaire()
    {
        FakeCustomersGateway customers = new();
        Guid sans = Guid.CreateVersion7();
        Guid anglais = Guid.CreateVersion7();
        customers.Customers.Add(new CustomerSummary(sans, CustomerKindDto.Company, "Atelier Dupont", "552100554", "Nantes"));
        customers.Customers.Add(new CustomerSummary(anglais, CustomerKindDto.Company, "London Ltd", "732829320", "London"));
        customers.Languages[anglais] = "en-GB";
        Optional<ICustomersGateway> present = Optional<ICustomersGateway>.When(() => customers);

        (await DocumentEditing.PdfCultureOfAsync(present, anglais)).Should().Be("en-GB");
        (await DocumentEditing.PdfCultureOfAsync(present, sans)).Should().Be(DocumentEditing.DefaultPdfCulture);
        (await DocumentEditing.PdfCultureOfAsync(present, Guid.Empty)).Should().BeNull();
        (await DocumentEditing.PdfCultureOfAsync(Optional<ICustomersGateway>.When(() => null), anglais)).Should().BeNull();
    }

    [Fact]
    public async Task Le_prix_vient_de_la_grille_du_client_et_rien_sans_client_ni_grille()
    {
        Guid article = Guid.CreateVersion7();
        Guid customer = Guid.CreateVersion7();
        FakePriceListsGateway prices = new();
        prices.Prices[article] = new ResolvedPrice(article, 80m, 0m, "TVA20", null);
        Optional<IPriceListsGateway> present = Optional<IPriceListsGateway>.When(() => prices);

        (await DocumentEditing.ResolvePriceAsync(present, customer, article))!.UnitPriceExclTax.Should().Be(80m);
        (await DocumentEditing.ResolvePriceAsync(present, customer, Guid.CreateVersion7())).Should().BeNull();
        (await DocumentEditing.ResolvePriceAsync(present, Guid.Empty, article)).Should().BeNull();
        (await DocumentEditing.ResolvePriceAsync(Optional<IPriceListsGateway>.When(() => null), customer, article)).Should().BeNull();
    }

    [Fact]
    public void Seuls_les_articles_du_catalogue_presents_sur_la_piece_sont_rendus()
    {
        Guid article = Guid.CreateVersion7();
        DocumentLineRow libre = DocumentLineRow.New(new VatRateOption("TVA20", 20m));
        DocumentLineRow catalogue = libre with { Key = Guid.NewGuid(), CatalogItemId = article };

        DocumentEditing.PresentItems([catalogue, libre]).Should().Equal(article);
    }

    [Theory]
    [InlineData("Mention.Seller.Siren")]
    [InlineData(ErrorCodes.Mention.LatePenalties)]
    [InlineData(ErrorCodes.Mention.VatOnDebits)]
    public async Task Une_mention_du_cabinet_mene_aux_reglages(string code)
    {
        RecordingNavigation navigation = new();
        List<string> own = [];

        await DocumentEditing.FocusFindingAsync(Finding(code), new(navigation, null, null, null, null), Own(own, handles: true));

        navigation.Last.Should().Be("https://lettra.test/settings/company");
        own.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_champ_propre_a_la_piece_passe_avant_les_lignes()
    {
        RecordingNavigation navigation = new();
        List<string> own = [];

        await DocumentEditing.FocusFindingAsync(Finding(ErrorCodes.Mention.DueDate), new(navigation, null, null, null, null), Own(own, handles: true));
        await DocumentEditing.FocusFindingAsync(Finding("Line.Designation"), new(navigation, null, null, null, null), Own(own, handles: false));
        await DocumentEditing.FocusFindingAsync(Finding("Mention.Customer.Address"), new(navigation, null, null, null, null), Own(own, handles: true));

        own.Should().Equal(ErrorCodes.Mention.DueDate, "Line.Designation");
        navigation.Last.Should().BeNull();
    }

    private static ComplianceFindingResponse Finding(string code) => new(code, ComplianceSeverityDto.Error, null);

    private static Func<string, Task<bool>> Own(List<string> asked, bool handles) => code =>
    {
        asked.Add(code);
        return Task.FromResult(handles);
    };

    private sealed class RecordingNavigation : NavigationManager
    {
        public RecordingNavigation() => Initialize("https://lettra.test/", "https://lettra.test/");

        public string? Last { get; private set; }

        protected override void NavigateToCore(string uri, NavigationOptions options) => Last = ToAbsoluteUri(uri).ToString();
    }
}
