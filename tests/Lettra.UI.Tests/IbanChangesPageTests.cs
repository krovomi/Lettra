using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Explorer;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Parties;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests;

/// <summary>Passerelle des changements d'IBAN en mémoire : filtre d'état comme le serveur, décisions enregistrées.</summary>
internal sealed class FakeIbanChangesGateway : IIbanChangesGateway
{
    public List<IbanChangeResponse> Changes { get; } = [];

    public List<IbanChangeListQuery> Queries { get; } = [];

    public List<(Guid Id, ConfirmIbanChangeRequest Request)> Confirmations { get; } = [];

    public List<(Guid Id, RejectIbanChangeRequest Request)> Rejections { get; } = [];

    public string? NextError { get; set; }

    public IbanChangeResponse Add(string supplier, bool countryChanged = false)
    {
        IbanChangeResponse change = new(Guid.CreateVersion7(), Guid.CreateVersion7(), supplier, "FR76 •••• 0189",
            countryChanged ? "DE89 •••• 3000" : "FR14 •••• 2606", countryChanged, new DateTimeOffset(2026, 10, 20, 9, 30, 0, TimeSpan.Zero),
            IbanChangeStatusDto.Pending, null, null, null)
        {
            Version = 3,
        };
        Changes.Add(change);
        return change;
    }

    public Task<OperationResult<PagedResponse<IbanChangeResponse>>> ListAsync(IbanChangeListQuery query, CancellationToken cancellationToken)
    {
        Queries.Add(query);
        List<IbanChangeResponse> items = [.. Changes.Where(c => query.Status is not { } status || c.Status == status)];
        return Task.FromResult(OperationResult<PagedResponse<IbanChangeResponse>>.Success(new(items, query.Page, query.PageSize, items.Count)));
    }

    public Task<OperationResult<IReadOnlyList<IbanChangeResponse>>> ForSupplierAsync(Guid supplierId, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<IReadOnlyList<IbanChangeResponse>>.Success([.. Changes.Where(c => c.SupplierId == supplierId)]));

    public Task<OperationResult<bool>> ConfirmAsync(Guid changeId, ConfirmIbanChangeRequest request, CancellationToken cancellationToken)
    {
        Confirmations.Add((changeId, request));
        return Decide(changeId, IbanChangeStatusDto.Confirmed);
    }

    public Task<OperationResult<bool>> RejectAsync(Guid changeId, RejectIbanChangeRequest request, CancellationToken cancellationToken)
    {
        Rejections.Add((changeId, request));
        return Decide(changeId, IbanChangeStatusDto.Rejected);
    }

    private Task<OperationResult<bool>> Decide(Guid changeId, IbanChangeStatusDto status)
    {
        if (NextError is { } code)
        {
            NextError = null;
            return Task.FromResult(OperationResult<bool>.Failure(code));
        }

        int index = Changes.FindIndex(c => c.Id == changeId);
        Changes[index] = Changes[index] with { Status = status };
        return Task.FromResult(OperationResult<bool>.Success(true));
    }
}

[Collection("culture")]
public sealed class IbanChangesPageTests : BunitContext
{
    private readonly FakeIbanChangesGateway _gateway = new();

