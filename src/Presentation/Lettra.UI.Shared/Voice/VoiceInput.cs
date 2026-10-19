using System.Globalization;
using Lettra.Contracts.Common;
using Lettra.Contracts.Speech;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace Lettra.UI.Shared.Voice;

/// <summary>Module JavaScript de l'enregistreur (micro → WAV 16 kHz) et de la touche F8.</summary>
public static class VoiceModule
{
    public const string Path = "./_content/Lettra.UI.Shared/lettra-voice.js";
}

/// <summary>Ce que la voix peut faire ici et maintenant.</summary>
public enum VoiceAvailability
{
    /// <summary>Pas encore demandé au serveur.</summary>
    Unknown,

    /// <summary>Un moteur Whisper répond et le navigateur sait enregistrer.</summary>
    Ready,

    /// <summary>Le déploiement n'a pas de moteur (réglage par défaut) : la voix est expliquée, pas proposée.</summary>
    NotConfigured,

    /// <summary>Le serveur ne répond pas (hors ligne, API arrêtée).</summary>
    Unreachable,

    /// <summary>Ce navigateur ou cette WebView ne donne pas accès au micro.</summary>
    Unsupported,

    /// <summary>
    /// Moteur configuré mais pas encore prêt : au premier démarrage, le serveur Whisper télécharge puis charge son modèle.
    /// L'état est redemandé tout seul jusqu'à ce qu'il le soit.
    /// </summary>
    Starting,

    /// <summary>Page ouverte en <c>http://</c> sur une autre adresse que localhost : le navigateur refuse le micro.</summary>
    InsecureContext,
}

public enum VoicePhase
{
    Idle,
    Listening,
    Transcribing,
}

/// <summary>
/// Fin d'un enregistrement. <see cref="Owner"/> est l'id du champ dicté, ou <see cref="VoiceInput.CommandOwner"/>
/// pour une commande ; <see cref="Error"/> est le message à afficher quand rien n'a été compris.
/// </summary>
public sealed record VoiceOutcome(string Owner, SpeechMode Mode, string? Text, VoiceCommand? Command, string? Error);

/// <summary>
/// La voix dans l'interface (vague 5, lane F) : état du moteur, choix de l'utilisateur (micros affichés ou non),
/// enregistrement et transcription. Le texte dicté est inséré dans son champ par le service lui-même ; une commande
/// est remise à <see cref="Completed"/>, que l'hôte vocal affiche avant de l'exécuter. Un seul enregistrement à la fois.
/// </summary>
public sealed class VoiceInput(IServiceProvider services, DevicePreferences preferences, IJSRuntime js, IStringLocalizer<SharedResource> localizer) : IDisposable
{
    /// <summary>Propriétaire d'un enregistrement destiné aux commandes (bouton de la barre du haut, F8 hors d'un champ).</summary>
    public const string CommandOwner = "voice-command";

    public const string PreferenceKey = "lettra.voice";

    private IJSObjectReference? _module;
    private DotNetObjectReference<VoiceInput>? _self;
    private bool _preferenceLoaded;
    private Task? _loading;
    private CancellationTokenSource? _waitForEngine;

    public event EventHandler? Changed;

    /// <summary>Un enregistrement est fini (texte inséré, commande comprise ou erreur).</summary>
    public event EventHandler<VoiceOutcome>? Completed;

    public VoiceAvailability Availability { get; private set; }

    public SpeechStatusResponse Status { get; private set; } = SpeechStatusResponse.Disabled;

    /// <summary>Choix de l'utilisateur sur cet appareil : afficher les micros dans les champs (oui par défaut).</summary>
    public bool Enabled { get; private set; } = true;

    public VoicePhase Phase { get; private set; }

    /// <summary>Champ ou commande en cours d'enregistrement.</summary>
    public string? Owner { get; private set; }

    public SpeechMode Mode { get; private set; }

    /// <summary>Niveau sonore de 0 à 1, pour l'onde affichée pendant l'écoute.</summary>
    public double Level { get; private set; }

    /// <summary>Dernier résultat, gardé pour l'affichage (texte entendu, commande ou erreur).</summary>
    public VoiceOutcome? Last { get; private set; }

    /// <summary>Panneau « Dictée et commandes vocales » ouvert (bouton micro de la barre du haut).</summary>
    public bool PanelOpen { get; private set; }

