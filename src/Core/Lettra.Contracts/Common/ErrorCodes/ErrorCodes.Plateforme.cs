namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : administration de la plateforme (arrivée et départ des cabinets).</summary>
public static partial class ErrorCodes
{
    public static class CabinetLifecycle
    {
        public const string NameRequired = "CabinetLifecycle.NameRequired";
        public const string NameTooLong = "CabinetLifecycle.NameTooLong";

        /// <summary>Ce serveur n'héberge pas de cabinets en ligne (pas de catalogue, ou pas de fournisseur d'identité réglé).</summary>
        public const string Unavailable = "CabinetLifecycle.Unavailable";

        /// <summary>Le fournisseur d'identité n'a pas répondu ; rien n'est perdu, l'arrivée se relance telle quelle.</summary>
        public const string IdentityFailed = "CabinetLifecycle.IdentityFailed";

        /// <summary>Un autre cabinet porte déjà ce nom chez le fournisseur d'identité.</summary>
        public const string NameTaken = "CabinetLifecycle.NameTaken";

        public const string CellUnknown = "CabinetLifecycle.CellUnknown";
        public const string CabinetDeclared = "CabinetLifecycle.CabinetDeclared";
        public const string DepartureNotCancellable = "CabinetLifecycle.DepartureNotCancellable";
        public const string CabinetLeaving = "CabinetLifecycle.CabinetLeaving";
    }
}
