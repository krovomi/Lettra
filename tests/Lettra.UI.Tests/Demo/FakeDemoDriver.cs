using Lettra.UI.Shared.Demo;

namespace Lettra.UI.Tests.Demo;

/// <summary>Mains de la démo pour les tests : note chaque geste, et échoue sur les cibles qu'on lui désigne.</summary>
internal sealed class FakeDemoDriver : IDemoDriver
{
    public List<string> Gestures { get; } = [];

    /// <summary>Cibles introuvables : le geste qui les vise rend <c>false</c> tant qu'elles sont dans l'ensemble.</summary>
    public HashSet<string> Missing { get; } = new(StringComparer.Ordinal);

    /// <summary>Appelé quand une étape montre son élément (fin des gestes) : le test peut y mettre la démo en pause.</summary>
    public Action<string?>? OnHighlight { get; set; }

    public bool Cleared { get; private set; }

    public Task NavigateAsync(string path, CancellationToken cancellationToken)
    {
        Gestures.Add($"go {path}");
        return Task.CompletedTask;
    }

    public Task<bool> WaitForAsync(string target, CancellationToken cancellationToken) => Note($"wait {target}", target);

    public Task<bool> HighlightAsync(string? target, CancellationToken cancellationToken)
    {
        Gestures.Add($"show {target}");
        OnHighlight?.Invoke(target);
        return Task.FromResult(target is null || !Missing.Contains(target));
    }

    public Task<bool> TypeAsync(string target, string text, double speed, CancellationToken cancellationToken) =>
        Note($"type {target}={text}", target);

    public Task<bool> ClickAsync(string target, double speed, CancellationToken cancellationToken) => Note($"click {target}", target);

    public Task ClearAsync()
    {
        Cleared = true;
        return Task.CompletedTask;
    }

    private Task<bool> Note(string gesture, string target)
    {
        Gestures.Add(gesture);
        return Task.FromResult(!Missing.Contains(target));
    }
}
