using System.Globalization;
using Bunit;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.Contracts.Platform;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages;
using Lettra.UI.Shared.Pages.Platform;
using Lettra.UI.Tests.Administration;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Cabinet;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Platform;

/// <summary>
/// L'écran « Plateforme » : compteurs, liste triée et filtrée à l'écran, arrivée d'un cabinet, départ annulable depuis
/// le toaster, relance des créations en échec, en français et en anglais ; et l'écran sans plateforme (poste isolé).
/// </summary>
[Collection("culture")]
public sealed class PlatformCabinetsPageTests : BunitContext
{
    private readonly FakePlatformCabinetsGateway _gateway = new();

    public PlatformCabinetsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<ISessionGateway>(new FakeSessionGateway(new SessionResponse(true, SessionModeDto.Local, "Dominique Plateforme", true)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<PlatformCabinetsPage> RenderWithPlatform()
    {
        Services.AddSingleton<IPlatformCabinetsGateway>(_gateway);
        IRenderedComponent<PlatformCabinetsPage> page = Render<PlatformCabinetsPage>();
        page.WaitForAssertion(() => page.FindAll("#platform-cabinets tbody tr").Should().HaveCount(3));
        return page;
    }

    private static string Row(Guid id) => $"#platform-cabinet-{id:N}";

    [Fact]
    public void Les_compteurs_et_la_liste_montrent_les_cabinets_sans_leurs_donnees()
    {
        IRenderedComponent<PlatformCabinetsPage> page = RenderWithPlatform();

        page.Find("#platform-active").TextContent.Should().Be("1");
        page.Find("#platform-leaving").TextContent.Should().Be("1");
        page.Find("#platform-dossiers").TextContent.Should().Be("7");
        page.Find("#platform-failed").TextContent.Should().Be("1");
        page.Find(Row(FakePlatformCabinetsGateway.Martin)).TextContent.Should().Contain("Cabinet Martin").And.Contain("Actif").And.Contain("cellule fr-par-1");
        page.Find(Row(FakePlatformCabinetsGateway.Durand)).TextContent.Should().Contain("Départ demandé").And.Contain("Annuler le départ");
        page.Find(Row(FakePlatformCabinetsGateway.Ancien)).TextContent.Should().Contain("Parti").And.Contain("le 01/10/2026");
        page.Find("#platform-retry").TextContent.Should().Contain("Relancer les créations en échec (1)");
        page.FindAll("#platform-cannot-onboard").Should().BeEmpty();
    }

    [Fact]
    public void Les_filtres_et_le_tri_s_appliquent_a_l_ecran()
    {
        IRenderedComponent<PlatformCabinetsPage> page = RenderWithPlatform();

        page.FindAll("#platform-cabinets tbody strong").Select(s => s.TextContent).Should().Equal("Ancien cabinet", "Cabinet Durand", "Cabinet Martin");
        page.Find("#platform-sort").Change("dossiers:Descending");
        page.FindAll("#platform-cabinets tbody strong").Select(s => s.TextContent).Should().Equal("Cabinet Martin", "Cabinet Durand", "Ancien cabinet");

        page.Find("#platform-state").Change(nameof(PlatformCabinetStateDto.Leaving));
        page.FindAll("#platform-cabinets tbody tr").Should().ContainSingle().Which.Id.Should().Be(Row(FakePlatformCabinetsGateway.Durand)[1..]);

        page.Find("#platform-search").Input("zzz");
        page.Find("#platform-empty").TextContent.Should().NotBeEmpty();
        page.Find("#platform-empty-reset").Click();
        page.FindAll("#platform-cabinets tbody tr").Should().HaveCount(3);
    }

    [Fact]
    public void Inscrire_un_cabinet_le_montre_dans_la_liste_et_le_dit_dans_le_toaster()
    {
        IRenderedComponent<PlatformCabinetsPage> page = RenderWithPlatform();

        page.Find("#platform-onboard").Click();
        page.Find("#platform-name").Change("Cabinet Leroy");
        page.Find("#platform-admin-name").Change("Alex Leroy");
        page.Find("#platform-admin-email").Change("alex@leroy.example");
        page.Find("#platform-form").Submit();

        page.WaitForAssertion(() => page.FindAll("#platform-cabinets tbody tr").Should().HaveCount(4));
        _gateway.Onboarded.Should().ContainSingle().Which.Name.Should().Be("Cabinet Leroy");
        this.ToastText().Should().Be("Cabinet Leroy est inscrit. alex@leroy.example a reçu l'e-mail pour choisir son mot de passe.");
        page.FindAll("#platform-form").Should().BeEmpty();
    }

    [Fact]
    public void Un_refus_du_serveur_reste_sous_le_formulaire()
    {
        IRenderedComponent<PlatformCabinetsPage> page = RenderWithPlatform();
        _gateway.OnboardErrors = [new ErrorDetail(ErrorCodes.CabinetLifecycle.NameTaken)];

        page.Find("#platform-onboard").Click();
        page.Find("#platform-name").Change("Cabinet Martin");
        page.Find("#platform-admin-name").Change("Alex Leroy");
        page.Find("#platform-admin-email").Change("alex@leroy.example");
        page.Find("#platform-form").Submit();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Un autre cabinet porte déjà ce nom. Choisissez un nom différent."));
        page.FindAll("#platform-form").Should().ContainSingle();
        page.Find("#platform-cancel").Click();
        page.FindAll("#platform-form").Should().BeEmpty();
    }

    [Fact]
    public async Task Un_depart_se_confirme_dans_la_ligne_et_s_annule_depuis_le_toaster()
    {
        IRenderedComponent<PlatformCabinetsPage> page = RenderWithPlatform();

        page.Find($"{Row(FakePlatformCabinetsGateway.Martin)} .lt-row-delete").Click();
        page.Find(".lt-row-confirm-yes").Click();

        page.WaitForAssertion(() => page.Find(Row(FakePlatformCabinetsGateway.Martin)).TextContent.Should().Contain("Départ demandé"));
        this.ToastText().Should().StartWith("Départ de Cabinet Martin demandé : tout sera supprimé le ");
        await page.InvokeAsync(() => this.Toasts()[^1].Action!.Run());

        page.WaitForAssertion(() => page.Find(Row(FakePlatformCabinetsGateway.Martin)).TextContent.Should().Contain("Actif"));
        this.ToastText().Should().Be("Départ de Cabinet Martin annulé : rien n'a changé pour le cabinet.");
    }

    [Fact]
    public void Annuler_un_depart_et_relancer_les_echecs_le_disent_dans_le_toaster()
    {
        IRenderedComponent<PlatformCabinetsPage> page = RenderWithPlatform();

        page.Find($"#platform-cancel-{FakePlatformCabinetsGateway.Durand:N}").Click();
        page.WaitForAssertion(() => page.Find(Row(FakePlatformCabinetsGateway.Durand)).TextContent.Should().Contain("Actif"));

        page.Find("#platform-retry").Click();
        page.WaitForAssertion(() => this.ToastText().Should().Be("1 création(s) de dossier relancée(s)."));
        page.FindAll("#platform-retry").Should().BeEmpty();
    }

    [Fact]
    public void Un_refus_de_depart_est_traduit()
    {
        IRenderedComponent<PlatformCabinetsPage> page = RenderWithPlatform();
        _gateway.DepartureErrors = [new ErrorDetail(ErrorCodes.CabinetLifecycle.DepartureNotCancellable)];

        page.Find($"#platform-cancel-{FakePlatformCabinetsGateway.Durand:N}").Click();

        page.WaitForAssertion(() => this.ToastText().Should().StartWith("Le départ de ce cabinet ne peut plus être annulé"));
    }

    [Fact]
    public void Sans_KMS_ni_compte_de_service_l_inscription_est_expliquee_et_fermee_en_anglais()
    {
        LettraCultures.Apply(CultureInfo.GetCultureInfo("en-GB"));
        _gateway.CanOnboard = false;

        IRenderedComponent<PlatformCabinetsPage> page = RenderWithPlatform();

        page.Find("#platform-cannot-onboard").TextContent.Should().Contain("This cell cannot onboard firms yet");
        page.Find("#platform-onboard").HasAttribute("disabled").Should().BeTrue();
        page.Markup.Should().Contain("Departure requested").And.Contain("Active firms");
    }

    [Fact]
    public void Sur_un_hote_sans_plateforme_l_ecran_le_dit()
    {
        IRenderedComponent<PlatformCabinetsPage> page = Render<PlatformCabinetsPage>();

        page.Find("#platform-unavailable").TextContent.Should().Contain("Pas de plateforme ici");
        page.FindAll("#platform-onboard").Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, "platform", false)]
    [InlineData(false, "", false)]
    [InlineData(true, "platform", true)]
    [InlineData(false, "", true)]
    public void Sans_dossier_l_administrateur_de_la_plateforme_arrive_sur_la_plateforme(bool platformOnly, string landing, bool answerLater)
    {
        Services.AddCabinetFakes();
        Services.AddSingleton<IInvoicesGateway>(new FakeInvoicesGateway());
        Services.AddSingleton<IQuotesGateway>(new FakeQuotesGateway());
        Services.AddFakeBillingDashboard();
        Services.AddSingleton<IAdministrationGateway>(new FakeAdministrationGateway { PlatformOnly = platformOnly, AnswerLater = answerLater });
        Services.AddSingleton<IDossierSelection>(new SwitchableDossierSelection(null));
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();

        IRenderedComponent<Home> home = Render<Home>();

        home.WaitForAssertion(() => navigation.Uri.Should().Be(navigation.BaseUri + landing));
        if (platformOnly)
        {
            home.FindAll("h1").Should().BeEmpty("la plateforme n'affiche rien du tableau de bord");
        }
        else
        {
            // Réponse arrivée plus tard (passerelle HTTP) : le tableau de bord s'affiche quand même.
            home.WaitForAssertion(() => home.FindAll("h1").Should().NotBeEmpty());
        }
    }

    [Fact]
    public void L_entree_du_menu_ne_s_affiche_qu_a_l_administrateur_de_la_plateforme()
    {
        Lettra.UI.Shared.Layout.NavItems.All.Should().ContainSingle(i => i.Href == "platform")
            .Which.Page.Should().Be(AccessAreaDto.Platform);
    }
}

/// <summary>Une plateforme en mémoire : trois cabinets (actif, en partance, parti), une création en échec.</summary>
internal sealed class FakePlatformCabinetsGateway : IPlatformCabinetsGateway
{
    public static readonly Guid Martin = Guid.Parse("0199e3a0-0000-7000-8000-0000000000a1");
    public static readonly Guid Durand = Guid.Parse("0199e3a0-0000-7000-8000-0000000000a2");
    public static readonly Guid Ancien = Guid.Parse("0199e3a0-0000-7000-8000-0000000000a3");
    private static readonly DateTimeOffset Joined = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly List<PlatformCabinetResponse> _cabinets =
    [
        new(Martin, "Cabinet Martin", "fr-par-1", PlatformCabinetStateDto.Active, false, 5, 1, [new PlatformDossierIssue(Guid.NewGuid(), "Dossier Leblanc", "PostgresException")], Joined, null, null),
        new(Durand, "Cabinet Durand", "fr-par-1", PlatformCabinetStateDto.Leaving, false, 2, 0, [], Joined.AddDays(3), Joined.AddDays(60), null),
        new(Ancien, "Ancien cabinet", "fr-par-1", PlatformCabinetStateDto.Departed, false, 0, 0, [], Joined.AddMonths(-6), null, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero)),
    ];

