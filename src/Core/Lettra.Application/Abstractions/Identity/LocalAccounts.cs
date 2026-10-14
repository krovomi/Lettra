using Lettra.Domain.Common;

namespace Lettra.Application.Abstractions.Identity;

/// <summary>État du compte local du poste isolé (mode 1).</summary>
public sealed record LocalAccountStatus(bool Exists, bool IsUnlocked, string? DisplayName);

/// <summary>
/// Compte local du poste isolé (mode 1, annexe F §2) : une phrase secrète déverrouille la session, et plus tard
/// la clé de la base SQLite chiffrée. Pas d'implémentation hors du mode 1.
/// </summary>
public interface ILocalAccounts
{
    ValueTask<LocalAccountStatus> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>Crée le compte au premier lancement et ouvre la session. Échoue si un compte existe déjà.</summary>
    ValueTask<Result> CreateAsync(string displayName, string passphrase, CancellationToken cancellationToken);

    /// <summary>Ouvre la session ; verrouille le compte après trop d'échecs.</summary>
    ValueTask<Result> UnlockAsync(string passphrase, CancellationToken cancellationToken);

    void Lock();
}

public static class LocalAccountErrors
{
    /// <summary>Longueur minimale de la phrase secrète (reprise par <c>CreateLocalAccountRequest</c>).</summary>
    public const int PassphraseMinLength = 12;

    public const string DisplayNameRequiredCode = "Validation.Required";
    public const string AlreadyExistsCode = "LocalAccount.AlreadyExists";
    public const string NotFoundCode = "LocalAccount.NotFound";
    public const string InvalidPassphraseCode = "LocalAccount.InvalidPassphrase";
    public const string LockedOutCode = "LocalAccount.LockedOut";
    public const string PassphraseTooShortCode = "LocalAccount.PassphraseTooShort";
    public const string NotSupportedCode = "LocalAccount.NotSupported";

    public static Error AlreadyExists => Error.Conflict(AlreadyExistsCode);

    public static Error NotFound => Error.NotFound(NotFoundCode);

    /// <summary>Phrase secrète erronée. Même code que le compte inconnu n'existe pas en mode 1 (un seul compte).</summary>
    public static Error InvalidPassphrase => new(InvalidPassphraseCode, ErrorKind.Unauthorized, "Passphrase");

    public static Error LockedOut => new(LockedOutCode, ErrorKind.Forbidden);

    public static Error PassphraseTooShort => Error.Validation(PassphraseTooShortCode, "Passphrase");

    public static Error NotSupported => Error.Conflict(NotSupportedCode);

    public static Error DisplayNameRequired => Error.Validation(DisplayNameRequiredCode, "DisplayName");
}
