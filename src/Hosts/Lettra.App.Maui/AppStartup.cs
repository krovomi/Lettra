using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui;

namespace Lettra.App.Maui;

/// <summary>
/// Ouverture des données du poste avant le premier écran : cabinet local, premier dossier (ou réplique du dossier du
/// serveur), dossier ouvert. Elle tourne hors du fil principal, qui affiche pendant ce temps <see cref="StartupPage"/>
/// (sur Android, un fil principal bloqué au démarrage fait fermer l'application). Une ouverture qui échoue est
/// journalisée et peut être relancée.
/// </summary>
public sealed partial class AppStartup(Func<CancellationToken, Task> open, Action afterOpen, ILogger<AppStartup> logger)
{
    private Task<bool>? _opening;
    private bool _completed;

    /// <summary>Rien à ouvrir sur l'appareil (application reliée au serveur commun).</summary>
    public static AppStartup Nothing(ILogger<AppStartup> logger) => new(_ => Task.CompletedTask, () => { }, logger);

    /// <summary>Vrai une fois les données ouvertes ; faux si l'ouverture a échoué (un nouvel appel la relance).</summary>
    public Task<bool> OpenAsync()
    {
        if (_opening is { IsCompletedSuccessfully: true, Result: false })
        {
            _opening = null;
        }

        return _opening ??= Task.Run(RunAsync);
    }

    /// <summary>Sur le fil principal, une seule fois après l'ouverture : déclencheurs de synchronisation d'un appareil relié.</summary>
    public void Complete()
    {
        if (!_completed)
        {
            _completed = true;
            afterOpen();
        }
    }

    private async Task<bool> RunAsync()
    {
        try
        {
            await open(CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogOpenFailed(exception);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Ouverture des données du poste impossible")]
    private partial void LogOpenFailed(Exception exception);
}

/// <summary>Premier écran, le temps d'ouvrir les données du poste ; en cas d'échec, un message et « Réessayer ».</summary>
public sealed class StartupPage : ContentPage
{
    private static readonly bool English = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";

    private readonly AppStartup _startup;
    private readonly ActivityIndicator _spinner = new() { IsRunning = true, HorizontalOptions = LayoutOptions.Center };
    private readonly Label _message = new()
    {
        Text = English ? "Opening your files…" : "Ouverture de vos dossiers…",
        HorizontalTextAlignment = TextAlignment.Center,
        FontSize = 16,
    };

    private readonly Button _retry = new() { Text = English ? "Try again" : "Réessayer", IsVisible = false, HorizontalOptions = LayoutOptions.Center };

    public StartupPage(AppStartup startup)
    {
        _startup = startup;
        _retry.Clicked += async (_, _) => await OpenAsync().ConfigureAwait(true);
        Content = new VerticalStackLayout
        {
            Spacing = 20,
            Padding = new Thickness(24),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = "Lettra", FontSize = 32, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center },
                _spinner,
                _message,
                _retry,
            },
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await OpenAsync().ConfigureAwait(true);
    }

    private async Task OpenAsync()
    {
        _retry.IsVisible = false;
        _spinner.IsRunning = true;
        _message.Text = English ? "Opening your files…" : "Ouverture de vos dossiers…";
        if (await _startup.OpenAsync().ConfigureAwait(true))
        {
            _startup.Complete();
            if (Window is { } window)
            {
                window.Page = new MainPage();
            }

            return;
        }

        _spinner.IsRunning = false;
        _message.Text = English
            ? "Lettra could not open the files on this device. Restart the application, or try again."
            : "Lettra n'a pas pu ouvrir les dossiers de cet appareil. Relancez l'application ou réessayez.";
        _retry.IsVisible = true;
    }
}
