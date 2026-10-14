using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests;

internal sealed class FakeSessionGateway(SessionResponse initial, string? signInPath = null) : ISessionGateway
{
    public SessionResponse Current { get; private set; } = initial;

    public string? Passphrase { get; set; }

    public string? SignInPath { get; } = signInPath;

    public Task<OperationResult<SessionResponse>> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<SessionResponse>.Success(Current));

    public Task<OperationResult<SessionResponse>> CreateLocalAccountAsync(CreateLocalAccountRequest request, CancellationToken cancellationToken)
    {
        Passphrase = request.Passphrase;
        Current = new SessionResponse(true, SessionModeDto.Local, request.DisplayName, true);
        return Task.FromResult(OperationResult<SessionResponse>.Success(Current));
    }

    public Task<OperationResult<SessionResponse>> UnlockAsync(UnlockLocalSessionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(request.Passphrase == Passphrase
            ? OperationResult<SessionResponse>.Success(Current = Current with { IsAuthenticated = true })
            : OperationResult<SessionResponse>.Failure([new ErrorDetail(ErrorCodes.LocalAccount.InvalidPassphrase, "Passphrase")]));

    public Task<OperationResult<SessionResponse>> LockAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<SessionResponse>.Success(Current = Current with { IsAuthenticated = false }));
}

[Collection("culture")]
public sealed class SessionPageTests : BunitContext
{
    public SessionPageTests()
    {
        Services.AddLettraUi();
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    [Fact]
    public void Premier_lancement_propose_de_creer_le_compte_du_poste()
    {
        FakeSessionGateway gateway = new(new SessionResponse(false, SessionModeDto.Local, null, LocalAccountExists: false));
        Services.AddSingleton<ISessionGateway>(gateway);

        IRenderedComponent<Session> page = Render<Session>();
        page.Find("#display-name").Change("Claire Martin");
        page.Find("#passphrase").Change("une phrase assez longue");
        page.Find("#confirmation").Change("une phrase assez longue");
        page.Find("form").Submit();

        gateway.Current.IsAuthenticated.Should().BeTrue();
        Services.GetRequiredService<NavigationManager>().Uri.Should().Be("http://localhost/");
    }

    [Fact]
    public void Une_phrase_trop_courte_ou_differente_est_refusee_sans_appel()
    {
        FakeSessionGateway gateway = new(new SessionResponse(false, SessionModeDto.Local, null, LocalAccountExists: false));
        Services.AddSingleton<ISessionGateway>(gateway);

        IRenderedComponent<Session> page = Render<Session>();
        page.Find("#display-name").Change("Claire");
        page.Find("#passphrase").Change("court");
        page.Find("form").Submit();
        page.Markup.Should().Contain("au moins 12 caractères");

        page.Find("#passphrase").Change("une phrase assez longue");
        page.Find("#confirmation").Change("une autre phrase longue");
        page.Find("form").Submit();
        page.Markup.Should().Contain("ne sont pas identiques");

        gateway.Passphrase.Should().BeNull();
    }

    [Fact]
    public void Une_mauvaise_phrase_secrete_affiche_un_message_traduit()
    {
        FakeSessionGateway gateway = new(new SessionResponse(false, SessionModeDto.Local, null, LocalAccountExists: true)) { Passphrase = "la bonne phrase secrète" };
        Services.AddSingleton<ISessionGateway>(gateway);

        IRenderedComponent<Session> page = Render<Session>();
        page.Find("#passphrase").Change("une mauvaise phrase");
        page.Find("form").Submit();

        page.Markup.Should().Contain("Phrase secrète incorrecte.");
        page.Find("#passphrase").GetAttribute("value").Should().BeNullOrEmpty();
    }

    [Fact]
    public void Dans_le_navigateur_la_connexion_passe_par_le_bff()
    {
        Services.AddSingleton<ISessionGateway>(new FakeSessionGateway(new SessionResponse(false, SessionModeDto.Oidc, null, false), "bff/login?returnUrl=%2F"));

        IRenderedComponent<Session> page = Render<Session>();

        page.Find("a[data-sign-in]").GetAttribute("href").Should().Be("bff/login?returnUrl=%2F");
        page.FindAll("input[type=password]").Should().BeEmpty("le navigateur ne voit jamais de mot de passe ni de jeton");
    }
}
