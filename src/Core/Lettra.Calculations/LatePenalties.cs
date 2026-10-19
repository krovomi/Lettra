namespace Lettra.Calculations;

/// <summary>Pénalités de retard d'une facture à une date : intérêts au taux de la facture et indemnité forfaitaire.</summary>
/// <param name="Interest">Intérêts de retard, arrondis au centime (0 sans taux sur la facture).</param>
/// <param name="RecoveryIndemnity">Indemnité forfaitaire pour frais de recouvrement (40 €), due par un professionnel seulement.</param>
public readonly record struct LatePenalty(decimal Interest, decimal RecoveryIndemnity)
{
    public decimal Total => Interest + RecoveryIndemnity;
}

/// <summary>
/// Calcul des pénalités de retard citées dans les dernières relances (art. L441-10 du Code de commerce) : intérêts
/// simples au taux annuel figé sur la facture, sur le reste dû, au prorata des jours de retard (base 365), et
/// indemnité forfaitaire de 40 € par facture pour un client professionnel. Lettra les <b>propose</b>, ne les facture
/// jamais d'office. À relire par l'expert-comptable (jalon J2).
/// </summary>
public static class LatePenaltyCalculator
{
    public static LatePenalty Compute(decimal outstanding, decimal? annualRatePercent, int daysOverdue, bool professionalCustomer)
    {
        if (outstanding <= 0m || daysOverdue <= 0)
        {
            return default;
        }

        decimal interest = annualRatePercent is decimal rate && rate > 0m
            ? Math.Round(outstanding * rate / 100m * daysOverdue / 365m, 2, MidpointRounding.AwayFromZero)
            : 0m;
        decimal indemnity = professionalCustomer ? Compliance.MandatoryMentions.RecoveryIndemnityAmount : 0m;
        return new LatePenalty(interest, indemnity);
    }
}
