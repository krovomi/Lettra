using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Tests.Sync;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests;

/// <summary>
/// Garde des écrans : sans session ouverte, aucun écran ne lit le dossier (pas d'appel refusé en 401 dans la console du
/// navigateur avant la connexion) et l'utilisateur va à l'écran de session.
/// </summary>
[Collection("culture")]
public sealed class SessionGateTests : BunitContext
{
    private readonly ScriptedSessionGateway _sessions = new();

    public SessionGateTests()
    {
        Services.AddLettraUi();
        Services.AddSingleton<ISessionGateway>(_sessions);
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<SessionGate> Gate(bool redirect = true) =>
        Render<SessionGate>(p => p
            .Add(g => g.Redirect, redirect)
            .AddChildContent("<p id=\"ecran\">écran du dossier</p>"));

    private static OperationResult<SessionResponse> Session(bool authenticated) =>
        OperationResult<SessionResponse>.Success(new SessionResponse(authenticated, SessionModeDto.Oidc, authenticated ? "Camille" : null, false));

    [Fact]
    public void Sans_session_l_utilisateur_va_a_la_session()
    {
        _sessions.Result = Session(authenticated: false);
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("quotes");

        Gate();

        navigation.Uri.Should().Be("http://localhost/session");
    }

    [Fact]
    public void Avec_une_session_ouverte_l_ecran_est_rendu()
    {
        _sessions.Result = Session(authenticated: true);
        Services.GetRequiredService<NavigationManager>().NavigateTo("quotes");

        IRenderedComponent<SessionGate> gate = Gate();

        gate.Find("#ecran").TextContent.Should().Be("écran du dossier");
    }

    [Fact]
    public void Hors_ligne_sans_session_connue_l_ecran_reste_disponible_pour_les_brouillons()
    {
        _sessions.Result = OperationResult<SessionResponse>.Failure(ErrorCodes.Api.Unreachable);
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("quotes/new");

        IRenderedComponent<SessionGate> gate = Gate();

        gate.Find("#ecran").Should().NotBeNull();
        navigation.Uri.Should().Be("http://localhost/quotes/new");
    }

    [Fact]
    public void Sans_session_l_ecran_n_est_pas_rendu()
    {
        _sessions.Result = Session(authenticated: false);
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("quotes");

        IRenderedComponent<SessionGate> gate = Gate(redirect: false);

        gate.Markup.Trim().Should().BeEmpty();
        navigation.Uri.Should().Be("http://localhost/quotes");
    }

    [Fact]
    public void La_session_perdue_en_cours_de_route_renvoie_a_la_session_a_la_navigation_suivante()
    {
        _sessions.Result = Session(authenticated: true);
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        IRenderedComponent<SessionGate> gate = Gate();
        gate.Find("#ecran");

        _sessions.Result = Session(authenticated: false);
        navigation.NavigateTo("invoices");

        gate.WaitForAssertion(() => navigation.Uri.Should().Be("http://localhost/session"));
    }

    [Fact]
    public void Sur_l_ecran_de_session_le_contenu_sans_renvoi_attend_la_connexion()
    {
        _sessions.Result = Session(authenticated: false);
        Services.GetRequiredService<NavigationManager>().NavigateTo("session");

        Gate(redirect: false).Markup.Trim().Should().BeEmpty("l'assistant ne lit rien avant la connexion");
        Gate().Find("#ecran").Should().NotBeNull("l'écran de session lui-même s'affiche");
    }
}