    public void OpenPanel()
    {
        PanelOpen = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ClosePanel()
    {
        PanelOpen = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePanel()
    {
        if (PanelOpen)
        {
            ClosePanel();
        }
        else
        {
            OpenPanel();
        }
    }

    public bool IsReady => Availability == VoiceAvailability.Ready;

    /// <summary>Les micros des champs ne s'affichent que si la voix marche et que l'utilisateur la veut.</summary>
    public bool FieldMicsVisible => IsReady && Enabled;

    public bool IsBusy => Phase != VoicePhase.Idle;

    /// <summary>Délai entre deux demandes d'état tant que le moteur se prépare.</summary>
    public TimeSpan StartingRetryDelay { get; set; } = TimeSpan.FromSeconds(15);

    public int MaxSeconds => Status.MaxSeconds > 0 ? Status.MaxSeconds : 30;

    /// <summary>Langue de la dictée : celle de l'interface si le moteur la propose, sinon la première qu'il propose.</summary>
    public string Language
    {
        get
        {
            string ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return Status.Languages.Contains(ui, StringComparer.OrdinalIgnoreCase) ? ui : Status.Languages.Count > 0 ? Status.Languages[0] : ui;
        }
    }

    /// <summary>Demande l'état au serveur une fois (ou à nouveau avec <paramref name="refresh"/>).</summary>
    public Task LoadAsync(bool refresh = false)
    {
        if (refresh || _loading is null || _loading.IsFaulted)
        {
            _loading = LoadCoreAsync();
        }

        return _loading;
    }

    private async Task LoadCoreAsync()
    {
        if (!_preferenceLoaded)
        {
            _preferenceLoaded = true;
            Enabled = await preferences.ReadAsync(PreferenceKey) != "off";
        }

        ISpeechGateway? gateway = services.GetService<ISpeechGateway>();
        OperationResult<SpeechStatusResponse> status = gateway is null
            ? OperationResult<SpeechStatusResponse>.Success(SpeechStatusResponse.Disabled)
            : await gateway.GetStatusAsync(CancellationToken.None);
        Status = status.Value ?? SpeechStatusResponse.Disabled;
        if (!status.IsSuccess)
        {
            Availability = VoiceAvailability.Unreachable;
        }
        else if (!Status.Enabled)
        {
            Availability = VoiceAvailability.NotConfigured;
        }
        else if (!await SupportedAsync())
        {
            Availability = await SecureContextAsync() ? VoiceAvailability.Unsupported : VoiceAvailability.InsecureContext;
        }
        else
        {
            Availability = Status.Ready ? VoiceAvailability.Ready : VoiceAvailability.Starting;
        }

        if (Availability == VoiceAvailability.Starting)
        {
            WaitForEngine();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Redemande l'état plus tard, une seule attente à la fois, jusqu'à ce que le moteur soit prêt.</summary>
    private void WaitForEngine()
    {
        if (_waitForEngine is not null)
        {
            return;
        }

        CancellationTokenSource wait = new();
        _waitForEngine = wait;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(StartingRetryDelay, wait.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _waitForEngine = null;
            wait.Dispose();
            await LoadAsync(refresh: true);
        });
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        Enabled = enabled;
        _preferenceLoaded = true;
        await preferences.WriteAsync(PreferenceKey, enabled ? "on" : "off");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Démarre l'écoute. Rend le message à afficher si le micro n'est pas accessible, sinon <c>null</c>.</summary>
    public async Task<string?> StartAsync(string owner, SpeechMode mode)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        if (!IsReady)
        {
            return Unavailable();
        }

        if (IsBusy)
        {
            await CancelAsync();
        }

        IJSObjectReference? module = await ModuleAsync();
        if (module is null)
        {
            return localizer["Voice.Error.Unsupported"];
        }

        _self ??= DotNetObjectReference.Create(this);
        string result;
        try
        {
            result = await module.InvokeAsync<string>("start", _self, MaxSeconds);
        }
        catch (JSException)
        {
            result = "error";
        }

        if (result != "ok")
        {
            string message = localizer[result switch
            {
                "denied" => "Voice.Error.Denied",
                "no-device" => "Voice.Error.NoDevice",
                "unsupported" => "Voice.Error.Unsupported",
                _ => "Voice.Error.Recorder",
            }];
            Last = new VoiceOutcome(owner, mode, null, null, message);
            Changed?.Invoke(this, EventArgs.Empty);
            return message;
        }

        Owner = owner;
        Mode = mode;
        Phase = VoicePhase.Listening;
        Level = 0;
        Last = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <summary>Arrête l'écoute, transcrit, insère le texte dicté dans son champ et annonce le résultat.</summary>
    public async Task<VoiceOutcome?> StopAsync()
    {
        if (Phase != VoicePhase.Listening || Owner is not { } owner || await ModuleAsync() is not { } module)
        {
            return null;
        }

        SpeechMode mode = Mode;
        Phase = VoicePhase.Transcribing;
        Changed?.Invoke(this, EventArgs.Empty);

        VoiceOutcome outcome;
        byte[]? wav = null;
        try
        {
            wav = await module.InvokeAsync<byte[]?>("stop");
        }
        catch (JSException)
        {
        }

        if (wav is not { Length: > 44 })
        {
            outcome = new(owner, mode, null, null, localizer.Error(ErrorCodes.Speech.NothingHeard));
        }
        else
        {
            ISpeechGateway? gateway = services.GetService<ISpeechGateway>();
            OperationResult<TranscriptionResponse> result = gateway is null
                ? OperationResult<TranscriptionResponse>.Failure(ErrorCodes.Speech.Disabled)
                : await gateway.TranscribeAsync(wav, mode, Language, CancellationToken.None);
            if (result.Value is { } transcription && !string.IsNullOrWhiteSpace(transcription.Text))
            {
                outcome = new(owner, mode, transcription.Text, transcription.Command, null);
            }
            else
            {
                string code = result.Errors.Count > 0 ? result.Errors[0].Code : ErrorCodes.Speech.NothingHeard;
                outcome = new(owner, mode, null, null, localizer.Error(code));
                if (code is ErrorCodes.Speech.EngineUnavailable or ErrorCodes.Speech.ModelUnavailable)
                {
                    // Le moteur a pu redémarrer ou être en train de charger son modèle : l'état affiché le dira.
                    _ = LoadAsync(refresh: true);
                }
            }
        }

        if (outcome is { Mode: SpeechMode.Dictation, Text: { } text } && owner != CommandOwner)
        {
            bool inserted = false;
            try
            {
                inserted = await module.InvokeAsync<bool>("insertText", owner, text);
            }
            catch (JSException)
            {
            }

            if (!inserted)
            {
                outcome = outcome with { Error = localizer["Voice.Error.FieldGone"] };
            }
        }

        Phase = VoicePhase.Idle;
        Owner = null;
        Level = 0;
        Last = outcome;
        Changed?.Invoke(this, EventArgs.Empty);
        Completed?.Invoke(this, outcome);
        return outcome;
    }

    public async Task CancelAsync()
    {
        if (Phase == VoicePhase.Idle)
        {
            return;
        }

        if (await ModuleAsync() is { } module)
        {
            try
            {
                await module.InvokeVoidAsync("cancel");
            }
            catch (JSException)
            {
            }
        }

        Phase = VoicePhase.Idle;
        Owner = null;
        Level = 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Le champ qui a le focus, s'il accepte du texte (pour F8).</summary>
    public async Task<string?> FocusedFieldAsync()
    {
        try
        {
            return await ModuleAsync() is { } module ? await module.InvokeAsync<string?>("focusedField") : null;
        }
        catch (JSException)
        {
            return null;
        }
    }

    /// <summary>Message clair quand la voix ne peut pas servir.</summary>
    public string Unavailable() => localizer[Availability switch
    {
        VoiceAvailability.NotConfigured => "Voice.State.NotConfigured",
        VoiceAvailability.Unreachable => "Voice.State.Unreachable",
        VoiceAvailability.Unsupported => "Voice.Error.Unsupported",
        VoiceAvailability.Starting => "Voice.State.Starting",
        VoiceAvailability.InsecureContext => "Voice.Error.InsecureContext",
        _ => "Voice.State.Unknown",
    }];

    [JSInvokable]
    public void OnLevel(double level)
    {
        if (Phase == VoicePhase.Listening)
        {
            Level = Math.Clamp(level, 0, 1);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Durée maximale atteinte : on transcrit ce qui a été dit.</summary>
    [JSInvokable]
    public Task OnTimeLimit() => StopAsync();

    internal async Task<IJSObjectReference?> ModuleAsync()
    {
        try
        {
            return _module ??= await js.InvokeAsync<IJSObjectReference?>("import", VoiceModule.Path);
        }
        catch (JSException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<bool> SupportedAsync()
    {
        try
        {
            return await ModuleAsync() is { } module && await module.InvokeAsync<bool>("supported");
        }
        catch (JSException)
        {
            return false;
        }
    }

    private async Task<bool> SecureContextAsync()
    {
        try
        {
            return await ModuleAsync() is not { } module || await module.InvokeAsync<bool>("secureContext");
        }
        catch (JSException)
        {
            return true;
        }
    }

    /// <summary>Le module JavaScript est libéré avec la page ; l'hôte vocal coupe le micro en partant.</summary>
    public void Dispose()
    {
        _waitForEngine?.Cancel();
        _self?.Dispose();
    }
}
