using System.Globalization;
using Lettra.Contracts.Billing;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Services;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Billing;

/// <summary>Le compte rendu d'une création de factures récurrentes, en toasts (liste et fiche du modèle).</summary>
public static class RecurringToasts
{
    public static void Show(ToastService toasts, IStringLocalizer<SharedResource> localizer, RecurringRunResponse run)
    {
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(run);
        CultureInfo culture = CultureInfo.CurrentCulture;
        if (run.CreatedCount == 0 && run.Failures.Count == 0)
        {
            toasts.Show(ToastKind.Info, localizer["Recurring.Toast.NothingDue"].Value);
            return;
        }

        if (run.CreatedCount > 0)
        {
            toasts.Show(ToastKind.Success, string.Format(culture, localizer["Recurring.Toast.Generated"].Value, run.CreatedCount, run.ValidatedCount));
        }

        int leftAsDraft = run.Created.Count(i => i.ValidationError is not null);
        if (leftAsDraft > 0)
        {
            toasts.Show(ToastKind.Warning, string.Format(culture, localizer["Recurring.Toast.LeftAsDraft"].Value, leftAsDraft));
        }

        foreach (RecurringRunFailure failure in run.Failures)
        {
            toasts.Show(ToastKind.Error, string.Format(culture, localizer["Recurring.Toast.Failed"].Value, failure.Name, localizer["Error." + failure.Code].Value));
        }
    }
}
