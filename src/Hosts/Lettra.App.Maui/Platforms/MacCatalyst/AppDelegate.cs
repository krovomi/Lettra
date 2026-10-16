using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace Lettra.App.Maui;

/// <summary>Point d'entrée Mac Catalyst (macOS).</summary>
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
