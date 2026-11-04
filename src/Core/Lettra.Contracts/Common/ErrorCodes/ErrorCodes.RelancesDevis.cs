namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : devis à relancer et relances de devis.</summary>
public static partial class ErrorCodes
{
    public static class QuoteFollowUps
    {
        /// <summary>La fenêtre des devis à relancer va de 1 à 30 jours avant la fin de validité.</summary>
        public const string WindowInvalid = "QuoteFollowUps.WindowInvalid";

        /// <summary>Le devis n'attend plus de réponse : brouillon, déjà accepté ou refusé, ou expiré.</summary>
        public const string QuoteNotPending = "QuoteFollowUps.QuoteNotPending";

        /// <summary>Le client n'a pas d'adresse e-mail : le relancer autrement, puis noter la relance.</summary>
        public const string CustomerEmailMissing = "QuoteFollowUps.CustomerEmailMissing";

        /// <summary>La fiche de l'entreprise n'est pas remplie : la relance ne peut pas dire qui la signe.</summary>
        public const string CompanyProfileMissing = "QuoteFollowUps.CompanyProfileMissing";

        /// <summary>L'objet (une ligne, 300 caractères au plus) ou le texte (8 000 caractères au plus) est vide ou trop long.</summary>
        public const string MessageInvalid = "QuoteFollowUps.MessageInvalid";
    }
}
