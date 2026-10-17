namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : application installée reliée au serveur commun du cabinet.</summary>
public static partial class ErrorCodes
{
    public static class SharedServer
    {
        /// <summary>L'adresse saisie n'est pas une adresse de serveur (vide, espaces, protocole autre que http ou https).</summary>
        public const string AddressInvalid = "SharedServer.AddressInvalid";

        /// <summary>
        /// Adresse en http hors de cet appareil : la connexion passerait en clair sur le réseau. Seul localhost (le serveur
        /// de démonstration sur le même ordinateur, ou relayé par câble USB) est accepté en http.
        /// </summary>
        public const string HttpsRequired = "SharedServer.HttpsRequired";

        /// <summary>Aucun serveur ne répond à cette adresse.</summary>
        public const string Unreachable = "SharedServer.Unreachable";

        /// <summary>Le serveur répond, mais son certificat n'est pas approuvé par l'appareil (certificat de démonstration non installé).</summary>
        public const string CertificateUntrusted = "SharedServer.CertificateUntrusted";

        /// <summary>Un serveur répond à cette adresse, mais ce n'est pas un serveur Lettra.</summary>
        public const string NotLettra = "SharedServer.NotLettra";

        /// <summary>Connecté, mais l'utilisateur n'a accès à aucun dossier sur ce serveur.</summary>
        public const string NoDossier = "SharedServer.NoDossier";
    }
}
