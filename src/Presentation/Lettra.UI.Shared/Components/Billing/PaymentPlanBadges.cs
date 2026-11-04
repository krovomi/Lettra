using Lettra.Contracts.Reminders;

namespace Lettra.UI.Shared.Components.Billing;

/// <summary>La couleur du statut d'un échéancier, la même sur l'écran Relances et sur la facture.</summary>
internal static class PaymentPlanBadges
{
    public static string StatusClass(PaymentPlanStatusDto status) => status switch
    {
        PaymentPlanStatusDto.OnTrack => "is-validated",
        PaymentPlanStatusDto.Completed => "is-accepted",
        PaymentPlanStatusDto.Late or PaymentPlanStatusDto.Lapsed => "is-refused",
        _ => "is-expired",
    };
}
