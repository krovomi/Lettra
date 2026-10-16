using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Shared.Services;

/// <summary>
/// Session de l'utilisateur, lue une fois pour toute la mise en page (garde des écrans, barre de session, assistant).
/// Les lectures simultanées n'en font qu'une. Sans réponse du serveur (hors ligne), la dernière session connue reste.
/// </summary>
public sealed class SessionState(ISessionGateway sessions)
{
    private Task? _reading;

    public event EventHandler? Changed;

    /// <summary>Dernière session lue ; <c>null</c> avant la première lecture ou après le verrouillage.</summary>
    public SessionResponse? Current { get; private set; }

    /// <summary>La dernière lecture n'a pas eu de réponse (serveur injoignable, navigateur hors ligne).</summary>
    public bool Unreachable { get; private set; }

    public bool IsAuthenticated => Current is { IsAuthenticated: true };

    /// <summary>Relit la session ; une lecture en cours est partagée.</summary>
    public Task RefreshAsync() => _reading is { IsCompleted: false } reading ? reading : _reading = ReadAsync();

    /// <summary>Session fermée sur le poste (verrouillage) : plus rien n'est affiché avant la prochaine lecture.</summary>
    public void Forget()
    {
        Current = null;
        Unreachable = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ReadAsync()
    {
        OperationResult<SessionResponse> result = await sessions.GetAsync(CancellationToken.None).ConfigureAwait(true);
        Unreachable = !result.IsSuccess;
        if (result.IsSuccess)
        {
            Current = result.Value;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
