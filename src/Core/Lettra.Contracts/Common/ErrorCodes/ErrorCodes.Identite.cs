namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : identité, sécurité et administration des droits.</summary>
public static partial class ErrorCodes
{
    public static class Security
    {
        public const string Unauthenticated = "Security.Unauthenticated";
        public const string Forbidden = "Security.Forbidden";
        public const string MfaRequired = "Security.MfaRequired";
        public const string CsrfHeaderMissing = "Security.CsrfHeaderMissing";
    }

    public static class LocalAccount
    {
        public const string AlreadyExists = "LocalAccount.AlreadyExists";
        public const string NotFound = "LocalAccount.NotFound";
        public const string InvalidPassphrase = "LocalAccount.InvalidPassphrase";
        public const string LockedOut = "LocalAccount.LockedOut";
        public const string PassphraseTooShort = "LocalAccount.PassphraseTooShort";
        public const string PassphraseMismatch = "LocalAccount.PassphraseMismatch";
        public const string NotSupported = "LocalAccount.NotSupported";
    }

    public static class Rights
    {
        public const string NotSupported = "Rights.NotSupported";
        public const string UserNotFound = "Rights.UserNotFound";
        public const string GroupNotFound = "Rights.GroupNotFound";
        public const string GroupNameRequired = "Rights.GroupNameRequired";
        public const string GroupNameTooLong = "Rights.GroupNameTooLong";
        public const string SubjectNotFound = "Rights.SubjectNotFound";
        public const string RoleUnknown = "Rights.RoleUnknown";
        public const string ScopeNotFound = "Rights.ScopeNotFound";
        public const string ScopeTooWide = "Rights.ScopeTooWide";
        public const string ExpiryRequired = "Rights.ExpiryRequired";
        public const string ExpiryInPast = "Rights.ExpiryInPast";
        public const string AssignmentNotFound = "Rights.AssignmentNotFound";
        public const string StateInvalid = "Rights.StateInvalid";
    }
}
