using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Shared.Security;

/// <summary>Réponse à un code PIN saisi.</summary>
public enum PinCheck
{
    Accepted = 0,
    WrongPin = 1,

    /// <summary>Trop d'essais manqués : plus aucun essai avant <see cref="DeviceLock.BlockedUntil"/>.</summary>
    Blocked = 2,

    /// <summary>Le nouveau code n'est pas de 4 à 12 chiffres.</summary>
    Invalid = 3,
}

/// <summary>
/// Verrou de l'application sur le poste (étape 4.4) : une fois un code PIN défini, l'application démarre verrouillée et
/// se reverrouille à la demande ou quand l'hôte passe en arrière-plan. Le code est haché (PBKDF2-SHA256, sel aléatoire)
/// dans le coffre de l'hôte ; après 5 essais manqués, chaque nouvel échec double l'attente (30 s, 1 min… 1 h au plus),
/// et l'attente survit au redémarrage. Le verrou protège l'écran, pas les données : celles du dossier restent chiffrées
/// par la clé du cabinet (ADR-0011) et sont effacées si le cabinet retire le poste.
/// </summary>
public sealed class DeviceLock : IDisposable
{
    public const int MinLength = 4;
    public const int MaxLength = 12;
    public const int FreeAttempts = 5;

    private const int Iterations = 100_000;
    private static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LongestWait = TimeSpan.FromHours(1);

    private readonly IDeviceLockStore? _store;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Stored? _stored;
    private bool _loaded;

    public DeviceLock(IDeviceLockStore? store, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _clock = clock;
    }

    public event EventHandler? Changed;

    /// <summary>L'hôte a un coffre : le navigateur n'en a pas, et n'y calcule pas PBKDF2.</summary>
    [UnsupportedOSPlatformGuard("browser")]
    public bool IsAvailable => _store is not null && !OperatingSystem.IsBrowser();

    public bool IsLoaded => _loaded || !IsAvailable;

    public bool HasPin => _stored is not null;

    public bool IsLocked { get; private set; }

    public DateTimeOffset? BlockedUntil => _stored?.BlockedUntil is { } until && until > _clock.GetUtcNow() ? until : null;

    /// <summary>Lit le coffre une fois : un poste qui a un code démarre verrouillé.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!IsAvailable || _loaded)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            _stored = Stored.Parse(await _store!.ReadAsync(cancellationToken).ConfigureAwait(false));
            IsLocked = _stored is not null;
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static bool IsValidPin(string? pin) =>
        pin is { Length: >= MinLength and <= MaxLength } && pin.All(char.IsAsciiDigit);

