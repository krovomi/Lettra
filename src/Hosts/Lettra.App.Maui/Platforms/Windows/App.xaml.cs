using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace Lettra.App.Maui.WinUI;

/// <summary>Point d'entrée WinUI.</summary>
public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
