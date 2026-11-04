namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : avant le retard (rappel avant échéance et échéancier en libre-service).</summary>
public static partial class ErrorCodes
{
    public static partial class Reminders
    {
        /// <summary>Le rappel avant échéance part de 1 à 30 jours avant l'échéance.</summary>
        public const string BeforeDueDaysInvalid = "Reminders.BeforeDueDaysInvalid";

        /// <summary>L'échéancier va de 2 à 3 échéances, pour un reste dû minimal de 0 à 1 000 000 €.</summary>
        public const string PaymentPlanBoundsInvalid = "Reminders.PaymentPlanBoundsInvalid";
    }

    public static class PaymentPlans
    {
        /// <summary>L'entreprise ne propose pas l'échéancier.</summary>
        public const string Disabled = "PaymentPlans.Disabled";

        /// <summary>L'échéancier est réservé aux clients professionnels.</summary>
        public const string NotProfessional = "PaymentPlans.NotProfessional";

        /// <summary>La facture n'est plus due.</summary>
        public const string NothingDue = "PaymentPlans.NothingDue";

        /// <summary>La facture est contestée : elle ne s'étale pas.</summary>
        public const string Disputed = "PaymentPlans.Disputed";

        /// <summary>La facture a déjà un échéancier en cours.</summary>
        public const string AlreadyPlanned = "PaymentPlans.AlreadyPlanned";

        /// <summary>Le reste dû est sous le minimum fixé par l'entreprise.</summary>
        public const string BelowMinimum = "PaymentPlans.BelowMinimum";

        /// <summary>Le plafond légal des délais de paiement est trop proche pour étaler la facture.</summary>
        public const string NoRoom = "PaymentPlans.NoRoom";

        /// <summary>Le nombre d'échéances n'est pas permis.</summary>
        public const string InstallmentsInvalid = "PaymentPlans.InstallmentsInvalid";

        /// <summary>Le nom de la personne qui demande va de 2 à 120 caractères.</summary>
        public const string RequesterInvalid = "PaymentPlans.RequesterInvalid";

        /// <summary>La demande a besoin de l'accord explicite de la personne.</summary>
        public const string ConsentRequired = "PaymentPlans.ConsentRequired";

        /// <summary>L'échéancier n'existe pas dans ce dossier.</summary>
        public const string NotFound = "PaymentPlans.NotFound";

        /// <summary>L'échéancier est déjà arrêté.</summary>
        public const string AlreadyCancelled = "PaymentPlans.AlreadyCancelled";
    }
}
