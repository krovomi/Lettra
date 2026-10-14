using System.Globalization;
using Lettra.Poc.Grille.Ui;
using Lettra.Poc.Grille.Wasm;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// Français par défaut (formats 1 234,56 et 14/10/2026).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddLettraGrilleClavier();

await builder.Build().RunAsync();
