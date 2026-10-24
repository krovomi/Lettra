namespace Lettra.UI.Shared.Services;

public enum ToastKind
{
    Success,
    Info,
    Warning,
    Error,
}

/// <summary>Une notification brève affichée en bas de l'écran, avec au plus une action (« Annuler » un geste qui se défait).</summary>
public sealed record Toast(int Id, ToastKind Kind, string Message, ToastAction? Action = null);

/// <summary>Le bouton d'un message : son libellé et ce qu'il fait. Le message se ferme dès qu'on le presse.</summary>
public sealed record ToastAction(string Label, Func<Task> Run);

/// <summary>
/// Notifications brèves (vague 5, lane E) : le résultat d'une action qui ne reste pas à l'écran (données de démonstration
/// chargées, pièce envoyée). Une réussite ou une information disparaît d'elle-même ; un avertissement ou une erreur reste
/// jusqu'à ce qu'on le ferme ; un message qui porte une action reste un peu plus longtemps, le temps de la presser. Trois
/// au plus à la fois.
/// </summary>
public sealed class ToastService(TimeProvider? clock = null)
{
    public const int MaxVisible = 3;

    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);

    public static readonly TimeSpan ActionLifetime = TimeSpan.FromSeconds(10);

    private readonly List<Toast> _toasts = [];
    private int _nextId;

    public IReadOnlyList<Toast> Visible => _toasts;

    public event EventHandler? Changed;

    public Toast Show(ToastKind kind, string message, ToastAction? action = null)
    {
        // Le même message déjà affiché (nouvel essai qui échoue pareil) n'est pas empilé une seconde fois ; un geste à
        // défaire l'est toujours, chacun annulant le sien.
        if (action is null && _toasts.Find(t => t.Kind == kind && t.Action is null && string.Equals(t.Message, message, StringComparison.Ordinal)) is { } shown)
        {
            return shown;
        }

        Toast toast = new(Interlocked.Increment(ref _nextId), kind, message, action);
        _toasts.Add(toast);
        if (_toasts.Count > MaxVisible)
        {
            _toasts.RemoveAt(0);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        if (kind is ToastKind.Success or ToastKind.Info)
        {
            _ = DismissLaterAsync(toast.Id, action is null ? Lifetime : ActionLifetime);
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

    /// <summary>Ferme le message puis lance son action.</summary>
    public async Task RunActionAsync(Toast toast)
    {
        ArgumentNullException.ThrowIfNull(toast);
        Dismiss(toast.Id);
        if (toast.Action is { } action)
        {
            await action.Run().ConfigureAwait(true);
        }
    }

    private async Task DismissLaterAsync(int id, TimeSpan lifetime)
    {
        await Task.Delay(lifetime, clock ?? TimeProvider.System).ConfigureAwait(false);
        Dismiss(id);
    }
}
