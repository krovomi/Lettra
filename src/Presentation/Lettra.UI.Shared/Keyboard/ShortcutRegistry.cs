namespace Lettra.UI.Shared.Keyboard;

/// <summary>
/// Registre de raccourcis scopé (annexe A §4.2). Alimente la <c>FunctionKeyBar</c> et reçoit les seules
/// touches de commande que le module JS remonte ; la navigation pure ne passe jamais par ici.
/// </summary>
public sealed class ShortcutRegistry
{
    private readonly List<KeyboardScope> _scopes = [];   // du plus global au plus spécifique

    public event EventHandler? Changed;

    public IDisposable PushScope(KeyboardScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scopes.Add(scope);
        Changed?.Invoke(this, EventArgs.Empty);
        return new PopOnDispose(this, scope);
    }

    /// <summary>Exécute la commande du scope le plus spécifique qui la connaît et peut l'exécuter.</summary>
    public async Task<bool> TryHandleAsync(KeyChord chord, CommandContext context)
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].Bindings.TryGetValue(chord, out ShortcutBinding? binding)
                && (binding.CanExecute?.Invoke(context) ?? true))
            {
                await binding.Execute(context).ConfigureAwait(false);
                return true;
            }
        }

        return false;
    }

    /// <summary>Raccourcis affichés dans la barre F1…F12 : le scope le plus spécifique masque les autres.</summary>
    public IReadOnlyList<ShortcutBinding> VisibleBindings =>
        Enumerable.Range(0, _scopes.Count).Reverse()
            .SelectMany(i => _scopes[i].Bindings.Values)
            .Where(b => b.ShowInBar)
            .DistinctBy(b => b.Chord)
            .OrderBy(b => SortKey(b.Chord))
            .ToList();

    /// <summary>Touches à confier au module JS d'un scope donné (format <see cref="KeyChord.ToString"/>).</summary>
    public static string[] KeysOf(KeyboardScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.Bindings.Keys.Select(k => k.ToString()).ToArray();
    }

    private static (int Order, string Text) SortKey(KeyChord chord) =>
        chord.Key.Length > 1 && chord.Key[0] == 'F' && int.TryParse(chord.Key[1..], System.Globalization.CultureInfo.InvariantCulture, out int n)
            ? (n, chord.ToString())
            : (100, chord.ToString());

    private sealed class PopOnDispose(ShortcutRegistry registry, KeyboardScope scope) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            registry._scopes.Remove(scope);
            registry.Changed?.Invoke(registry, EventArgs.Empty);
        }
    }
}