    public IbanChangesPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IIbanChangesGateway>(_gateway);
        UseCulture("fr-FR");
    }

    private static void UseCulture(string name) => LettraCultures.Apply(CultureInfo.GetCultureInfo(name));

    private IRenderedComponent<IbanChangesPage> Open()
    {
        IRenderedComponent<IbanChangesPage> page = Render<IbanChangesPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    [Fact]
    public void Sans_changement_a_verifier_l_ecran_le_dit_en_francais_et_en_anglais()
    {
        IRenderedComponent<IbanChangesPage> page = Open();
        page.Find("h1").TextContent.Should().Be("Changements d'IBAN");
        page.Find("#iban-changes-empty").TextContent.Should().Contain("Aucun IBAN à vérifier");
        _gateway.Queries.Should().ContainSingle().Which.Status.Should().Be(IbanChangeStatusDto.Pending, "par défaut, seuls les changements à vérifier");

        UseCulture("en-US");
        page = Open();
        page.Find("h1").TextContent.Should().Be("IBAN changes");
    }

    [Fact]
    public void Chaque_changement_montre_les_iban_masques_et_le_changement_de_pays()
    {
        _gateway.Add("Minoterie Dupuis");
        IbanChangeResponse foreign = _gateway.Add("Papeterie Durand", countryChanged: true);

        IRenderedComponent<IbanChangesPage> page = Open();

        page.FindAll("#iban-changes-table tbody tr").Should().HaveCount(2);
        string row = page.Find($"tr[data-change='{foreign.Id}']").TextContent;
        row.Should().Contain("Papeterie Durand").And.Contain("FR76 •••• 0189").And.Contain("DE89 •••• 3000").And.Contain("Le compte change de pays");
    }

    [Fact]
    public void Confirmer_envoie_le_canal_et_la_version_puis_retire_la_ligne()
    {
        IbanChangeResponse change = _gateway.Add("Minoterie Dupuis");
        IRenderedComponent<IbanChangesPage> page = Open();

        page.Find($"#iban-confirm-{change.Id}").Click();
        page.Find("#iban-decision-title").TextContent.Should().Contain("Minoterie Dupuis").And.Contain("FR14 •••• 2606");
        page.Find("#iban-decision-method").Change(nameof(IbanVerificationMethodDto.KnownPhoneNumber));
        page.Find("#iban-decision-note").Change("Rappel au numéro de la fiche");
        page.Find("#iban-decision-submit").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Nouvel IBAN de Minoterie Dupuis confirmé."));
        _gateway.Confirmations.Should().ContainSingle().Which.Should().BeEquivalentTo(
            (change.Id, new ConfirmIbanChangeRequest { Method = IbanVerificationMethodDto.KnownPhoneNumber, Note = "Rappel au numéro de la fiche", ExpectedVersion = 3 }));
        page.FindAll("#iban-decision").Should().BeEmpty();
        page.FindAll("#iban-changes-table").Should().BeEmpty("le changement n'est plus à vérifier");
    }

    [Fact]
    public void Refuser_affiche_le_refus_du_serveur_dans_un_toast_et_garde_le_panneau()
    {
        IbanChangeResponse change = _gateway.Add("Minoterie Dupuis");
        _gateway.NextError = ErrorCodes.IbanChange.SameUser;
        IRenderedComponent<IbanChangesPage> page = Open();

        page.Find($"#iban-reject-{change.Id}").Click();
        page.Find("#iban-decision-note").Change("Demande reçue d'une adresse inconnue");
        page.Find("#iban-decision-submit").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Une autre personne du cabinet doit vérifier l'IBAN que vous avez saisi."));
        _gateway.Rejections.Should().ContainSingle().Which.Request.Note.Should().Be("Demande reçue d'une adresse inconnue");
        page.Find("#iban-decision").Should().NotBeNull();

        page.Find("#iban-decision-cancel").Click();
        page.FindAll("#iban-decision").Should().BeEmpty();
    }

    [Fact]
    public void Les_filtres_et_le_tri_relancent_la_liste_et_se_reinitialisent()
    {
        _gateway.Add("Minoterie Dupuis");
        IRenderedComponent<IbanChangesPage> page = Open();

        page.Find("#iban-changes-status").Change(string.Empty);
        page.Find("#iban-changes-search").Change("dupuis");
        page.Find("#iban-changes-sort-supplier").Click();

        _gateway.Queries[^1].Should().Be(new IbanChangeListQuery(null, "dupuis", 1, 50, IbanChangeSortDto.Supplier, SortDirectionDto.Ascending));

        page.Find("#iban-changes-status").Change(nameof(IbanChangeStatusDto.Confirmed));
        page.WaitForAssertion(() => page.FindAll("#iban-changes-filtered-empty").Should().ContainSingle());
        page.Find("#iban-changes-empty-reset").Click();

        _gateway.Queries[^1].Status.Should().Be(IbanChangeStatusDto.Pending);
        _gateway.Queries[^1].Search.Should().BeNull();
    }
}

/// <summary>Le bandeau de la fiche fournisseur : un IBAN modifié à vérifier se voit avant tout paiement.</summary>
[Collection("culture")]
public sealed class SupplierIbanBannerTests : BunitContext
{
    private readonly FakeSuppliersGateway _suppliers = new();

    public SupplierIbanBannerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new ExplorerSettings { SearchDelay = TimeSpan.FromMilliseconds(1) });
        Services.AddLettraUi();
        Services.AddSingleton<ISuppliersGateway>(_suppliers);
        Services.AddSingleton<IDirectoryGateway>(new FakeDirectoryGateway());
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<SuppliersPage> OpenSheet(Guid id)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"suppliers/{id}");
        IRenderedComponent<SuppliersPage> page = Render<SuppliersPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    private SupplierResponse WithPendingChange(bool countryChanged)
    {
        SupplierResponse supplier = _suppliers.Add("Papeterie Durand", "Lyon", "FR1420041010050500013M02606");
        IbanChangeResponse change = new(Guid.CreateVersion7(), supplier.Id, supplier.Name, "FR76 •••• 0189", "FR14 •••• 2606", countryChanged,
            new DateTimeOffset(2026, 10, 20, 9, 30, 0, TimeSpan.Zero), IbanChangeStatusDto.Pending, null, null, null);
        _suppliers.Records[supplier.Id] = supplier with { PendingIbanChange = change };
        return supplier;
    }

    [Fact]
    public void Un_iban_a_verifier_s_annonce_sur_la_fiche_avec_un_lien_vers_la_verification()
    {
        IRenderedComponent<SuppliersPage> page = OpenSheet(WithPendingChange(countryChanged: false).Id);

        AngleSharp.Dom.IElement banner = page.Find("#supplier-iban-pending");
        banner.ClassList.Should().Contain("is-warning");
        banner.TextContent.Should().Contain("IBAN modifié le 20/10/2026 (FR14 •••• 2606) : à vérifier par un autre canal avant tout paiement.");
        page.Find("#supplier-iban-verify").GetAttribute("href").Should().Be("iban-changes");
    }

    [Fact]
    public void Un_compte_qui_change_de_pays_passe_le_bandeau_en_danger()
    {
        IRenderedComponent<SuppliersPage> page = OpenSheet(WithPendingChange(countryChanged: true).Id);

        page.Find("#supplier-iban-pending").ClassList.Should().Contain("is-danger");
        page.Find("#supplier-iban-pending").TextContent.Should().Contain("Le compte change de pays");
    }

    [Fact]
    public void Sans_changement_en_attente_la_fiche_n_a_pas_de_bandeau()
    {
        SupplierResponse supplier = _suppliers.Add("Papeterie Durand", "Lyon", "FR7630006000011234567890189");

        OpenSheet(supplier.Id).FindAll("#supplier-iban-pending").Should().BeEmpty();
    }
}
