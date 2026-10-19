namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur de l'espace d'administration (personnes, équipes, rôles sur mesure).</summary>
public static partial class ErrorCodes
{
    public static class Administration
    {
        public const string NameRequired = "Administration.NameRequired";
        public const string NameTooLong = "Administration.NameTooLong";
        public const string EmailInvalid = "Administration.EmailInvalid";
        public const string EmailTaken = "Administration.EmailTaken";
        public const string RoleNameRequired = "Administration.RoleNameRequired";
        public const string RoleNameTooLong = "Administration.RoleNameTooLong";
        public const string RoleDescriptionTooLong = "Administration.RoleDescriptionTooLong";
        public const string RoleEmpty = "Administration.RoleEmpty";
        public const string RoleAreaInvalid = "Administration.RoleAreaInvalid";
        public const string RoleNotFound = "Administration.RoleNotFound";
        public const string RoleInUse = "Administration.RoleInUse";
        public const string SelfLockout = "Administration.SelfLockout";

        /// <summary>La personne s'est déjà connectée : son invitation n'a plus à être renvoyée.</summary>
        public const string NotInvited = "Administration.NotInvited";
        public const string NoAccess = "Administration.NoAccess";
    }
}
