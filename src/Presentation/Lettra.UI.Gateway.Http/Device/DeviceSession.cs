using System.Net.Http.Headers;
using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http.Device;

/// <summary>Ajoute le jeton d'accès du poste à chaque appel de l'API, renouvelé si besoin.</summary>
public sealed class BearerTokenHandler(OidcPkceClient client) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? token = await client.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Session de l'application de bureau reliée à un serveur : connectée tant que le coffre garde des jetons. La connexion
/// passe par la page <see cref="SignInRoute"/> de l'UI, qui appelle <see cref="IInteractiveSignIn"/>.
/// </summary>
public sealed class DeviceSessionGateway(OidcPkceClient client, ITokenVault vault) : ISessionGateway, IInteractiveSignIn
{
    public const string SignInRoute = "session/connexion";
    public const string SignOutRoute = "session/deconnexion";

    public string? SignInPath => SignInRoute;

    public async Task<OperationResult<SessionResponse>> GetAsync(CancellationToken cancellationToken)
    {
        DeviceTokens? tokens = await vault.ReadAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<SessionResponse>.Success(Session(tokens));
    }

    public async Task<OperationResult<SessionResponse>> SignInAsync(CancellationToken cancellationToken)
    {
        OperationResult<DeviceTokens> signedIn = await client.SignInAsync(cancellationToken).ConfigureAwait(false);
        return signedIn.IsSuccess
            ? OperationResult<SessionResponse>.Success(Session(signedIn.Value))
            : OperationResult<SessionResponse>.Failure(signedIn.Errors);
    }

    public Task SignOutAsync(CancellationToken cancellationToken) => client.SignOutAsync(cancellationToken);

    public Task<OperationResult<SessionResponse>> CreateLocalAccountAsync(CreateLocalAccountRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<SessionResponse>.Failure(ErrorCodes.LocalAccount.NotSupported));

    public Task<OperationResult<SessionResponse>> UnlockAsync(UnlockLocalSessionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<SessionResponse>.Failure(ErrorCodes.LocalAccount.NotSupported));

    public Task<OperationResult<SessionResponse>> LockAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<SessionResponse>.Failure(ErrorCodes.LocalAccount.NotSupported));

    private static SessionResponse Session(DeviceTokens? tokens) => tokens is null
        ? new SessionResponse(IsAuthenticated: false, SessionModeDto.Oidc, DisplayName: null, LocalAccountExists: false)
        : new SessionResponse(IsAuthenticated: true, SessionModeDto.Oidc, tokens.DisplayName, LocalAccountExists: false, SignOutUrl: SignOutRoute);
}
