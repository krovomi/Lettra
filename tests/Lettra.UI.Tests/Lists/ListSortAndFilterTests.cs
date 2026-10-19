using System.Globalization;
using Bunit;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Lists;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Billing;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Cabinet;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Lists;

/// <summary>Briques de tri et de filtre des listes : état du tri, tri à l'écran, en-têtes, liste déroulante, fourchettes.</summary>
[Collection("culture")]
public sealed class ListSortAndFilterTests : BunitContext
{
    private enum Column
    {
        Name,
        Amount,
        Date,
    }

    private sealed record Row(string Name, decimal? Amount, DateOnly? Date);

    private static readonly IReadOnlyDictionary<Column, Func<Row, object?>> Keys = new Dictionary<Column, Func<Row, object?>>
    {
        [Column.Name] = r => r.Name,
        [Column.Amount] = r => r.Amount,
        [Column.Date] = r => r.Date,
    };

    public ListSortAndFilterTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    [Fact]
    public void Un_clic_sur_la_colonne_triee_inverse_le_sens_une_autre_colonne_part_de_son_sens_naturel()
    {
        SortState<Column> state = new(Column.Name);

        state.Toggle(Column.Name);
        state.Direction.Should().Be(SortDirectionDto.Descending);
        state.AriaSort(Column.Name).Should().Be("descending");
        state.IsChanged.Should().BeTrue();

        state.Toggle(Column.Amount, SortDirectionDto.Descending);
        state.Column.Should().Be(Column.Amount);
        state.Descending.Should().BeTrue();
        state.AriaSort(Column.Name).Should().Be("none");

        state.Toggle(Column.Amount);
        state.AriaSort(Column.Amount).Should().Be("ascending");

        state.Reset();
        state.Column.Should().Be(Column.Name);
        state.IsChanged.Should().BeFalse();
    }

    [Fact]
    public void Le_tri_a_l_ecran_ignore_casse_et_accents_et_laisse_les_vides_en_fin_de_liste()
    {
        Row[] rows = [new("élodie", null, new(2026, 1, 2)), new("Zoé", 5m, null), new("Elodie", 50m, new(2026, 3, 1)), new("adam", 10m, null)];

        ListSorting.Sort(rows, new SortState<Column>(Column.Name), Keys).Select(r => r.Name).Should()
            .Equal("adam", "élodie", "Elodie", "Zoé");
        ListSorting.Sort(rows, new SortState<Column>(Column.Amount, SortDirectionDto.Descending), Keys).Select(r => r.Amount).Should()
            .Equal(50m, 10m, 5m, null);
        ListSorting.Sort(rows, new SortState<Column>(Column.Amount), Keys).Select(r => r.Amount).Should()
            .Equal(5m, 10m, 50m, null);
        ListSorting.Sort(rows, new SortState<Column>(Column.Date), Keys).Select(r => r.Name).Should()
            .Equal(["élodie", "Elodie", "Zoé", "adam"], "les lignes sans date gardent leur ordre d'origine, en fin de liste");
        ListSorting.Sort(rows, new SortState<Column>(Column.Name), new Dictionary<Column, Func<Row, object?>>()).Should().Equal(rows);
    }

    [Fact]
    public void L_en_tete_triable_annonce_son_sens_et_previent_la_liste()
    {
        SortState<Column> state = new(Column.Name);
        int sorted = 0;
        IRenderedComponent<SortHeader<Column>> header = Render<SortHeader<Column>>(p => p
            .Add(h => h.Option, new SortOption<Column>(Column.Amount, "Montant", SortDirectionDto.Descending))
            .Add(h => h.State, state)
            .Add(h => h.Id, "sort-amount")
            .Add(h => h.OnSort, () => sorted++));

        header.Find("th").GetAttribute("aria-sort").Should().Be("none");
        header.Find("button").GetAttribute("title").Should().Be("Trier par Montant");

        header.Find("button").Click();
        header.Find("th").GetAttribute("aria-sort").Should().Be("descending");
        header.Find("button").ClassList.Should().Contain("is-active");
        header.Find("button").Click();
        header.Find("th").GetAttribute("aria-sort").Should().Be("ascending");
        sorted.Should().Be(2);
    }

