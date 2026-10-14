using Lettra.Application.Abstractions.Identity;
using Lettra.Domain.Common;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Identity.Local;

/// <summary>Le compte unique du poste isolé : création au premier lancement, déverrouillage, verrouillage après échecs.</summary>
internal sealed class LocalAccounts(
    LocalAccountStore store,
    LocalSession session,
    IOptions<LocalIdentityOptions> options,
    TimeProvider clock) : ILocalAccounts, IDisposable
{
    private static readonly TimeSpan MaxLockout = TimeSpan.FromHours(24);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<LocalAccountStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        LocalAccountFile? account = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
        return new LocalAccountStatus(account is not null, account is not null && session.IsAuthenticated && session.UserId == account.UserId, account?.DisplayName);
    }

    public async ValueTask<Result> CreateAsync(string displayName, string passphrase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passphrase);
        if (passphrase.Length < LocalAccountErrors.PassphraseMinLength)
        {
            return LocalAccountErrors.PassphraseTooShort;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return LocalAccountErrors.DisplayNameRequired;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await store.ReadAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                return LocalAccountErrors.AlreadyExists;
            }

            int iterations = options.Value.Pbkdf2Iterations;
            (byte[] salt, byte[] hash) = PassphraseHasher.Hash(passphrase, iterations);
            LocalAccountFile account = new()
            {
                UserId = Guid.CreateVersion7(),
                DisplayName = displayName.Trim(),
                Salt = salt,
                Hash = hash,
                Iterations = iterations,
                CreatedAt = clock.GetUtcNow(),
            };
            await store.WriteAsync(account, cancellationToken).ConfigureAwait(false);
            session.Open(account.UserId);
            return Result.Success();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<Result> UnlockAsync(string passphrase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passphrase);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LocalAccountFile? account = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (account is null)
            {
                return LocalAccountErrors.NotFound;
            }

            DateTimeOffset now = clock.GetUtcNow();
            if (account.LockedUntil is DateTimeOffset until && until > now)
            {
                return LocalAccountErrors.LockedOut;
            }

            if (PassphraseHasher.Verify(passphrase, account.Salt, account.Hash, account.Iterations))
            {
                if (account.FailedAttempts > 0 || account.LockedUntil is not null || account.Iterations < options.Value.Pbkdf2Iterations)
                {
                    await store.WriteAsync(Rehash(account, passphrase) with { FailedAttempts = 0, Lockouts = 0, LockedUntil = null }, cancellationToken).ConfigureAwait(false);
                }

                session.Open(account.UserId);
                return Result.Success();
            }

            int failures = account.FailedAttempts + 1;
            LocalAccountFile failed = account with { FailedAttempts = failures };
            if (failures >= options.Value.MaxFailedAttempts)
            {
                double factor = Math.Pow(2, Math.Min(account.Lockouts, 16));
                TimeSpan lockout = TimeSpan.FromTicks((long)Math.Min(options.Value.LockoutDuration.Ticks * factor, MaxLockout.Ticks));
                failed = failed with { FailedAttempts = 0, Lockouts = account.Lockouts + 1, LockedUntil = now + lockout };
            }

            await store.WriteAsync(failed, cancellationToken).ConfigureAwait(false);
            return failed.LockedUntil > now ? LocalAccountErrors.LockedOut : LocalAccountErrors.InvalidPassphrase;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Lock() => session.Close();

    public void Dispose() => _gate.Dispose();

    /// <summary>Relève le nombre d'itérations d'un ancien hachage, au passage.</summary>
    private LocalAccountFile Rehash(LocalAccountFile account, string passphrase)
    {
        if (account.Iterations >= options.Value.Pbkdf2Iterations)
        {
            return account;
        }

        (byte[] salt, byte[] hash) = PassphraseHasher.Hash(passphrase, options.Value.Pbkdf2Iterations);
        return account with { Salt = salt, Hash = hash, Iterations = options.Value.Pbkdf2Iterations };
    }
}
