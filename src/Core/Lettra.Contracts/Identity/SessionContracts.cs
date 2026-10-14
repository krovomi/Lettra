using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Identity;

/// <summary>Comment l'utilisateur s'authentifie.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SessionModeDto>))]
public enum SessionModeDto
{
    /// <summary>Compte local du poste isolé (mode 1) : phrase secrète.</summary>
    Local = 1,

    /// <summary>Keycloak (SSO du cabinet, MFA) ; le navigateur passe par le BFF.</summary>
    Oidc = 2,
}

/// <summary>État de la session, tel que l'affiche l'UI. Jamais de jeton ni de droit : seulement qui est connecté.</summary>
/// <param name="IsAuthenticated">Une session est ouverte.</param>
/// <param name="Mode">Mode d'authentification de l'hôte.</param>
/// <param name="DisplayName">Nom affiché de l'utilisateur, s'il est connecté.</param>
/// <param name="LocalAccountExists">Mode local : le compte du poste a déjà été créé (sinon l'UI propose de le créer).</param>
/// <param name="SignOutUrl">Navigateur : adresse de déconnexion du BFF, protégée par un jeton propre à la session.</param>
public sealed record SessionResponse(bool IsAuthenticated, SessionModeDto Mode, string? DisplayName, bool LocalAccountExists, string? SignOutUrl = null);

/// <summary>Création du compte local du poste, au premier lancement. Propriétés modifiables pour <c>EditForm</c>.</summary>
public sealed record CreateLocalAccountRequest : IValidatableObject
{
    public const int PassphraseMinLength = 12;

    [Required(ErrorMessage = ErrorCodes.Validation.Required)]
    [StringLength(100, ErrorMessage = ErrorCodes.Validation.TooLong)]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = ErrorCodes.LocalAccount.PassphraseTooShort)]
    [StringLength(256, MinimumLength = PassphraseMinLength, ErrorMessage = ErrorCodes.LocalAccount.PassphraseTooShort)]
    public string Passphrase { get; set; } = string.Empty;

    public string PassphraseConfirmation { get; set; } = string.Empty;

    /// <summary>Confirmation identique (sans <c>[Compare]</c>, qui ne survit pas au trimming WASM).</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.Equals(Passphrase, PassphraseConfirmation, StringComparison.Ordinal))
        {
            yield return new ValidationResult(ErrorCodes.LocalAccount.PassphraseMismatch, [nameof(PassphraseConfirmation)]);
        }
    }

    /// <summary>La phrase secrète ne doit jamais apparaître dans une trace.</summary>
    public override string ToString() => nameof(CreateLocalAccountRequest);
}

/// <summary>Déverrouillage du poste par la phrase secrète.</summary>
public sealed record UnlockLocalSessionRequest
{
    [Required(ErrorMessage = ErrorCodes.LocalAccount.InvalidPassphrase)]
    [StringLength(256, ErrorMessage = ErrorCodes.LocalAccount.InvalidPassphrase)]
    public string Passphrase { get; set; } = string.Empty;

    /// <summary>La phrase secrète ne doit jamais apparaître dans une trace.</summary>
    public override string ToString() => nameof(UnlockLocalSessionRequest);
}
