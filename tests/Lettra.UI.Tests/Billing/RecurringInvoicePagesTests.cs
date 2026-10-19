using System.Globalization;
using Bunit;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Billing;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Billing;
using Lettra.UI.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Billing;

/// <summary>
/// Factures récurrentes (vague 7) : liste filtrée et triée, bandeau des factures dues, fiche du modèle (création,
/// pause, révision, historique) ; chaque action finit par un message du toaster.
/// </summary>
[Collection("culture")]
public sealed class RecurringInvoicePagesTests : BunitContext
{
    private readonly FakeRecurringInvoicesGateway _recurring = new();
    private readonly FakeCustomersGateway _customers = new();

    public RecurringInvoicePagesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IRecurringInvoicesGateway>(_recurring);
        Services.AddSingleton<IBillingSettingsGateway>(new FakeBillingSettingsGateway());
        Services.AddSingleton<ICatalogGateway>(new FakeCatalogGateway());
        Services.AddSingleton<ICustomersGateway>(_customers);
        Services.AddSingleton<IPriceListsGateway>(new FakePriceListsGateway());
        _customers.Customers.Add(new CustomerSummary(BillingFixtures.DupontId, CustomerKindDto.Company, "Dupont SARL", "732829320", "Lyon"));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<RecurringInvoiceList> List()
    {
        IRenderedComponent<RecurringInvoiceList> page = Render<RecurringInvoiceList>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    private IRenderedComponent<RecurringInvoiceEditor> Editor(Guid? id = null)
    {
        IRenderedComponent<RecurringInvoiceEditor> page = Render<RecurringInvoiceEditor>(p => p.Add(e => e.Id, id));
        page.WaitForAssertion(() => page.Find("#recurring-save"));
        return page;
    }

    [Fact]
    public void La_liste_montre_les_etats_et_le_bandeau_cree_les_factures_dues_avec_un_message()
    {
        _recurring.Add(FakeRecurringInvoicesGateway.Template("Tenue mensuelle", due: true));
        _recurring.Add(FakeRecurringInvoicesGateway.Template("Forfait suspendu", active: false));
        _recurring.Add(FakeRecurringInvoicesGateway.Template("Assistance Syntec", awaitsIndex: true));
        _recurring.NextRun = new RecurringRunResponse(
            [new RecurringRunItem(Guid.CreateVersion7(), "Tenue mensuelle", Guid.CreateVersion7(), new(2026, 10, 1), new(2026, 10, 31), true, "FAC-000042", null)], []);

        IRenderedComponent<RecurringInvoiceList> page = List();

        page.FindAll("#recurring-table tbody tr").Should().HaveCount(3);
        page.Find("#recurring-due").TextContent.Should().Contain("Une facture récurrente est à créer.");
        page.Markup.Should().Contain("En pause").And.Contain("À facturer").And.Contain("Indice à saisir").And.Contain("Mensuelle");
        page.FindAll("#recurring-table td[data-label]").Should().NotBeEmpty("les lignes deviennent des cartes sur téléphone");

        page.Find("#recurring-run").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("1 facture(s) créée(s), dont 1 validée(s)."));
        _recurring.Runs.Should().Equal([null]);
        page.WaitForAssertion(() => page.FindAll("#recurring-due").Should().BeEmpty());
    }

    [Fact]
    public void Les_filtres_et_le_tri_partent_au_serveur_et_une_liste_filtree_vide_le_dit()
    {
        _recurring.Add(FakeRecurringInvoicesGateway.Template("Tenue mensuelle"));
        IRenderedComponent<RecurringInvoiceList> page = List();

        page.Find("#recurring-state").Change("paused");
        page.Find("#recurring-sort").Change(nameof(RecurringInvoiceSortDto.Amount));
        page.Find("#recurring-descending").Change("desc");
        page.Find("#recurring-search").Change("introuvable");

        page.WaitForAssertion(() => page.Find("#recurring-empty").TextContent.Should().NotContain("Créez un modèle"));
        RecurringInvoiceListQuery last = _recurring.Queries.Where(q => q.PageSize > 1).Last();
        last.Should().Match<RecurringInvoiceListQuery>(q => q.Active == false && q.Sort == RecurringInvoiceSortDto.Amount && q.Descending && q.Search == "introuvable");
    }

    [Fact]
    public void Une_liste_vide_invite_a_creer_un_modele()
    {
        IRenderedComponent<RecurringInvoiceList> page = List();

        page.Find("#recurring-empty").TextContent.Should().Contain("Aucune facture récurrente");
        page.Find("#recurring-empty a").GetAttribute("href").Should().Be("recurring-invoices/new");
    }

