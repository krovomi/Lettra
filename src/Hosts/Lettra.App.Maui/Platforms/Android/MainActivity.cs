using Android.App;
using Android.Content.PM;
using Microsoft.Maui;

namespace Lettra.App.Maui;

/// <summary>Activité Android unique ; nom Java fixe pour la lancer par <c>am start -n fr.lettra.app/.MainActivity</c>.</summary>
[Activity(
    Name = "fr.lettra.app.MainActivity",
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity;
