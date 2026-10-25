namespace Lettra.UI.Shared.Demo;

/// <summary>Où en est la démo commentée.</summary>
public enum DemoState
{
    /// <summary>Pas lancée, ou arrêtée.</summary>
    Idle,

    /// <summary>Lettra joue les gestes et laisse le temps de lire.</summary>
    Playing,

    /// <summary>En pause : l'étape reste affichée, rien ne bouge.</summary>
    Paused,

    /// <summary>Un geste n'a pas abouti (élément absent) : l'utilisateur réessaie ou passe l'étape.</summary>
    Blocked,

    /// <summary>Dernière étape lue.</summary>
    Finished,
}

/// <summary>
/// Lecteur de la démo commentée : joue les chapitres de <see cref="DemoScriptCatalog"/> étape par étape. Pour chaque
/// étape, il joue les gestes, montre l'élément, puis laisse le temps de lire la bulle (selon la longueur du texte et
/// la vitesse) avant de passer à la suivante. Pause, reprise, « Suivant » (sans attendre la fin de la lecture),
/// vitesse et arrêt à tout moment. Un geste qui échoue arrête la lecture sur l'étape au lieu de continuer à l'aveugle.
/// </summary>
/// <remarks>
/// Les attentes gardent le contexte de l'appelant (<c>ConfigureAwait(true)</c>) : la lecture est lancée depuis un
/// composant, et la navigation comme les appels JS doivent rester sur le fil du rendu (WebView MAUI).
/// </remarks>
public sealed class DemoPlayer(IDemoDriver driver, TimeProvider clock) : IDisposable
{
    /// <summary>Vitesses proposées : lente, normale, rapide.</summary>
    public static IReadOnlyList<double> Speeds { get; } = [0.75, 1.0, 1.5, 2.0];

    private static readonly TimeSpan MinimumReading = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan MaximumReading = TimeSpan.FromSeconds(16);
    private static readonly TimeSpan PerCharacter = TimeSpan.FromMilliseconds(55);
    private static readonly TimeSpan BetweenActions = TimeSpan.FromMilliseconds(450);

    private IReadOnlyList<DemoChapter> _chapters = DemoScriptCatalog.Chapters;
    private CancellationTokenSource? _run;
    private TaskCompletionSource? _resume;
    private bool _skipReading;
    private bool _retry;
    private DemoValues _values = DemoValues.For(clock);
    private Func<string, int>? _textLength;

    public event EventHandler? Changed;

    public DemoState State { get; private set; } = DemoState.Idle;

    public int ChapterIndex { get; private set; }

    public int StepIndex { get; private set; }

    public double Speed { get; private set; } = 1.0;

    /// <summary>Gestes en cours (sinon : lecture de la bulle).</summary>
    public bool Acting { get; private set; }

    /// <summary>Élément que le dernier geste n'a pas trouvé (état <see cref="DemoState.Blocked"/>).</summary>
    public string? Problem { get; private set; }

    public IReadOnlyList<DemoChapter> Chapters => _chapters;

    public DemoChapter? Chapter => ChapterIndex < _chapters.Count ? _chapters[ChapterIndex] : null;

    public DemoStep? Step => Chapter is { } chapter && StepIndex < chapter.Steps.Count ? chapter.Steps[StepIndex] : null;

    public bool IsActive => State is DemoState.Playing or DemoState.Paused or DemoState.Blocked;

    /// <summary>Valeurs de la lecture en cours (code, SIREN fictif), pour les textes tapés.</summary>
    public DemoValues Values => _values;

    /// <summary>Attente entre deux temps de la démo ; remplaçable dans les tests.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (delay, token) => Task.Delay(delay, clock, token);

    /// <summary>Texte tapé : les valeurs « res:… » sont lues dans les ressources par l'écran qui lance la démo.</summary>
    public Func<string, string> Resolve { get; set; } = value => value;

