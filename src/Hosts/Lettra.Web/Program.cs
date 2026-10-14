using System.Globalization;
using Lettra.UI.Abstractions;
using Lettra.UI.Gateway.Http;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Langue du navigateur si elle est prise en charge, sinon le français.
LettraCultures.Apply(LettraCultures.Resolve(CultureInfo.CurrentUICulture.Name));

Guid? dossier = Guid.TryParse(builder.Configuration["Lettra:DossierId"], out Guid id) ? id : null;
builder.Services.AddSingleton<IDossierSelection>(new FixedDossierSelection(dossier));
builder.Services.AddLettraUi();
builder.Services.AddHttpGateways(new Uri(builder.HostEnvironment.BaseAddress));

await builder.Build().RunAsync().ConfigureAwait(false);
