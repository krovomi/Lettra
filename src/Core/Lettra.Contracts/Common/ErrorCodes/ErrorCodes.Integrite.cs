namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : chaîne anti-fraude (journal chaîné, clôtures, archives scellées).</summary>
public static partial class ErrorCodes
{
    public static class Integrity
    {
        public const string EventInvalid = "Integrity.EventInvalid";
        public const string ClosingNotFound = "Integrity.ClosingNotFound";
        public const string KeyUnavailable = "Integrity.KeyUnavailable";
    }
}
