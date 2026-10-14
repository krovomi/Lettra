namespace Lettra.UI.Shared.Keyboard;

/// <summary>Portée de raccourcis : « global », « page », « grille ». La plus récente gagne.</summary>
public sealed class KeyboardScope(string name)
{
    private readonly Dictionary<KeyChord, ShortcutBinding> _bindings = [];

    public string Name { get; } = name;

    public IReadOnlyDictionary<KeyChord, ShortcutBinding> Bindings => _bindings;

    public KeyboardScope Bind(string chord, string label, Func<CommandContext, Task> execute,
        Func<CommandContext, bool>? canExecute = null, bool showInBar = true)
    {
        KeyChord parsed = KeyChord.Parse(chord);
        _bindings[parsed] = new ShortcutBinding(parsed, label, execute, canExecute, showInBar);
        return this;
    }

    public KeyboardScope Bind(string chord, string label, Action<CommandContext> execute,
        Func<CommandContext, bool>? canExecute = null, bool showInBar = true)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return Bind(chord, label, ctx =>
        {
            execute(ctx);
            return Task.CompletedTask;
        }, canExecute, showInBar);
    }
}
