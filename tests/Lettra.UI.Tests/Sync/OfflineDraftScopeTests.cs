using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components;
using Lettra.UI.Shared.Drafts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Sync;

/// <summary>Session du BFF scénarisée : connectée, déconnectée ou injoignable.</summary>
internal sealed class ScriptedSessionGateway : ISessionGateway
{
    public OperationResult<SessionResponse> Result { get; set; } = OperationResult<SessionResponse>.Failure(ErrorCodes.Api.Unreachable);

    public string? SignInPath => "/bff/login";

    public Task<OperationResult<SessionResponse>> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Result);

    public Task<OperationResult<SessionResponse>> CreateLocalAccountAsync(CreateLocalAccountRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<SessionResponse>> UnlockAsync(UnlockLocalSessionRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<SessionResponse>> LockAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>
/// Cloisonnement des brouillons du navigateur : une base IndexedDB par utilisateur et par dossier, retrouvée sans réseau
/// seulement dans l'onglet qui l'a ouverte, et effacée à la déconnexion.
/// </summary>
public sealed class OfflineDraftScopeTests : BunitContext
{
    private const string Camille = "4f1c0e5a9b7d2c3e8a6f1b0d9c7e5a3b";
    private static readonly Guid Dossier = Guid.Parse("0199e3a1-0000-7000-8000-00000000000a");
    private static readonly string Scope = $"{Camille}.{Dossier:N}";

    private readonly ScriptedSessionGateway _sessions = new();
    private readonly MutableDossierSelection _dossier = new(Dossier);
    private readonly BunitJSModuleInterop _module;

    public OfflineDraftScopeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        _module = JSInterop.SetupModule("./_content/Lettra.UI.Shared/lettra-drafts.js");
        _module.SetupVoid("rememberScope", _ => true).SetVoidResult();
        _module.SetupVoid("putDraft", _ => true).SetVoidResult();
        _module.SetupVoid("clearScope", _ => true).SetVoidResult();
        _module.Setup<string[]>("listDrafts", _ => true).SetResult([]);
        Services.AddSingleton<ISessionGateway>(_sessions);
        Services.AddSingleton<IDossierSelection>(_dossier);
        Services.AddOfflineDrafts();
    }

    private void Connecte(string? key = Camille) =>
        _sessions.Result = OperationResult<SessionResponse>.Success(
            new SessionResponse(true, SessionModeDto.Oidc, "Camille Martin", false, "/bff/logout?jeton=x", key));

    private static OfflineDraft Brouillon() =>
        new(Guid.NewGuid(), OfflineDraftKind.Quote, null, null, "Dupont SARL", "{}", OfflineDrafts.HashOf("{}"), DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task En_ligne_les_brouillons_vont_dans_la_base_de_l_utilisateur_et_du_dossier()
    {
        Connecte();
        await using AsyncServiceScope portee = Services.CreateAsyncScope();
        IOfflineDraftStore store = portee.ServiceProvider.GetRequiredService<IOfflineDraftStore>();

        await store.SaveAsync(Brouillon(), CancellationToken.None);

        _module.VerifyInvoke("rememberScope").Arguments.Should().Equal(Scope);
        _module.VerifyInvoke("putDraft").Arguments[2].Should().Be(Scope);
    }

    [Fact]
    public async Task Sans_session_ou_sans_cle_de_l_utilisateur_rien_n_est_lu_ni_garde()
    {
        _sessions.Result = OperationResult<SessionResponse>.Success(new SessionResponse(false, SessionModeDto.Oidc, null, false));
        await using AsyncServiceScope portee = Services.CreateAsyncScope();
        IOfflineDraftStore store = portee.ServiceProvider.GetRequiredService<IOfflineDraftStore>();

        (await store.IsAvailableAsync(CancellationToken.None)).Should().BeFalse();
        (await store.ListAsync(CancellationToken.None)).Should().BeEmpty();
        Connecte(key: null);
        (await store.IsAvailableAsync(CancellationToken.None)).Should().BeFalse();

        _module.VerifyNotInvoke("listDrafts");
        await FluentActions.Awaiting(() => store.SaveAsync(Brouillon(), CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Sans_reseau_seul_l_onglet_qui_a_ouvert_la_base_la_retrouve_et_pour_le_meme_dossier()
    {
        JSRuntimeInvocationHandler<string?> remembered = _module.Setup<string?>("rememberedScope");
        remembered.SetResult(null);
        await using AsyncServiceScope portee = Services.CreateAsyncScope();
        IOfflineDraftStore store = portee.ServiceProvider.GetRequiredService<IOfflineDraftStore>();
        (await store.IsAvailableAsync(CancellationToken.None)).Should().BeFalse("nouvel onglet : aucun périmètre retenu");

        remembered.SetResult($"{Camille}.{Guid.NewGuid():N}");
        (await store.IsAvailableAsync(CancellationToken.None)).Should().BeFalse("périmètre d'un autre dossier");

        remembered.SetResult(Scope);
        await store.ListAsync(CancellationToken.None);
        _module.VerifyInvoke("listDrafts").Arguments.Should().Equal(Scope);
    }

    [Fact]
    public async Task L_effacement_supprime_la_base_du_perimetre()
    {
        Connecte();
        await using AsyncServiceScope portee = Services.CreateAsyncScope();
        IOfflineDraftStore store = portee.ServiceProvider.GetRequiredService<IOfflineDraftStore>();

        await store.ClearAsync(CancellationToken.None);

        _module.VerifyInvoke("clearScope").Arguments.Should().Equal(Scope);
    }
}

/// <summary>La déconnexion du navigateur efface les brouillons de l'appareil, après confirmation s'il en reste à envoyer.</summary>
public sealed class SignOutDraftsTests : BunitContext
{
    private static readonly Guid Dossier = Guid.Parse("0199e3a1-0000-7000-8000-00000000000a");

    private readonly InMemoryDraftStore _store = new();
    private readonly ScriptedSessionGateway _sessions = new();

    public SignOutDraftsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IOfflineDraftStore>(_store);
        Services.AddSingleton<IDossierSelection>(new FixedDossierSelection(Dossier));
        Services.AddOfflineDrafts();
        Services.AddSingleton<ISessionGateway>(_sessions);
        _sessions.Result = OperationResult<SessionResponse>.Success(
            new SessionResponse(true, SessionModeDto.Oidc, "Camille Martin", false, "/bff/logout?jeton=x", "cle"));
        // Un brouillon d'un autre dossier : il ne peut pas partir, il reste à la déconnexion.
        OfflineDraft reste = new(Guid.NewGuid(), OfflineDraftKind.Quote, null, null, "Dupont SARL", "{}", OfflineDrafts.HashOf("{}"),
            DateTimeOffset.UnixEpoch, DossierId: Guid.NewGuid());
        _store.Drafts[reste.LocalId] = reste;
        _store.Cache["billing.reference"] = "{}";
    }

    [Fact]
    public void Se_deconnecter_efface_les_brouillons_et_le_cache_apres_confirmation()
    {
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        IRenderedComponent<SessionBar> barre = Render<SessionBar>();

        barre.WaitForElement("[data-sign-out]").Click();

        barre.WaitForAssertion(() => _store.Drafts.Should().BeEmpty());
        _store.Cache.Should().BeEmpty();
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/bff/logout?jeton=x");
    }

    [Fact]
    public void Refuser_la_confirmation_garde_les_brouillons_et_la_session()
    {
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        IRenderedComponent<SessionBar> barre = Render<SessionBar>();

        barre.WaitForElement("[data-sign-out]").Click();

        barre.WaitForAssertion(() => JSInterop.VerifyInvoke("confirm"));
        _store.Drafts.Should().ContainSingle();
        Services.GetRequiredService<NavigationManager>().Uri.Should().NotContain("logout");
    }
}