    public bool CanOnboard { get; set; } = true;

    public IReadOnlyList<ErrorDetail> OnboardErrors { get; set; } = [];

    public IReadOnlyList<ErrorDetail> DepartureErrors { get; set; } = [];

    public List<OnboardCabinetRequest> Onboarded { get; } = [];

    public Task<OperationResult<PlatformOverviewResponse>> GetOverviewAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<PlatformOverviewResponse>.Success(new PlatformOverviewResponse(CanOnboard, ["fr-par-1"], [.. _cabinets])));

    public Task<OperationResult<OnboardCabinetResponse>> OnboardAsync(OnboardCabinetRequest request, CancellationToken cancellationToken)
    {
        if (OnboardErrors.Count > 0)
        {
            return Task.FromResult(OperationResult<OnboardCabinetResponse>.Failure([.. OnboardErrors]));
        }

        Onboarded.Add(request);
        PlatformCabinetResponse cabinet = new(Guid.NewGuid(), request.Name, "fr-par-1", PlatformCabinetStateDto.Active, false, 0, 0, [], Joined.AddMonths(2), null, null);
        _cabinets.Add(cabinet);
        return Task.FromResult(OperationResult<OnboardCabinetResponse>.Success(new OnboardCabinetResponse(cabinet, Guid.NewGuid(), InvitationEmailStatusDto.Sent)));
    }

    public Task<OperationResult<PlatformCabinetResponse>> ScheduleDepartureAsync(Guid cabinetId, CancellationToken cancellationToken) =>
        Task.FromResult(Move(cabinetId, PlatformCabinetStateDto.Leaving, Joined.AddMonths(3)));

    public Task<OperationResult<PlatformCabinetResponse>> CancelDepartureAsync(Guid cabinetId, CancellationToken cancellationToken) =>
        Task.FromResult(DepartureErrors.Count > 0
            ? OperationResult<PlatformCabinetResponse>.Failure([.. DepartureErrors])
            : Move(cabinetId, PlatformCabinetStateDto.Active, null));

    public Task<OperationResult<DossierRetryResponse>> RetryFailedDossiersAsync(CancellationToken cancellationToken)
    {
        int resumed = _cabinets.Sum(c => c.FailedDossiers.Count);
        for (int i = 0; i < _cabinets.Count; i++)
        {
            _cabinets[i] = _cabinets[i] with { FailedDossiers = [], ActiveDossiers = _cabinets[i].ActiveDossiers + _cabinets[i].FailedDossiers.Count };
        }

        return Task.FromResult(OperationResult<DossierRetryResponse>.Success(new DossierRetryResponse(resumed)));
    }

    private OperationResult<PlatformCabinetResponse> Move(Guid id, PlatformCabinetStateDto state, DateTimeOffset? due)
    {
        int index = _cabinets.FindIndex(c => c.Id == id);
        _cabinets[index] = _cabinets[index] with { State = state, DepartureDueAt = due };
        return OperationResult<PlatformCabinetResponse>.Success(_cabinets[index]);
    }
}
