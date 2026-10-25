namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : espace client (liens envoyés au client, paiement en ligne, signature des devis).</summary>
public static partial class ErrorCodes
{
    public static class Portal
    {
        /// <summary>L'espace client n'est pas ouvert sur cette installation (pas de compte de service, ou poste isolé).</summary>
        public const string Disabled = "Portal.Disabled";

        /// <summary>Lien inconnu, expiré ou révoqué.</summary>
        public const string LinkNotFound = "Portal.LinkNotFound";

        /// <summary>Le lien est déjà révoqué.</summary>
        public const string LinkRevoked = "Portal.LinkRevoked";

        /// <summary>La durée de validité d'un lien va de 1 à 90 jours.</summary>
        public const string ValidityInvalid = "Portal.ValidityInvalid";

        /// <summary>L'acompte demandé à la signature va de 0,01 à 100 %, et seulement sur un lien de devis.</summary>
        public const string DownPaymentInvalid = "Portal.DownPaymentInvalid";

        /// <summary>Le devis n'est pas validé, n'est plus valable, ou n'est pas celui de ce client.</summary>
        public const string QuoteNotSignable = "Portal.QuoteNotSignable";

        /// <summary>Le nom du signataire va de 2 à 120 caractères.</summary>
        public const string SignerNameInvalid = "Portal.SignerNameInvalid";

        /// <summary>Le signataire doit cocher son accord.</summary>
        public const string ConsentRequired = "Portal.ConsentRequired";

        /// <summary>Le devis est déjà signé.</summary>
        public const string AlreadySigned = "Portal.AlreadySigned";

        /// <summary>La facture est soldée, ou n'est pas celle de ce client.</summary>
        public const string NothingToPay = "Portal.NothingToPay";

        /// <summary>Le prestataire de paiement a refusé le paiement.</summary>
        public const string PaymentDeclined = "Portal.PaymentDeclined";

        /// <summary>Paiement en ligne inconnu ou déjà traité.</summary>
        public const string CheckoutNotFound = "Portal.CheckoutNotFound";

        /// <summary>Le devis n'a pas été signé en ligne.</summary>
        public const string SignatureNotFound = "Portal.SignatureNotFound";
    }
}