    [Fact]
    public void La_liste_deroulante_de_tri_propose_chaque_colonne_dans_les_deux_sens()
    {
        SortState<Column> state = new(Column.Name);
        int sorted = 0;
        IRenderedComponent<SortSelect<Column>> select = Render<SortSelect<Column>>(p => p
            .Add(s => s.Options, [new SortOption<Column>(Column.Name, "Nom"), new SortOption<Column>(Column.Amount, "Montant", SortDirectionDto.Descending)])
            .Add(s => s.State, state)
            .Add(s => s.OnSort, () => sorted++));

        select.FindAll("option").Select(o => o.TextContent).Should().Equal("Nom, croissant", "Nom, décroissant", "Montant, décroissant", "Montant, croissant");
        select.Find(".lt-sort-select").ClassList.Should().Contain("is-phone-only");

        select.Find("select").Change("Amount:Ascending");
        state.Column.Should().Be(Column.Amount);
        state.Direction.Should().Be(SortDirectionDto.Ascending);
        select.Find("select").Change("n'importe quoi");
        sorted.Should().Be(1, "une valeur inconnue ne change pas le tri");
    }

    [Fact]
    public void Les_fourchettes_lisent_dates_et_montants_et_vident_une_borne_effacee()
    {
        DateOnly? from = null;
        DateOnly? to = new(2026, 12, 31);
        int changes = 0;
        IRenderedComponent<DateRangeFilter> dates = Render<DateRangeFilter>(p => p
            .Add(d => d.Id, "dates").Add(d => d.Label, "Date d'émission")
            .Add(d => d.From, from).Add(d => d.FromChanged, v => from = v)
            .Add(d => d.To, to).Add(d => d.ToChanged, v => to = v)
            .Add(d => d.OnChange, () => changes++));

        dates.Find("#dates-from").GetAttribute("max").Should().Be("2026-12-31");
        dates.Find("#dates-from").GetAttribute("aria-label").Should().Be("Date d'émission, à partir du");
        dates.Find("#dates-from").Change("2026-01-15");
        dates.Find("#dates-to").Change(string.Empty);
        from.Should().Be(new DateOnly(2026, 1, 15));
        to.Should().BeNull();

        decimal? min = null;
        decimal? max = 10m;
        IRenderedComponent<AmountRangeFilter> amounts = Render<AmountRangeFilter>(p => p
            .Add(a => a.Id, "amount").Add(a => a.Label, "Montant TTC")
            .Add(a => a.Min, min).Add(a => a.MinChanged, v => min = v)
            .Add(a => a.Max, max).Add(a => a.MaxChanged, v => max = v)
            .Add(a => a.OnChange, () => changes++));

        amounts.Find("#amount-max").GetAttribute("value").Should().Be("10");
        amounts.Find("#amount-min").Change("99,50");
        amounts.Find("#amount-max").Change("abc");
        min.Should().Be(99.50m);
        max.Should().BeNull();
        changes.Should().Be(4);
    }

    [Fact]
    public void La_barre_de_filtres_compte_les_filtres_poses_et_propose_de_les_effacer()
    {
        int resets = 0;
        IRenderedComponent<ListFilterBar> bar = Render<ListFilterBar>(p => p
            .Add(b => b.Id, "f")
            .Add(b => b.ActiveCount, 2)
            .Add(b => b.OnReset, () => resets++)
            .Add(b => b.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<input id='q' />")))
            .Add(b => b.More, (RenderFragment)(b => b.AddMarkupContent(0, "<select id='s'></select>"))));

        bar.Find("#f-toggle").GetAttribute("aria-expanded").Should().Be("false");
        bar.Find(".lt-filter-count").TextContent.Should().Be("2");
        bar.Find("#f-toggle .lettra-visually-hidden").TextContent.Should().Be("2 filtre(s) posé(s)");
        bar.Find("#f-toggle").Click();
        bar.Find("#f").GetAttribute("data-open").Should().Be("true");
        bar.Find("#f-toggle").GetAttribute("aria-expanded").Should().Be("true");
        bar.Find("#f-reset").Click();
        resets.Should().Be(1);

        bar.Render(p => p.Add(b => b.ActiveCount, 0));
        bar.FindAll("#f-reset").Should().BeEmpty();
        bar.FindAll(".lt-filter-count").Should().BeEmpty();
    }
}

/// <summary>Tri et filtres branchés sur les listes de factures et de devis : les critères partent vers le serveur.</summary>
[Collection("culture")]
public sealed class BillingListSortAndFilterTests : BunitContext
{
    private readonly FakeInvoicesGateway _invoices = new();
    private readonly FakeQuotesGateway _quotes = new();

