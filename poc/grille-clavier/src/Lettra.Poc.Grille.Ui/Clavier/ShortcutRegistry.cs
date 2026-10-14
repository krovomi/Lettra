namespace Lettra.Poc.Grille.Ui.Clavier;

/// <summary>Cellule active au moment d'une commande (ligne, clé de colonne). Null hors grille.</summary>
public sealed record CommandContext(int Row, string Column)
{
    public static readonly CommandContext None = new(-1, "");
}

public sealed record ShortcutBinding(
    KeyChord Chord,
    string Label,
    Func<CommandContext, Task> Execute,
    Func<CommandContext, bool>? CanExecute = null,
    bool ShowInBar = true);

/// <summary>Portée de raccourcis : « global », « page », « grille ». La plus récente gagne.</summary>
public sealed class KeyboardScope(string name)
{
    private readonly Dictionary<KeyChord, ShortcutBinding> _bindings = new();

    public string Name { get; } = name;
    public IReadOnlyDictionary<KeyChord, ShortcutBinding> Bindings => _bindings;

    public KeyboardScope Bind(string chord, string label, Func<CommandContext, Task> execute,
        Func<CommandContext, bool>? canExecute = null, bool showInBar = true)
    {
        var parsed = KeyChord.Parse(chord);
        _bindings[parsed] = new ShortcutBinding(parsed, label, execute, canExecute, showInBar);
        return this;
    }

    public KeyboardScope Bind(string chord, string label, Action<CommandContext> execute,
        Func<CommandContext, bool>? canExecute = null, bool showInBar = true) =>
        Bind(chord, label, ctx => { execute(ctx); return Task.CompletedTask; }, canExecute, showInBar);
}

/// <summary>
/// Registre de raccourcis scopé (annexe A §4.2). Alimente la FunctionKeyBar et reçoit les seules
/// touches de commande que le module JS remonte ; la navigation pure ne passe jamais par ici.
/// </summary>
public sealed class ShortcutRegistry
{
    private readonly List<KeyboardScope> _scopes = new();   // du plus global au plus spécifique

    public event Action? Changed;

    public IDisposable PushScope(KeyboardScope scope)
    {
        _scopes.Add(scope);
        Changed?.Invoke();
        return new PopOnDispose(this, scope);
    }

    /// <summary>Exécute la commande du scope le plus spécifique qui la connaît et peut l'exécuter.</summary>
    public async Task<bool> TryHandleAsync(KeyChord chord, CommandContext context)
    {
        for (var i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].Bindings.TryGetValue(chord, out var binding)
                && (binding.CanExecute?.Invoke(context) ?? true))
            {
                await binding.Execute(context);
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

    /// <summary>Touches à confier au module JS d'un scope donné (sérialisées au format KeyChord.ToString()).</summary>
    public static string[] KeysOf(KeyboardScope scope) => scope.Bindings.Keys.Select(k => k.ToString()).ToArray();

    private static (int, string) SortKey(KeyChord chord) =>
        chord.Key.Length > 1 && chord.Key[0] == 'F' && int.TryParse(chord.Key[1..], out var n)
            ? (n, chord.ToString())
            : (100, chord.ToString());

    private sealed class PopOnDispose(ShortcutRegistry registry, KeyboardScope scope) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            registry._scopes.Remove(scope);
            registry.Changed?.Invoke();
        }
    }
}