    /// <summary>Remplace le scénario (tests).</summary>
    public void Use(IReadOnlyList<DemoChapter> chapters)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        _chapters = chapters;
    }

    /// <summary>
    /// Longueur du texte de la bulle d'une étape (clé de ressource → nombre de caractères), pour le temps de lecture.
    /// Sans elle, chaque bulle reste affichée le temps minimum.
    /// </summary>
    public void MeasureWith(Func<string, int> textLength) => _textLength = textLength;

    /// <summary>Temps laissé pour lire une bulle à la vitesse 1 : 55 ms par caractère, entre 4 et 16 secondes.</summary>
    public static TimeSpan ReadingTime(int characters)
    {
        TimeSpan time = PerCharacter * Math.Max(0, characters);
        return time < MinimumReading ? MinimumReading : time > MaximumReading ? MaximumReading : time;
    }

    /// <summary>
    /// Lance la démo depuis le début, ou depuis un chapitre ; une lecture en cours est arrêtée d'abord. La tâche rendue
    /// dure toute la lecture : l'écran ne l'attend pas, les tests si.
    /// </summary>
    public Task StartAsync(int chapterIndex = 0)
    {
        if (chapterIndex < 0 || chapterIndex >= _chapters.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(chapterIndex));
        }

        CancelRun();
        _values = DemoValues.For(clock);
        ChapterIndex = chapterIndex;
        StepIndex = 0;
        Problem = null;
        _skipReading = false;
        _resume = null;
        State = DemoState.Playing;
        CancellationTokenSource run = new();
        _run = run;
        Raise();
        return PlayAsync(run.Token);
    }

    public void Pause()
    {
        if (State == DemoState.Playing)
        {
            State = DemoState.Paused;
            _resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Raise();
        }
    }

    public void Resume()
    {
        if (State == DemoState.Paused)
        {
            State = DemoState.Playing;
            Release();
            Raise();
        }
    }

    /// <summary>« Suivant » : passe à l'étape suivante dès que les gestes en cours sont joués.</summary>
    public void Next()
    {
        if (State is DemoState.Playing or DemoState.Paused)
        {
            _skipReading = true;
            Resume();
        }
    }

    /// <summary>Étape bloquée : rejoue ses gestes.</summary>
    public void Retry()
    {
        if (State == DemoState.Blocked)
        {
            Problem = null;
            State = DemoState.Playing;
            Release(retry: true);
            Raise();
        }
    }

    /// <summary>Étape bloquée : la laisse de côté et continue.</summary>
    public void Skip()
    {
        if (State == DemoState.Blocked)
        {
            Problem = null;
            State = DemoState.Playing;
            Release(retry: false);
            Raise();
        }
    }

    public void SetSpeed(double speed)
    {
        if (speed > 0 && Math.Abs(speed - Speed) > double.Epsilon)
        {
            Speed = speed;
            Raise();
        }
    }

    /// <summary>Arrête la démo ; ce qui a été créé reste dans le dossier, comme une saisie normale.</summary>
    public async Task StopAsync()
    {
        if (State == DemoState.Idle)
        {
            return;
        }

        CancelRun();
        State = DemoState.Idle;
        Problem = null;
        Raise();
        await driver.ClearAsync().ConfigureAwait(true);
    }

    private async Task PlayAsync(CancellationToken token)
    {
        try
        {
            while (Step is { } step && Chapter is { } chapter)
            {
                if (!await RunStepAsync(chapter, step, token).ConfigureAwait(true))
                {
                    continue;   // rejouer la même étape
                }

                Advance();
            }

            State = DemoState.Finished;
            Raise();
            await driver.ClearAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Arrêtée ou relancée : la nouvelle lecture a pris la main.
        }
    }

    /// <summary>Joue une étape ; <c>false</c> quand elle doit être rejouée (bloquée puis « Réessayer »).</summary>
    private async Task<bool> RunStepAsync(DemoChapter chapter, DemoStep step, CancellationToken token)
    {
        _skipReading = false;
        Acting = step.Actions.Count > 0;
        Raise();
        foreach (DemoAction action in step.Actions)
        {
            await WhilePausedAsync(token).ConfigureAwait(true);
            if (!await PerformAsync(action, token).ConfigureAwait(true))
            {
                return await BlockAsync(action.Target, token).ConfigureAwait(true);
            }

            await Delay(Scaled(BetweenActions), token).ConfigureAwait(true);
        }

        bool shown = await driver.HighlightAsync(step.Highlight, token).ConfigureAwait(true);
        Acting = false;
        Raise();
        if (!shown && step.Actions.Count > 0)
        {
            // L'élément à montrer n'est pas là alors que les gestes devaient l'afficher : l'étape n'a pas abouti.
            return await BlockAsync(step.Highlight!, token).ConfigureAwait(true);
        }

        await ReadAsync(chapter, step, token).ConfigureAwait(true);
        return true;
    }

    private async Task<bool> PerformAsync(DemoAction action, CancellationToken token)
    {
        string value = _values.Apply(Resolve(action.Value ?? string.Empty));
        switch (action.Kind)
        {
            case DemoActionKind.Navigate:
                await driver.NavigateAsync(value, token).ConfigureAwait(true);
                return true;
            case DemoActionKind.WaitFor:
                return await driver.WaitForAsync(action.Target, token).ConfigureAwait(true);
            case DemoActionKind.Type:
                return await driver.TypeAsync(action.Target, value, Speed, token).ConfigureAwait(true);
            case DemoActionKind.Click:
                return await driver.ClickAsync(action.Target, Speed, token).ConfigureAwait(true);
            default:
                throw new InvalidOperationException($"Geste inconnu : {action.Kind}.");
        }
    }

    private async Task<bool> BlockAsync(string target, CancellationToken token)
    {
        Acting = false;
        Problem = target;
        State = DemoState.Blocked;
        _retry = false;
        _resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Raise();
        await _resume.Task.WaitAsync(token).ConfigureAwait(true);
        return !_retry;
    }

    // Lecture de la bulle, découpée en courts instants pour suivre la pause, « Suivant » et la vitesse.
    private async Task ReadAsync(DemoChapter chapter, DemoStep step, CancellationToken token)
    {
        int characters = _textLength?.Invoke(chapter.StepTitleKey(step)) + _textLength?.Invoke(chapter.StepTextKey(step)) ?? 0;
        TimeSpan left = ReadingTime(characters);
        TimeSpan tick = TimeSpan.FromMilliseconds(250);
        while (left > TimeSpan.Zero && !_skipReading)
        {
            await WhilePausedAsync(token).ConfigureAwait(true);
            if (_skipReading)
            {
                break;
            }

            await Delay(tick, token).ConfigureAwait(true);
            left -= tick * Speed;
        }
    }

    private async Task WhilePausedAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (State == DemoState.Paused && _resume is { } resume)
        {
            await resume.Task.WaitAsync(token).ConfigureAwait(true);
        }
    }

    private void Advance()
    {
        if (Chapter is { } chapter && StepIndex + 1 < chapter.Steps.Count)
        {
            StepIndex++;
        }
        else
        {
            ChapterIndex++;
            StepIndex = 0;
        }
    }

    private TimeSpan Scaled(TimeSpan delay) => delay / Speed;

    private void Release(bool retry = false)
    {
        _retry = retry;
        TaskCompletionSource? resume = _resume;
        _resume = null;
        resume?.TrySetResult();
    }

    private void CancelRun()
    {
        _run?.Cancel();
        _run?.Dispose();
        _run = null;
        Acting = false;
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose() => CancelRun();
}
