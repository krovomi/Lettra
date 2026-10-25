using System.Net;
using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

/// <summary>Session du navigateur : lue sur le BFF (<c>/bff/user</c>). Pas de compte local côté navigateur.</summary>
internal sealed class HttpSessionGateway(HttpClient http) : ISessionGateway
{
    private static readonly SessionResponse SignedOut = new(IsAuthenticated: false, SessionModeDto.Oidc, DisplayName: null, LocalAccountExists: false);

    public string? SignInPath => "bff/login?returnUrl=%2F";

    public Task<OperationResult<SessionResponse>> GetAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(new Uri("bff/user", UriKind.Relative), ct),
            (response, ct) => response.StatusCode == HttpStatusCode.Unauthorized
                ? Task.FromResult(OperationResult<SessionResponse>.Success(SignedOut))
                : response.ToOperationResultAsync(ContractsJsonContext.Default.SessionResponse, ct),
            cancellationToken);

    public Task<OperationResult<SessionResponse>> CreateLocalAccountAsync(CreateLocalAccountRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<SessionResponse>.Failure(ErrorCodes.LocalAccount.NotSupported));

    public Task<OperationResult<SessionResponse>> UnlockAsync(UnlockLocalSessionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<SessionResponse>.Failure(ErrorCodes.LocalAccount.NotSupported));

    public Task<OperationResult<SessionResponse>> LockAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<SessionResponse>.Failure(ErrorCodes.LocalAccount.NotSupported));
}
