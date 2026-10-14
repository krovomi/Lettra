using Lettra.Application.Abstractions.Identity;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Identity.Local;

/// <summary>
/// Session du poste isolé : authentifiée seulement après la phrase secrète, et refermée après
/// <see cref="LocalIdentityOptions.IdleTimeout"/> sans activité. Chaque cas d'usage compte comme une activité.
/// </summary>
internal sealed class LocalSession(IOptions<LocalIdentityOptions> options, TimeProvider clock) : ICurrentUser
{
    private readonly Lock _gate = new();
    private Guid? _userId;
    private DateTimeOffset _lastActivity;

    public bool IsAuthenticated
    {
        get
        {
            lock (_gate)
            {
                if (_userId is null)
                {
                    return false;
                }

                DateTimeOffset now = clock.GetUtcNow();
                if (now - _lastActivity >= options.Value.IdleTimeout)
                {
                    _userId = null;
                    return false;
                }

                _lastActivity = now;
                return true;
            }
        }
    }

    public Guid UserId
    {
        get
        {
            lock (_gate)
            {
                return _userId ?? Guid.Empty;
            }
        }
    }

    public void Open(Guid userId)
    {
        lock (_gate)
        {
            _userId = userId;
            _lastActivity = clock.GetUtcNow();
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            _userId = null;
        }
    }
}
