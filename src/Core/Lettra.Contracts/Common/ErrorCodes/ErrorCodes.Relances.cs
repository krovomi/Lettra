namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : envoi d'e-mails et relances des factures en retard (vague 7).</summary>
public static partial class ErrorCodes
{
    public static class Email
    {
        /// <summary>Aucun serveur d'envoi n'est configuré sur cette installation : rien n'est parti.</summary>
        public const string NotConfigured = "Email.NotConfigured";

        /// <summary>Le serveur d'envoi a refusé le message ou ne répond pas.</summary>
        public const string SendFailed = "Email.SendFailed";
    }

    public static class Reminders
    {
        /// <summary>Les délais des niveaux de relance doivent croître (J+3 &lt; J+15 &lt; J+30 &lt; J+45), entre 1 et 365 jours.</summary>
        public const string DelaysInvalid = "Reminders.DelaysInvalid";

        /// <summary>L'écart minimal entre deux relances d'une facture va de 1 à 90 jours.</summary>
        public const string GapInvalid = "Reminders.GapInvalid";

        /// <summary>La facture n'existe pas, n'est pas validée, ou n'est plus due.</summary>
        public const string InvoiceNotOverdue = "Reminders.InvoiceNotOverdue";

        /// <summary>Le client n'a pas d'adresse e-mail : envoyer la relance à la main, puis la noter.</summary>
        public const string CustomerEmailMissing = "Reminders.CustomerEmailMissing";

        /// <summary>Tous les niveaux de relance de cette facture ont déjà été envoyés.</summary>
        public const string NoLevelLeft = "Reminders.NoLevelLeft";

        /// <summary>La fiche de l'entreprise n'est pas remplie : la relance ne peut pas dire qui la signe.</summary>
        public const string CompanyProfileMissing = "Reminders.CompanyProfileMissing";
    }
}
