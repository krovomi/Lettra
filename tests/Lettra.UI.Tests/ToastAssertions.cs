using Bunit;
using Lettra.UI.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests;

/// <summary>Les messages du toaster (vague 5) : résultats d'action et erreurs sans champ ne sont plus dans la page.</summary>
internal static class ToastAssertions
{
    public static IReadOnlyList<Toast> Toasts(this BunitContext context) => context.Services.GetRequiredService<ToastService>().Visible;

    /// <summary>Le dernier message affiché, ou une chaîne vide.</summary>
    public static string ToastText(this BunitContext context) => context.Toasts() is { Count: > 0 } toasts ? toasts[^1].Message : string.Empty;
}