    public BillingListSortAndFilterTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddCabinetFakes();
        Services.AddSingleton<IInvoicesGateway>(_invoices);
        Services.AddSingleton<IQuotesGateway>(_quotes);
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    [Fact]
    public void Les_factures_s_ouvrent_les_plus_recentes_en_tete_et_se_trient_par_colonne()
    {
        _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Validated, "FAC-2026-0042");
        IRenderedComponent<InvoiceList> page = Render<InvoiceList>();

        _invoices.ListQueries.Last().Should().Match<BillingListQuery>(q => q.Sort == BillingSortDto.IssueDate && q.Direction == SortDirectionDto.Descending);
        page.Find("th:has(#invoices-sort-date)").GetAttribute("aria-sort").Should().Be("descending");

        page.Find("#invoices-sort-incl").Click();
        _invoices.ListQueries.Last().Should().Match<BillingListQuery>(q => q.Sort == BillingSortDto.TotalInclTax && q.Direction == SortDirectionDto.Descending);
        page.Find("#invoices-sort-incl").Click();
        _invoices.ListQueries.Last().Direction.Should().Be(SortDirectionDto.Ascending);
        page.Find("#invoices-sort-customer").Click();
        _invoices.ListQueries.Last().Should().Match<BillingListQuery>(q => q.Sort == BillingSortDto.Customer && q.Direction == SortDirectionDto.Ascending);
        page.FindAll(".lt-skeleton").Should().BeEmpty("un tri garde la liste affichée");
    }

    [Fact]
    public void Les_filtres_de_dates_et_de_montant_partent_au_serveur_et_s_effacent_d_un_clic()
    {
        _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Validated, "FAC-2026-0042");
        IRenderedComponent<InvoiceList> page = Render<InvoiceList>();
        page.FindAll("#invoices-filters-reset").Should().BeEmpty();

        page.Find("#invoices-dates-from").Change("2026-01-01");
        page.Find("#invoices-dates-to").Change("2026-03-31");
        page.Find("#invoices-amount-min").Change("100");
        page.Find("#invoices-amount-max").Change("2500.5");
        page.Find("#invoices-status").Change(nameof(InvoiceStatusDto.Validated));

        BillingListQuery query = _invoices.ListQueries.Last();
        query.From.Should().Be(new DateOnly(2026, 1, 1));
        query.To.Should().Be(new DateOnly(2026, 3, 31));
        query.MinAmount.Should().Be(100m);
        query.MaxAmount.Should().Be(2500.5m);
        page.Find(".lt-filter-count").TextContent.Should().Be("5");

        page.Find("#invoices-filters-reset").Click();
        query = _invoices.ListQueries.Last();
        query.Should().Match<BillingListQuery>(q => q.From == null && q.To == null && q.MinAmount == null && q.MaxAmount == null);
        _invoices.ListCalls.Last().Status.Should().BeNull();
        page.FindAll(".lt-filter-count").Should().BeEmpty();
    }

    [Fact]
    public void Une_liste_filtree_vide_propose_d_effacer_les_filtres()
    {
        IRenderedComponent<InvoiceList> page = Render<InvoiceList>();
        page.Find("#invoices-amount-min").Change("1000000");

        page.Find("#invoices-empty-reset").Click();

        _invoices.ListQueries.Last().MinAmount.Should().BeNull();
        page.FindAll("#invoices-empty-reset").Should().BeEmpty();
    }

    [Fact]
    public void Les_devis_se_trient_par_la_liste_deroulante_et_se_filtrent_par_montant()
    {
        _quotes.Add(QuoteStatusDto.Validated, "DEV-2026-0001");
        IRenderedComponent<QuoteList> page = Render<QuoteList>();

        page.Find("#quotes-sort").Change("Customer:Descending");
        _quotes.ListQueries.Last().Should().Match<BillingListQuery>(q => q.Sort == BillingSortDto.Customer && q.Direction == SortDirectionDto.Descending);
        page.Find("th:has(#quotes-sort-customer)").GetAttribute("aria-sort").Should().Be("descending");

        page.Find("#quotes-sort-valid").Click();
        _quotes.ListQueries.Last().Should().Match<BillingListQuery>(q => q.Sort == BillingSortDto.DueDate && q.Direction == SortDirectionDto.Descending);

        page.Find("#quotes-amount-min").Change("10");
        page.Find("#quotes-dates-from").Change("2026-10-01");
        _quotes.ListQueries.Last().Should().Match<BillingListQuery>(q => q.MinAmount == 10m && q.From == new DateOnly(2026, 10, 1));

        page.Find("#quotes-filters-reset").Click();
        _quotes.ListQueries.Last().Should().Match<BillingListQuery>(q => q.MinAmount == null && q.From == null);
    }
}
