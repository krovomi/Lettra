using System.Globalization;
using Bunit;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Cabinet;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Dossiers;
using Lettra.UI.Tests.Administration;
using Lettra.UI.Tests.Cabinet;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Dossiers;

/// <summary>
/// L'écran « Dossiers du cabinet » : compteurs, liste triée et filtrée à l'écran, création d'un dossier, effacement
/// confirmé dans la ligne puis annulé depuis le toaster, en français et en anglais ; et l'écran refusé à qui
/// n'administre pas le cabinet.
/// </summary>
[Collection("culture")]
public sealed class CabinetDossiersPageTests : BunitContext
{
    private readonly FakeCabinetGateway _cabinet = new();
    private readonly FakeCabinetDossiersGateway _gateway;
    private readonly FakeAdministrationGateway _administration = new();
    private readonly SwitchableDossierSelection _selection = new(FakeCabinetGateway.Boulangerie);

    public CabinetDossiersPageTests()
    {
        _gateway = new FakeCabinetDossiersGateway(_cabinet);
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<ICabinetGateway>(_cabinet);
        Services.AddSingleton<IDossierSelection>(_selection);
        Services.AddSingleton<IAdministrationGateway>(_administration);
        Services.AddSingleton<ICabinetDossiersGateway>(_gateway);
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<CabinetDossiersPage> RenderPage(int rows = 4)
    {
        IRenderedComponent<CabinetDossiersPage> page = Render<CabinetDossiersPage>();
        page.WaitForAssertion(() => page.FindAll("#dossiers-list tbody tr").Should().HaveCount(rows));
        return page;
    }

    private static string Row(Guid id) => $"#dossiers-row-{id:N}";

    private static IReadOnlyList<string> Names(IRenderedComponent<CabinetDossiersPage> page) =>
        [.. page.FindAll("#dossiers-list tbody strong").Select(s => s.TextContent)];

    [Fact]
    public void Les_compteurs_et_la_liste_disent_l_etat_de_chaque_dossier()
    {
        IRenderedComponent<CabinetDossiersPage> page = RenderPage();

        page.Find("#dossiers-active").TextContent.Should().Be("3");
        page.Find("#dossiers-erasing").TextContent.Should().Be("1");
        page.Find(Row(FakeCabinetGateway.Boulangerie)).TextContent.Should().Contain("Boulangerie Martin").And.Contain("dossier ouvert").And.Contain("Ouvert");
        page.Find(Row(FakeCabinetDossiersGateway.Ancien)).TextContent.Should().Contain("Effacement demandé").And.Contain("Annuler l'effacement");

        // Une base déclarée dans la configuration garde son bouton, qui explique pourquoi elle ne s'efface pas.
        page.Find($"{Row(FakeCabinetGateway.Atelier)} .lt-row-delete").Click();
        page.Markup.Should().Contain("il ne s'efface pas depuis Lettra");
        page.FindAll($"{Row(FakeCabinetGateway.Atelier)} .lt-row-confirm-yes").Should().BeEmpty();
    }

    [Fact]
    public void Les_filtres_et_le_tri_s_appliquent_a_l_ecran()
    {
        IRenderedComponent<CabinetDossiersPage> page = RenderPage();

        Names(page).Should().Equal("Ancien client", "Atelier Éole", "Boulangerie Martin", "Garage du Centre");
        page.Find("#dossiers-sort").Change("name:Descending");
        Names(page).Should().Equal("Garage du Centre", "Boulangerie Martin", "Atelier Éole", "Ancien client");

        page.Find("#dossiers-state").Change(nameof(CabinetDossierStateDto.ErasureScheduled));
        Names(page).Should().Equal("Ancien client");

        page.Find("#dossiers-search").Input("zzz");
        page.Find("#dossiers-empty").TextContent.Should().NotBeEmpty();
        page.Find("#dossiers-empty-reset").Click();
        page.FindAll("#dossiers-list tbody tr").Should().HaveCount(4);
    }

    [Fact]
    public async Task Creer_un_dossier_le_montre_dans_la_liste_et_dans_le_selecteur_puis_l_ouvre_depuis_le_toaster()
    {
        IRenderedComponent<CabinetDossiersPage> page = RenderPage();
        CurrentDossier current = Services.GetRequiredService<CurrentDossier>();
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("dossiers");

        page.Find("#dossiers-new").Click();
        page.Find("#dossiers-name").Change("Fromagerie des Halles");
        page.Find("#dossiers-form").Submit();

        page.WaitForAssertion(() => page.FindAll("#dossiers-list tbody tr").Should().HaveCount(5));
        this.ToastText().Should().Be("Dossier « Fromagerie des Halles » créé.");
        page.FindAll("#dossiers-form").Should().BeEmpty();
        current.Dossiers.Should().Contain(d => d.Name == "Fromagerie des Halles");

        await page.InvokeAsync(() => this.Toasts()[^1].Action!.Run());

        current.Id.Should().Be(_gateway.Created!.Id);
        navigation.Uri.Should().Be(navigation.BaseUri);
    }

    [Fact]
    public void Un_refus_du_serveur_reste_sous_le_formulaire()
    {
        IRenderedComponent<CabinetDossiersPage> page = RenderPage();
        _gateway.CreateErrors = [new ErrorDetail(ErrorCodes.Tenancy.NoCapacity)];

        page.Find("#dossiers-new").Click();
        page.Find("#dossiers-name").Change("Fromagerie des Halles");
        page.Find("#dossiers-form").Submit();

        page.WaitForAssertion(() => this.ToastText().Should().StartWith("Aucun serveur ne peut accueillir de nouveau dossier"));
        page.FindAll("#dossiers-form").Should().ContainSingle();
        page.Find("#dossiers-cancel").Click();
        page.FindAll("#dossiers-form").Should().BeEmpty();
    }

    [Fact]
    public async Task Effacer_le_dossier_ouvert_en_ouvre_un_autre_et_s_annule_depuis_le_toaster()
    {
        IRenderedComponent<CabinetDossiersPage> page = RenderPage();
        CurrentDossier current = Services.GetRequiredService<CurrentDossier>();

        page.Find($"{Row(FakeCabinetGateway.Boulangerie)} .lt-row-delete").Click();
        page.Markup.Should().Contain("Il sera effacé dans 30 jours, sauf annulation.");
        page.Find(".lt-row-confirm-yes").Click();

        page.WaitForAssertion(() => page.Find(Row(FakeCabinetGateway.Boulangerie)).TextContent.Should().Contain("Effacement demandé"));
        this.ToastText().Should().StartWith("Dossier « Boulangerie Martin » fermé. Son effacement aura lieu le ");
        current.Id.Should().NotBe(FakeCabinetGateway.Boulangerie, "le dossier fermé n'est plus le dossier ouvert");
        current.Dossiers.Should().NotContain(d => d.Id == FakeCabinetGateway.Boulangerie);

        await page.InvokeAsync(() => this.Toasts()[^1].Action!.Run());

        page.WaitForAssertion(() => page.Find(Row(FakeCabinetGateway.Boulangerie)).TextContent.Should().Contain("Ouvert"));
        this.ToastText().Should().Be("Effacement annulé : le dossier « Boulangerie Martin » rouvre tel quel.");
        current.Dossiers.Should().Contain(d => d.Id == FakeCabinetGateway.Boulangerie);
    }

    [Fact]
    public void Un_refus_d_annulation_est_traduit_et_un_effacement_echu_n_est_plus_annulable()
    {
        _gateway.Add(new CabinetDossierResponse(FakeCabinetDossiersGateway.Echu, "Dossier échu", CabinetDossierStateDto.ErasureScheduled, false, DateTimeOffset.UtcNow.AddMinutes(-5)));
        IRenderedComponent<CabinetDossiersPage> page = RenderPage(rows: 5);
        _gateway.CancelErrors = [new ErrorDetail(ErrorCodes.Tenancy.ErasureNotCancellable)];

        page.Find(Row(FakeCabinetDossiersGateway.Echu)).TextContent.Should().Contain("effacement en cours");
        page.FindAll($"#dossiers-cancel-{FakeCabinetDossiersGateway.Echu:N}").Should().BeEmpty();

        page.Find($"#dossiers-cancel-{FakeCabinetDossiersGateway.Ancien:N}").Click();
        page.WaitForAssertion(() => this.ToastText().Should().StartWith("L'effacement de ce dossier ne peut plus être annulé"));
    }

    [Fact]
    public void Un_refus_d_effacement_est_traduit()
    {
        IRenderedComponent<CabinetDossiersPage> page = RenderPage();
        _gateway.ScheduleErrors = [new ErrorDetail(ErrorCodes.Tenancy.DossierDeclared)];

        page.Find($"{Row(FakeCabinetGateway.Garage)} .lt-row-delete").Click();
        page.Find(".lt-row-confirm-yes").Click();

        page.WaitForAssertion(() => this.ToastText().Should().StartWith("Ce dossier est déclaré dans la configuration du serveur"));
    }

    [Fact]
    public void L_ecran_est_traduit_en_anglais()
    {
        LettraCultures.Apply(CultureInfo.GetCultureInfo("en-GB"));

        IRenderedComponent<CabinetDossiersPage> page = RenderPage();

        page.Find("h1").TextContent.Should().Be("Firm dossiers");
        page.Markup.Should().Contain("Erasure requested").And.Contain("Open dossiers").And.Contain("Cancel erasure");
    }

    [Fact]
    public void Sans_le_droit_d_administrer_le_cabinet_l_ecran_est_refuse()
    {
        _administration.CanAdminister = false;

        IRenderedComponent<CabinetDossiersPage> page = Render<CabinetDossiersPage>();

        page.WaitForAssertion(() => page.Find("#dossiers-denied").TextContent.Should().Contain("Seul l'administrateur du cabinet gère ses dossiers"));
        page.FindAll("#dossiers-new").Should().BeEmpty();
        _gateway.Lists.Should().Be(0);
    }

    [Fact]
    public void L_entree_du_menu_Pilotage_ne_s_affiche_qu_a_l_administrateur_du_cabinet() =>
        Lettra.UI.Shared.Layout.NavItems.All.Should().ContainSingle(i => i.Href == "dossiers")
            .Which.Should().Match<Lettra.UI.Shared.Layout.NavItem>(i => i.Page == AccessAreaDto.Administration && i.GroupKey == "Nav.Group.Settings");
}

/// <summary>
/// Les dossiers du cabinet en mémoire, tenus en phase avec le sélecteur (<see cref="FakeCabinetGateway"/>) : trois
/// ouverts (l'atelier est une base déclarée), un dont l'effacement est demandé.
/// </summary>
internal sealed class FakeCabinetDossiersGateway : ICabinetDossiersGateway
{
    public static readonly Guid Ancien = new("0199e3a0-0000-7000-8000-0000000b0009");
    public static readonly Guid Echu = new("0199e3a0-0000-7000-8000-0000000b0010");

