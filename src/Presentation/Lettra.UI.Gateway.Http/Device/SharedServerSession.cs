using System.Net.Http.Json;
using System.Text.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Identity;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http.Device;

/// <summary>
/// Le dossier ouvert par l'application installée sur le serveur commun : choisi à la connexion, gardé sur l'appareil
/// (l'hôte fournit <paramref name="remember"/>) pour rouvrir le même dossier au lancement suivant, même hors réseau.
/// </summary>
public sealed class SharedServerDossierSelection(Guid? remembered, Action<Guid?> remember) : IDossierSelection
{
    public Guid? CurrentDossierId { get; private set; } = remembered;

    internal void Choose(Guid? dossierId)
    {
        CurrentDossierId = dossierId;
        remember(dossierId);
    }
}

/// <summary>
/// Choisit le dossier à ouvrir après la connexion : celui qu'ouvre le navigateur sur ce serveur (son
/// <c>appsettings.json</c>) s'il est accessible à l'utilisateur, sinon le premier de ses dossiers. Le navigateur et les
/// applications installées montrent ainsi les mêmes données.
/// </summary>
public sealed class SharedServerDossierChooser(HttpClient http, SharedServerDossierSelection selection)
{
    public async Task<OperationResult<Guid>> ChooseAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(new Uri("api/v1/dossiers", UriKind.Relative), cancellationToken).ConfigureAwait(false);
            OperationResult<IReadOnlyList<DossierSummary>> listed = await response
                .ToOperationResultAsync(ContractsJsonContext.Default.IReadOnlyListDossierSummary, cancellationToken)
                .ConfigureAwait(false);
            if (!listed.IsSuccess)
            {
                return OperationResult<Guid>.Failure(listed.Errors);
            }

            IReadOnlyList<DossierSummary> dossiers = listed.Value ?? [];
            Guid? browser = await BrowserDossierAsync(cancellationToken).ConfigureAwait(false);
            DossierSummary? chosen = dossiers.FirstOrDefault(d => d.Id == browser) ?? (dossiers.Count > 0 ? dossiers[0] : null);
            if (chosen is null)
            {
                return OperationResult<Guid>.Failure(ErrorCodes.SharedServer.NoDossier);
            }

            selection.Choose(chosen.Id);
            return OperationResult<Guid>.Success(chosen.Id);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return OperationResult<Guid>.Failure(ErrorCodes.Api.Unreachable);
        }
    }

    /// <summary>Le dossier configuré pour l'application du navigateur (<c>Lettra:DossierId</c>), s'il y en a un.</summary>
    private async Task<Guid?> BrowserDossierAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(new Uri("appsettings.json", UriKind.Relative), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using JsonDocument settings = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return settings.RootElement.TryGetProperty("Lettra", out JsonElement lettra)
                && lettra.TryGetProperty("DossierId", out JsonElement id)
                && Guid.TryParse(id.GetString(), out Guid dossier)
                ? dossier
                : null;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>
/// Session de l'application installée sur le serveur commun : la connexion du cabinet (PKCE, <see cref="DeviceSessionGateway"/>),
/// puis le choix du dossier, avant le premier écran. La déconnexion oublie le dossier.
/// </summary>
public sealed class SharedServerSessionGateway(
    DeviceSessionGateway inner,
    SharedServerDossierChooser chooser,
    SharedServerDossierSelection selection) : ISessionGateway, IInteractiveSignIn
{
    public string? SignInPath => inner.SignInPath;

    public async Task<OperationResult<SessionResponse>> GetAsync(CancellationToken cancellationToken)
    {
        OperationResult<SessionResponse> session = await inner.GetAsync(cancellationToken).ConfigureAwait(false);
        if (session.Value is { IsAuthenticated: true } && selection.CurrentDossierId is null)
        {
            // Connecté lors d'un lancement précédent sans dossier retenu (réseau coupé à la connexion) : nouvel essai.
            await chooser.ChooseAsync(cancellationToken).ConfigureAwait(false);
        }

        return session;
    }

    public async Task<OperationResult<SessionResponse>> SignInAsync(CancellationToken cancellationToken)
    {
        OperationResult<SessionResponse> signedIn = await inner.SignInAsync(cancellationToken).ConfigureAwait(false);
        if (!signedIn.IsSuccess)
        {
            return signedIn;
        }

        OperationResult<Guid> dossier = await chooser.ChooseAsync(cancellationToken).ConfigureAwait(false);
        return dossier.IsSuccess ? signedIn : OperationResult<SessionResponse>.Failure(dossier.Errors);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await inner.SignOutAsync(cancellationToken).ConfigureAwait(false);
        selection.Choose(null);
    }

    public Task<OperationResult<SessionResponse>> CreateLocalAccountAsync(CreateLocalAccountRequest request, CancellationToken cancellationToken) =>
        inner.CreateLocalAccountAsync(request, cancellationToken);

    public Task<OperationResult<SessionResponse>> UnlockAsync(UnlockLocalSessionRequest request, CancellationToken cancellationToken) =>
        inner.UnlockAsync(request, cancellationToken);

    public Task<OperationResult<SessionResponse>> LockAsync(CancellationToken cancellationToken) => inner.LockAsync(cancellationToken);
}
