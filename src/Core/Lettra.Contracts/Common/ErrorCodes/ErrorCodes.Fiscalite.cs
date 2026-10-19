namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : déclarations fiscales (brouillons de TVA, DES, calendrier fiscal).</summary>
public static partial class ErrorCodes
{
    public static class Tax
    {
        /// <summary>Fiche entreprise absente : le régime de TVA est inconnu.</summary>
        public const string ProfileMissing = "Tax.ProfileMissing";

        /// <summary>Franchise en base : aucune déclaration de TVA.</summary>
        public const string VatFranchise = "Tax.VatFranchise";

        /// <summary>Régime de déclaration de la TVA non renseigné dans la fiche entreprise.</summary>
        public const string RegimeMissing = "Tax.RegimeMissing";

        /// <summary>Période qui ne correspond pas à une période de déclaration (mois, trimestre ou exercice).</summary>
        public const string PeriodInvalid = "Tax.PeriodInvalid";

        public const string FiscalYearEndInvalid = "Tax.FiscalYearEndInvalid";
        public const string DeadlineDayInvalid = "Tax.DeadlineDayInvalid";

        /// <summary>Obligation ou période inconnue du calendrier du dossier.</summary>
        public const string DeadlineUnknown = "Tax.DeadlineUnknown";
    }
}
