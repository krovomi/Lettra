using Lettra.UI.Shared.Services;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Pages.Administration;

/// <summary>Les messages de « Équipe et accès » passent par le toaster partagé : un geste fait, s'il se défait, s'annule depuis son message.</summary>
internal static class AdminToasts
{
    public static void Done(this ToastService toasts, IStringLocalizer l, string text, Func<Task>? undo = null) =>
        toasts.Show(ToastKind.Success, text, undo is null ? null : new ToastAction(l["Admin.Undo"], undo));

    public static void Failed(this ToastService toasts, string text) => toasts.Show(ToastKind.Error, text);
}