    [Fact]
    public void Un_nouveau_modele_s_enregistre_avec_sa_revision_et_ouvre_sa_fiche()
    {
        IRenderedComponent<RecurringInvoiceEditor> page = Editor();

        page.Find("#recurring-name").Change("Tenue trimestrielle");
        page.Find("#frequency").Change(nameof(RecurrenceFrequencyDto.Quarterly));
        page.Find("#auto-validate").Change(true);
        page.Find("#revision").Change(nameof(PriceRevisionKindDto.FixedPercent));
        page.Find("#revision-percent").Change("2.5");
        page.Find("#recurring-save").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Modèle créé."));
        (Guid? id, SaveRecurringInvoiceRequest request, long? version) = _recurring.Saved.Single();
        id.Should().BeNull();
        version.Should().BeNull();
        request.Should().Match<SaveRecurringInvoiceRequest>(r => r.Name == "Tenue trimestrielle" && r.Frequency == RecurrenceFrequencyDto.Quarterly
            && r.ValidateAutomatically && r.Revision == PriceRevisionKindDto.FixedPercent && r.RevisionPercent == 2.5m);
        Navigation.Uri.Should().EndWith($"recurring-invoices/{_recurring.Templates.Single().Id}");
    }

    [Fact]
    public void Une_saisie_refusee_montre_l_erreur_sous_son_champ()
    {
        _recurring.SaveErrors = [new ErrorDetail(ErrorCodes.RecurringInvoice.NameRequired, "Name")];
        IRenderedComponent<RecurringInvoiceEditor> page = Editor();

        page.Find("#recurring-save").Click();

        page.WaitForAssertion(() => page.Markup.Should().Contain("Donnez un nom au modèle."));
        this.Toasts().Should().NotContain(t => t.Kind == ToastKind.Success);
    }

    [Fact]
    public void La_fiche_montre_la_prochaine_facture_l_historique_et_se_met_en_pause()
    {
        Guid invoice = Guid.CreateVersion7();
        RecurringInvoiceResponse template = _recurring.Add(FakeRecurringInvoicesGateway.Template("Tenue mensuelle", due: true, occurrences:
        [
            new RecurringOccurrenceResponse(new(2026, 9, 1), new(2026, 9, 30), invoice, "FAC-000041", InvoiceStatusDto.Validated, 1.03m, DateTimeOffset.UnixEpoch),
        ]));

        IRenderedComponent<RecurringInvoiceEditor> page = Editor(template.Id);

        page.Find("#recurring-next").TextContent.Should().Contain("Prochaine facture le 01/10/2026, pour la période du 01/10/2026 au 31/10/2026.");
        page.Find("#recurring-history").TextContent.Should().Contain("FAC-000041").And.Contain("du 01/09/2026 au 30/09/2026").And.Contain("Prix révisés +3 %");
        page.Find($"#recurring-history a[href='invoices/{invoice}']");
        page.Find("#frequency").HasAttribute("disabled").Should().BeTrue("une période est déjà facturée");

        page.Find("#recurring-toggle").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Modèle mis en pause."));
        page.WaitForAssertion(() => page.Find("#recurring-next").TextContent.Should().Contain("Le modèle est en pause"));
        page.Find("#recurring-toggle").TextContent.Trim().Should().Be("Reprendre");
    }

    [Fact]
    public void La_fiche_cree_la_facture_due_et_un_refus_de_validation_le_signale()
    {
        RecurringInvoiceResponse template = _recurring.Add(FakeRecurringInvoicesGateway.Template("Tenue mensuelle", due: true));
        _recurring.NextRun = new RecurringRunResponse(
            [new RecurringRunItem(template.Id, template.Name, Guid.CreateVersion7(), new(2026, 10, 1), new(2026, 10, 31), false, null, ErrorCodes.Security.Forbidden)], []);
        IRenderedComponent<RecurringInvoiceEditor> page = Editor(template.Id);

        page.Find("#recurring-run-one").Click();

        page.WaitForAssertion(() => this.Toasts().Select(t => t.Message).Should().Contain("1 facture(s) laissée(s) en brouillon : la validation a été refusée."));
        _recurring.Runs.Should().Equal(template.Id);
    }

    [Fact]
    public void La_revision_Syntec_qui_attend_son_indice_est_signalee_et_le_modele_se_supprime()
    {
        RecurringInvoiceResponse template = _recurring.Add(FakeRecurringInvoicesGateway.Template("Assistance", awaitsIndex: true));
        IRenderedComponent<RecurringInvoiceEditor> page = Editor(template.Id);

        page.Find("#revision-awaits").TextContent.Should().Contain("nouvel indice Syntec");
        page.Find("#syntec-reference").GetAttribute("value").Should().Be("300");
        page.Find("#next-revision").TextContent.Should().Contain("01/10/2027");

        page.Find("#recurring-delete").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Modèle supprimé."));
        _recurring.Templates.Should().BeEmpty();
        Navigation.Uri.Should().EndWith("recurring-invoices");
    }

    [Fact]
    public void Une_passe_sans_facture_due_ou_en_echec_le_dit()
    {
        ToastService toasts = Services.GetRequiredService<ToastService>();
        Microsoft.Extensions.Localization.IStringLocalizer<SharedResource> l = Services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<SharedResource>>();

        RecurringToasts.Show(toasts, l, new RecurringRunResponse([], []));
        this.ToastText().Should().Be("Aucune facture n'est à créer aujourd'hui.");

        RecurringToasts.Show(toasts, l, new RecurringRunResponse([], [new RecurringRunFailure(Guid.CreateVersion7(), "Forfait", ErrorCodes.Billing.CustomerRequired)]));
        this.Toasts().Should().Contain(t => t.Kind == ToastKind.Error && t.Message.StartsWith("« Forfait » : ", StringComparison.Ordinal));
    }
}