    private readonly FakeCabinetGateway _cabinet;
    private readonly List<CabinetDossierResponse> _dossiers;

    public FakeCabinetDossiersGateway(FakeCabinetGateway cabinet)
    {
        _cabinet = cabinet;
        _dossiers =
        [
            new(FakeCabinetGateway.Boulangerie, "Boulangerie Martin", CabinetDossierStateDto.Active, true, null),
            new(FakeCabinetGateway.Garage, "Garage du Centre", CabinetDossierStateDto.Active, true, null),
            new(FakeCabinetGateway.Atelier, "Atelier Éole", CabinetDossierStateDto.Active, false, null),
            new(Ancien, "Ancien client", CabinetDossierStateDto.ErasureScheduled, false, DateTimeOffset.UtcNow.AddDays(12)),
        ];
    }

    public IReadOnlyList<ErrorDetail> CreateErrors { get; set; } = [];

    public IReadOnlyList<ErrorDetail> ScheduleErrors { get; set; } = [];

    public IReadOnlyList<ErrorDetail> CancelErrors { get; set; } = [];

    public DossierSummary? Created { get; private set; }

    public int Lists { get; private set; }

    public void Add(CabinetDossierResponse dossier) => _dossiers.Add(dossier);

    public Task<OperationResult<IReadOnlyList<CabinetDossierResponse>>> ListAsync(Guid cabinetId, CancellationToken cancellationToken)
    {
        Lists++;
        return Task.FromResult(OperationResult<IReadOnlyList<CabinetDossierResponse>>.Success([.. _dossiers]));
    }