    /// <summary>Verrouille maintenant (bouton, passage en arrière-plan) ; sans code, ne fait rien.</summary>
    public void Lock()
    {
        if (!HasPin || IsLocked)
        {
            return;
        }

        IsLocked = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<PinCheck> UnlockAsync(string pin, CancellationToken cancellationToken) =>
        WithStoreAsync(async () =>
        {
            PinCheck check = await VerifyAsync(pin, cancellationToken).ConfigureAwait(false);
            if (check == PinCheck.Accepted)
            {
                IsLocked = false;
            }

            return check;
        });

    /// <summary>Définit le code, ou le change : il faut alors le code actuel.</summary>
    public Task<PinCheck> SetPinAsync(string? currentPin, string newPin, CancellationToken cancellationToken) =>
        WithStoreAsync(async () =>
        {
            if (!IsValidPin(newPin))
            {
                return PinCheck.Invalid;
            }

            if (_stored is not null)
            {
                PinCheck check = await VerifyAsync(currentPin ?? string.Empty, cancellationToken).ConfigureAwait(false);
                if (check != PinCheck.Accepted)
                {
                    return check;
                }
            }

            byte[] salt = RandomNumberGenerator.GetBytes(16);
            await SaveAsync(new Stored(Iterations, salt, Hash(newPin, salt, Iterations), 0, null), cancellationToken).ConfigureAwait(false);
            IsLocked = false;
            return PinCheck.Accepted;
        });

    /// <summary>Retire le code : l'application ne se verrouille plus.</summary>
    public Task<PinCheck> RemovePinAsync(string currentPin, CancellationToken cancellationToken) =>
        WithStoreAsync(async () =>
        {
            if (_stored is null)
            {
                return PinCheck.Accepted;
            }

            PinCheck check = await VerifyAsync(currentPin, cancellationToken).ConfigureAwait(false);
            if (check == PinCheck.Accepted)
            {
                await SaveAsync(null, cancellationToken).ConfigureAwait(false);
                IsLocked = false;
            }

            return check;
        });

    private async Task<PinCheck> WithStoreAsync(Func<Task<PinCheck>> action)
    {
        if (!IsAvailable)
        {
            return PinCheck.Accepted;
        }

        await LoadAsync(CancellationToken.None).ConfigureAwait(false);
        PinCheck result;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            result = await action().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    private async Task<PinCheck> VerifyAsync(string pin, CancellationToken cancellationToken)
    {
        Stored stored = _stored!;
        DateTimeOffset now = _clock.GetUtcNow();
        if (stored.BlockedUntil is { } until && until > now)
        {
            return PinCheck.Blocked;
        }

        if (CryptographicOperations.FixedTimeEquals(Hash(pin ?? string.Empty, stored.Salt, stored.Iterations), stored.Hash))
        {
            if (stored.Failures > 0)
            {
                await SaveAsync(stored with { Failures = 0, BlockedUntil = null }, cancellationToken).ConfigureAwait(false);
            }

            return PinCheck.Accepted;
        }

        int failures = stored.Failures + 1;
        DateTimeOffset? blocked = failures < FreeAttempts ? null : now + Wait(failures - FreeAttempts);
        await SaveAsync(stored with { Failures = failures, BlockedUntil = blocked }, cancellationToken).ConfigureAwait(false);
        return blocked is null ? PinCheck.WrongPin : PinCheck.Blocked;
    }

    /// <summary>30 s au 5e échec, puis le double à chaque échec, 1 h au plus.</summary>
    public static TimeSpan Wait(int beyondFree) =>
        beyondFree >= 7 ? LongestWait : TimeSpan.FromTicks(Math.Min(FirstWait.Ticks << beyondFree, LongestWait.Ticks));

    private async Task SaveAsync(Stored? stored, CancellationToken cancellationToken)
    {
        await _store!.WriteAsync(stored?.Format(), cancellationToken).ConfigureAwait(false);
        _stored = stored;
    }

    public void Dispose() => _gate.Dispose();

    private static byte[] Hash(string pin, byte[] salt, int iterations) => OperatingSystem.IsBrowser()
        ? throw new PlatformNotSupportedException()
        : Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, 32);

    /// <summary>« v1|itérations|sel|empreinte|échecs|attente (ms Unix) » : une seule valeur dans le coffre.</summary>
    private sealed record Stored(int Iterations, byte[] Salt, byte[] Hash, int Failures, DateTimeOffset? BlockedUntil)
    {
        public string Format() => string.Join('|', "v1", Iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(Salt),
            Convert.ToBase64String(Hash), Failures.ToString(CultureInfo.InvariantCulture),
            BlockedUntil?.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) ?? string.Empty);

        public static Stored? Parse(string? value)
        {
            string[] parts = value?.Split('|') ?? [];
            if (parts is not ["v1", var iterations, var salt, var hash, var failures, var blocked])
            {
                return null;
            }

            return new Stored(int.Parse(iterations, CultureInfo.InvariantCulture), Convert.FromBase64String(salt), Convert.FromBase64String(hash),
                int.Parse(failures, CultureInfo.InvariantCulture),
                blocked.Length == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(blocked, CultureInfo.InvariantCulture)));
        }
    }
}
