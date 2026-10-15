using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace Lettra.App.Maui;

/// <summary>Point d'entrée iOS.</summary>
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
