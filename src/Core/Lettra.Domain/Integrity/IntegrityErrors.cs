using Lettra.Domain.Common;

namespace Lettra.Domain.Integrity;

/// <summary>Codes d'erreur de la chaîne anti-fraude.</summary>
public static class IntegrityErrors
{
    public const string EventInvalidCode = "Integrity.EventInvalid";
    public const string ClosingNotFoundCode = "Integrity.ClosingNotFound";
    public const string KeyUnavailableCode = "Integrity.KeyUnavailable";

    /// <summary>Événement incomplet : le module des pièces doit fournir tous les champs figés.</summary>
    public static Error EventInvalid => Error.Validation(EventInvalidCode);

    public static Error ClosingNotFound => Error.NotFound(ClosingNotFoundCode);

    /// <summary>La clé du cabinet est absente : rien n'est journalisé ni signé sans elle.</summary>
    public static Error KeyUnavailable => new(KeyUnavailableCode, ErrorKind.Unexpected);
}
