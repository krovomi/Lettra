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
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Administration;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Cabinet;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Dossiers;

/// <summary>
/// L'écran « Dossiers du cabinet » : compteurs, liste triée et filtrée à l'écran, création d'un dossier, effacement
/// confirmé dans la ligne puis annulé depuis le toaster, export d'un dossier dans un paquet chiffré et import d'un
/// paquet, en français et en anglais ; et l'écran refusé à qui n'administre pas le cabinet.
/// </summary>
[Collection("culture")]
public sealed class CabinetDossiersPageTests : BunitContext
{
    private readonly FakeCabinetGateway _cabinet = new();
    private readonly FakeCabinetDossiersGateway _gateway;
    private readonly FakeAdministrationGateway _administration = new();
    private readonly SwitchableDossierSelection _selection = new(FakeCabinetGateway.Boulangerie);
    private readonly FakeFileDownloader _files = new();

    public CabinetDossiersPageTests()
    {
        _gateway = new FakeCabinetDossiersGateway(_cabinet);
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<ICabinetGateway>(_cabinet);
        Services.AddSingleton<IDossierSelection>(_selection);
        Services.AddSingleton<IAdministrationGateway>(_administration);
        Services.AddSingleton<ICabinetDossiersGateway>(_gateway);
        Services.AddSingleton<IFileDownloader>(_files);
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<CabinetDossiersPage> RenderPage(int rows = 4)
    {
        IRenderedComponent<CabinetDossiersPage> page = Render<CabinetDossiersPage>();
        page.WaitForAssertion(() => page.FindAll("#dossiers-list tbody tr").Should().HaveCount(rows));
        return page;
    }

    private const string Passphrase = "cheval agrafe batterie correcte";

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
    public void Exporter_demande_la_phrase_secrete_deux_fois_puis_remet_le_paquet()
    {
        IRenderedComponent<CabinetDossiersPage> page = RenderPage();

        page.Find($"#dossiers-export-{FakeCabinetGateway.Garage:N}").Click();
        page.Find("#dossiers-export-title").TextContent.Should().Be("Exporter « Garage du Centre »");

        // Trop courte : refusée avant tout appel.
        page.Find("#dossiers-export-passphrase").Change("trop court");
        page.Find("#dossiers-export-confirmation").Input("trop court");
        page.Find("#dossiers-export-form").Submit();
        page.Markup.Should().Contain("au moins 12 caractères");
        _gateway.Exports.Should().BeEmpty();

        // Retapée autrement : refusée, une faute de frappe rendrait le paquet illisible.
        page.Find("#dossiers-export-passphrase").Change(Passphrase);
        page.Find("#dossiers-export-confirmation").Input(Passphrase + "!");
        page.Find("#dossiers-export-form").Submit();
        page.Markup.Should().Contain("Les deux phrases ne sont pas identiques.");
        _gateway.Exports.Should().BeEmpty();

        page.Find("#dossiers-export-confirmation").Input(Passphrase);
        page.Find("#dossiers-export-form").Submit();

        page.WaitForAssertion(() => _files.Files.Should().ContainSingle());
        _files.Files[0].FileName.Should().Be("Garage-du-Centre-20261107.lettrapaquet");
        _files.Files[0].Content.Should().Equal(FakeCabinetDossiersGateway.PackageBytes);
        _gateway.Exports.Should().Equal((FakeCabinetGateway.Garage, Passphrase));
        this.ToastText().Should().Be("Dossier « Garage du Centre » exporté dans le fichier Garage-du-Centre-20261107.lettrapaquet. Gardez la phrase secrète à part du fichier.");
        page.FindAll("#dossiers-export-form").Should().BeEmpty();

        // Rouvert, l'encart ne garde pas la phrase.
        page.Find($"#dossiers-export-{FakeCabinetGateway.Garage:N}").Click();
        page.Find("#dossiers-export-passphrase").GetAttribute("value").Should().BeNullOrEmpty();
    }

    [Fact]
    public void Un_dossier_ferme_ne_s_exporte_pas_depuis_sa_ligne() =>
        RenderPage().FindAll($"#dossiers-export-{FakeCabinetDossiersGateway.Ancien:N}").Should().BeEmpty();

    [Fact]
    public async Task Importer_un_paquet_cree_le_dossier_et_l_ouvre_depuis_le_toaster()
    {
        IRenderedComponent<CabinetDossiersPage> page = RenderPage();
        CurrentDossier current = Services.GetRequiredService<CurrentDossier>();

        page.Find("#dossiers-import").Click();
        page.Find("#dossiers-import-form").Submit();
        page.Find("#dossiers-import-problem").TextContent.Should().Be("Choisissez le fichier du paquet.");

        page.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromBinary(FakeCabinetDossiersGateway.PackageBytes, "dossier.lettrapaquet"));
        page.Find("#dossiers-import-passphrase").Input(Passphrase);
        page.Find("#dossiers-import-form").Submit();

        page.WaitForAssertion(() => page.FindAll("#dossiers-list tbody tr").Should().HaveCount(5));
        _gateway.Imports.Should().ContainSingle();
        _gateway.Imports[0].Package.Should().Equal(FakeCabinetDossiersGateway.PackageBytes, "le paquet arrive tel quel");
        _gateway.Imports[0].Passphrase.Should().Be(Passphrase);
        this.ToastText().Should().Be("Dossier « Dossier rapatrié » importé.");
        page.FindAll("#dossiers-import-form").Should().BeEmpty();
        current.Dossiers.Should().Contain(d => d.Name == "Dossier rapatrié");

        await page.InvokeAsync(() => this.Toasts()[^1].Action!.Run());
        current.Id.Should().Be(FakeCabinetDossiersGateway.Imported);
    }

