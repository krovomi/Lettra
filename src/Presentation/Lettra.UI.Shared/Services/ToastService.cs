namespace Lettra.UI.Shared.Services;

public enum ToastKind
{
    Success,
    Info,
    Warning,
    Error,
}

/// <summary>Une notification brève affichée en bas de l'écran.</summary>
public sealed record Toast(int Id, ToastKind Kind, string Message);

/// <summary>
/// Notifications brèves (vague 5, lane E) : le résultat d'une action qui ne reste pas à l'écran (données de démonstration
/// chargées, pièce envoyée). Une réussite ou une information disparaît d'elle-même ; un avertissement ou une erreur reste
/// jusqu'à ce qu'on le ferme. Trois au plus à la fois.
/// </summary>
public sealed class ToastService(TimeProvider? clock = null)
{
    public const int MaxVisible = 3;

    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);

    private readonly List<Toast> _toasts = [];
    private int _nextId;

    public IReadOnlyList<Toast> Visible => _toasts;

    public event EventHandler? Changed;

    public Toast Show(ToastKind kind, string message)
    {
        // Le même message déjà affiché (nouvel essai qui échoue pareil) n'est pas empilé une seconde fois.
        if (_toasts.Find(t => t.Kind == kind && string.Equals(t.Message, message, StringComparison.Ordinal)) is { } shown)
        {
            return shown;
        }

        Toast toast = new(Interlocked.Increment(ref _nextId), kind, message);
        _toasts.Add(toast);
        if (_toasts.Count > MaxVisible)
        {
            _toasts.RemoveAt(0);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        if (kind is ToastKind.Success or ToastKind.Info)
        {
            _ = DismissLaterAsync(toast.Id);
        }

        return toast;
    }

    public void Dismiss(int id)
    {
        if (_toasts.RemoveAll(t => t.Id == id) > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task DismissLaterAsync(int id)
    {
        await Task.Delay(Lifetime, clock ?? TimeProvider.System).ConfigureAwait(false);
        Dismiss(id);
    }
}
