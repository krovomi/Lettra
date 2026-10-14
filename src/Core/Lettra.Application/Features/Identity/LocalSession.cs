using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Identity;
using Lettra.Domain.Common;

namespace Lettra.Application.Features.Identity;

// Session du poste isolé (mode 1). Publics par nature : ils servent à s'authentifier.

/// <summary>État de la session locale.</summary>
[PublicUseCase]
public sealed record GetLocalSessionQuery : IQuery<Result<SessionResponse>>;

/// <summary>Créer le compte du poste au premier lancement.</summary>
[PublicUseCase]
public sealed record CreateLocalAccountCommand(string DisplayName, string Passphrase) : ICommand<Result<SessionResponse>>
{
    /// <summary>La phrase secrète ne doit jamais apparaître dans une trace.</summary>
    public override string ToString() => nameof(CreateLocalAccountCommand);
}

/// <summary>Déverrouiller le poste.</summary>
[PublicUseCase]
public sealed record OpenLocalSessionCommand(string Passphrase) : ICommand<Result<SessionResponse>>
{
    /// <summary>La phrase secrète ne doit jamais apparaître dans une trace.</summary>
    public override string ToString() => nameof(OpenLocalSessionCommand);
}

/// <summary>Verrouiller le poste.</summary>
[PublicUseCase]
public sealed record CloseLocalSessionCommand : ICommand<Result<SessionResponse>>;

internal sealed class LocalSessionHandlers(ILocalAccounts accounts) :
    IQueryHandler<GetLocalSessionQuery, Result<SessionResponse>>,
    ICommandHandler<CreateLocalAccountCommand, Result<SessionResponse>>,
    ICommandHandler<OpenLocalSessionCommand, Result<SessionResponse>>,
    ICommandHandler<CloseLocalSessionCommand, Result<SessionResponse>>
{
    public async Task<Result<SessionResponse>> HandleAsync(GetLocalSessionQuery query, CancellationToken cancellationToken) =>
        await SessionAsync(cancellationToken).ConfigureAwait(false);

    public async Task<Result<SessionResponse>> HandleAsync(CreateLocalAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result created = await accounts.CreateAsync(command.DisplayName, command.Passphrase, cancellationToken).ConfigureAwait(false);
        return created.IsFailure ? Result<SessionResponse>.Failure(created.Errors) : await SessionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<SessionResponse>> HandleAsync(OpenLocalSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result opened = await accounts.UnlockAsync(command.Passphrase, cancellationToken).ConfigureAwait(false);
        return opened.IsFailure ? Result<SessionResponse>.Failure(opened.Errors) : await SessionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<SessionResponse>> HandleAsync(CloseLocalSessionCommand command, CancellationToken cancellationToken)
    {
        accounts.Lock();
        return await SessionAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<SessionResponse>> SessionAsync(CancellationToken cancellationToken)
    {
        LocalAccountStatus status = await accounts.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return new SessionResponse(status.IsUnlocked, SessionModeDto.Local, status.IsUnlocked ? status.DisplayName : null, status.Exists);
    }
}