    [Fact]
    public void Un_paquet_illisible_ou_deja_present_est_dit_sous_le_formulaire()
    {
        IRenderedComponent<CabinetDossiersPage> page = RenderPage();
        _gateway.ImportErrors = [new ErrorDetail(ErrorCodes.Tenancy.PackageUnreadable, "Package")];

        page.Find("#dossiers-import").Click();
        page.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "dossier.lettrapaquet"));
        page.Find("#dossiers-import-passphrase").Input("une autre phrase secrète");
        page.Find("#dossiers-import-form").Submit();

        page.WaitForAssertion(() => page.Find("#dossiers-import-form").TextContent.Should().Contain("la phrase secrète est incorrecte"));

        _gateway.ImportErrors = [new ErrorDetail(ErrorCodes.Tenancy.DossierAlreadyExists)];
        page.Find("#dossiers-import-form").Submit();
        page.WaitForAssertion(() => this.ToastText().Should().StartWith("Ce dossier existe déjà"));
        page.FindAll("#dossiers-list tbody tr").Should().HaveCount(4);
    }

    [Fact]
    public void L_ecran_est_traduit_en_anglais()
    {
        LettraCultures.Apply(CultureInfo.GetCultureInfo("en-GB"));

        IRenderedComponent<CabinetDossiersPage> page = RenderPage();

        page.Find("h1").TextContent.Should().Be("Firm dossiers");
        page.Markup.Should().Contain("Erasure requested").And.Contain("Open dossiers").And.Contain("Cancel erasure").And.Contain("Import a dossier");
        page.Find($"#dossiers-export-{FakeCabinetGateway.Garage:N}").Click();
        page.Find("#dossiers-export-title").TextContent.Should().Be("Export “Garage du Centre”");
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
    public static readonly Guid Imported = new("0199e3a0-0000-7000-8000-0000000b0011");

    /// <summary>Le contenu du paquet remis par l'export, et celui qu'on importe : opaque pour l'écran.</summary>
    public static readonly byte[] PackageBytes = [0x4c, 0x45, 0x54, 0x54, 0x52, 0x41, 1, 2, 3];

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

    public IReadOnlyList<ErrorDetail> ImportErrors { get; set; } = [];

    public List<(Guid Dossier, string Passphrase)> Exports { get; } = [];

    public List<(byte[] Package, string Passphrase)> Imports { get; } = [];

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

    public Task<OperationResult<DossierPackageFile>> ExportAsync(Guid cabinetId, Guid dossierId, ExportDossierRequest request, CancellationToken cancellationToken)
    {
        Exports.Add((dossierId, request.Passphrase));
        string name = _dossiers.Single(d => d.Id == dossierId).Name.Replace(' ', '-');
        return Task.FromResult(OperationResult<DossierPackageFile>.Success(new DossierPackageFile($"{name}-20261107.lettrapaquet", PackageBytes)));
    }

    public async Task<OperationResult<DossierSummary>> ImportAsync(Guid cabinetId, Stream package, string passphrase, CancellationToken cancellationToken)
    {
        using MemoryStream content = new();
        await package.CopyToAsync(content, cancellationToken);
        if (ImportErrors.Count > 0)
        {
            return OperationResult<DossierSummary>.Failure([.. ImportErrors]);
        }

        Imports.Add((content.ToArray(), passphrase));
        DossierSummary imported = new(Imported, "Dossier rapatrié");
        _dossiers.Add(new CabinetDossierResponse(imported.Id, imported.Name, CabinetDossierStateDto.Active, true, null));
        _cabinet.Dossiers.Add(imported);
        return OperationResult<DossierSummary>.Success(imported);
    }

    private CabinetDossierResponse Move(Guid id, CabinetDossierStateDto state, DateTimeOffset? due)
    {
        int index = _dossiers.FindIndex(d => d.Id == id);
        _dossiers[index] = _dossiers[index] with { State = state, Erasable = state == CabinetDossierStateDto.Active, ErasureDueAt = due };
        return _dossiers[index];
    }
}