    public Task<OperationResult<DossierSummary>> CreateAsync(Guid cabinetId, CreateDossierRequest request, CancellationToken cancellationToken)
    {
        if (CreateErrors.Count > 0)
        {
            return Task.FromResult(OperationResult<DossierSummary>.Failure([.. CreateErrors]));
        }

        Created = new DossierSummary(Guid.CreateVersion7(), request.Name);
        _dossiers.Add(new CabinetDossierResponse(Created.Id, Created.Name, CabinetDossierStateDto.Active, true, null));
        _cabinet.Dossiers.Add(Created);
        return Task.FromResult(OperationResult<DossierSummary>.Success(Created));
    }

    public Task<OperationResult<DossierErasureResponse>> ScheduleErasureAsync(Guid cabinetId, Guid dossierId, CancellationToken cancellationToken)
    {
        if (ScheduleErrors.Count > 0)
        {
            return Task.FromResult(OperationResult<DossierErasureResponse>.Failure([.. ScheduleErrors]));
        }

        DateTimeOffset due = DateTimeOffset.UtcNow.AddDays(30);
        CabinetDossierResponse dossier = Move(dossierId, CabinetDossierStateDto.ErasureScheduled, due);
        _cabinet.Dossiers.RemoveAll(d => d.Id == dossierId);
        return Task.FromResult(OperationResult<DossierErasureResponse>.Success(new DossierErasureResponse(dossierId, dossier.Name, due)));
    }

    public Task<OperationResult<DossierSummary>> CancelErasureAsync(Guid cabinetId, Guid dossierId, CancellationToken cancellationToken)
    {
        if (CancelErrors.Count > 0)
        {
            return Task.FromResult(OperationResult<DossierSummary>.Failure([.. CancelErrors]));
        }

        CabinetDossierResponse dossier = Move(dossierId, CabinetDossierStateDto.Active, null);
        DossierSummary summary = new(dossierId, dossier.Name);
        _cabinet.Dossiers.Add(summary);
        return Task.FromResult(OperationResult<DossierSummary>.Success(summary));
    }

    private CabinetDossierResponse Move(Guid id, CabinetDossierStateDto state, DateTimeOffset? due)
    {
        int index = _dossiers.FindIndex(d => d.Id == id);
        _dossiers[index] = _dossiers[index] with { State = state, Erasable = state == CabinetDossierStateDto.Active, ErasureDueAt = due };
        return _dossiers[index];
    }
}
