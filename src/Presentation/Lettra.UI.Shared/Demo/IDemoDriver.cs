namespace Lettra.UI.Shared.Demo;

/// <summary>
/// Les mains de la démo commentée : ouvrir un écran, montrer, taper, cliquer. Dans l'application, ce sont de vrais
/// événements du navigateur (<see cref="JsDemoDriver"/>) ; dans les tests, un double enregistre les gestes.
/// Chaque geste rend <c>false</c> quand l'élément n'est pas apparu à temps : la démo s'arrête alors sur l'étape.
/// </summary>
public interface IDemoDriver
{
    Task NavigateAsync(string path, CancellationToken cancellationToken);

    Task<bool> WaitForAsync(string target, CancellationToken cancellationToken);

    Task<bool> HighlightAsync(string? target, CancellationToken cancellationToken);

    Task<bool> TypeAsync(string target, string text, double speed, CancellationToken cancellationToken);

    Task<bool> ClickAsync(string target, double speed, CancellationToken cancellationToken);

    /// <summary>Fin ou arrêt : plus d'anneau ni de pointeur à l'écran.</summary>
    Task ClearAsync();
}
