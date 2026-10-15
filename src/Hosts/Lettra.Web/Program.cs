using System.Globalization;
using Lettra.UI.Abstractions;
using Lettra.UI.Gateway.Http;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Drafts;
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
// Brouillons hors ligne (étape 4.5) : saisis sans réseau, gardés dans IndexedDB, envoyés au retour du réseau.
// Navigateur seulement : l'application MAUI garde tout le dossier sur le poste.
builder.Services.AddOfflineDrafts();
builder.Services.AddHttpGateways(new Uri(builder.HostEnvironment.BaseAddress));

await builder.Build().RunAsync().ConfigureAwait(false);
