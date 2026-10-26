namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : banques connectées (agrégateur DSP2, consentement, synchronisation).</summary>
public static partial class ErrorCodes
{
    public static class BankConnection
    {
        /// <summary>Aucun agrégateur bancaire n'est configuré sur cette installation.</summary>
        public const string Unavailable = "BankConnection.Unavailable";

        /// <summary>La banque choisie n'est pas proposée par l'agrégateur.</summary>
        public const string InstitutionUnknown = "BankConnection.InstitutionUnknown";

        /// <summary>Compte relié inconnu dans ce dossier.</summary>
        public const string NotFound = "BankConnection.NotFound";

        /// <summary>Le consentement n'est pas encore donné, ou le compte est déconnecté.</summary>
        public const string NotActive = "BankConnection.NotActive";

        /// <summary>Le consentement de cette connexion a déjà été enregistré.</summary>
        public const string AlreadyCompleted = "BankConnection.AlreadyCompleted";

        /// <summary>Le consentement a été refusé ou abandonné sur la page de la banque.</summary>
        public const string ConsentRefused = "BankConnection.ConsentRefused";

        /// <summary>Le consentement est échu : il faut le renouveler auprès de la banque.</summary>
        public const string ConsentExpired = "BankConnection.ConsentExpired";

        /// <summary>Une synchronisation de ce compte est déjà en cours.</summary>
        public const string SyncInProgress = "BankConnection.SyncInProgress";

        /// <summary>La banque ou l'agrégateur n'a pas répondu.</summary>
        public const string BankUnreachable = "BankConnection.BankUnreachable";
    }
}
