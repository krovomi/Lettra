using System.Globalization;
using Bunit;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Layout;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Tests.Cabinet;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using AdminPage = Lettra.UI.Shared.Pages.Administration.Administration;

namespace Lettra.UI.Tests.Administration;

/// <summary>
/// Captures de l'espace « Équipe et accès » pour build/design-captures.cjs, comme <c>GalleryTests</c> : chaque étape du
/// parcours (liste, fiche, donner un accès, rôle sur mesure, équipes) est rendue dans la coque puis écrite en HTML
/// statique, en clair et en sombre. Ne produit rien sans <c>LETTRA_GALLERY_DIR</c>.
/// </summary>
[Collection("culture")]
public sealed class AdministrationGalleryTests : BunitContext
{
    private static readonly string[] Themes = ["light", "dark"];

    public AdministrationGalleryTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddCabinetFakes();
        Services.AddSingleton<IAdministrationGateway>(new FakeAdministrationGateway());
        Services.AddSingleton<IDossierSelection>(new FixedDossierSelection(FakeAdministrationGateway.Boulangerie));
        Services.AddSingleton<ISessionGateway>(new FakeSessionGateway(new SessionResponse(true, SessionModeDto.Local, "Camille Démo", true)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    [Fact]
    public void Galerie_de_l_administration_pour_les_captures()
    {
        string? dossier = Environment.GetEnvironmentVariable("LETTRA_GALLERY_DIR");
        if (string.IsNullOrEmpty(dossier))
        {
            return;
        }

        Directory.CreateDirectory(dossier);
        Ecrire(dossier, "admin-personnes");
        Ecrire(dossier, "admin-fiche", c => c.Find($"#person-{FakeAdministrationGateway.Hugo}").Click());
        Ecrire(dossier, "admin-donner-acces", c =>
        {
            c.Find($"#person-{FakeAdministrationGateway.Nadia}").Click();
            c.Find("#person-grant").Click();
            c.FindAll(".la-role-choice input")[3].Change("collaborateur");
        });
        Ecrire(dossier, "admin-equipes", c => c.Find("#tab-teams").Click());
        Ecrire(dossier, "admin-equipe", c =>
        {
            c.Find("#tab-teams").Click();
            c.Find($"#team-{FakeAdministrationGateway.TeamTpe}").Click();
        });
        Ecrire(dossier, "admin-roles", c => c.Find("#tab-roles").Click());
        Ecrire(dossier, "admin-role-sur-mesure", c =>
        {
            c.Find("#tab-roles").Click();
            c.Find("#role-new").Click();
            c.Find("#role-name").Input("Assistant facturation");
            c.Find("#role-quotes-work").Click();
            c.Find("#role-invoices-work").Click();
            c.Find("#role-parties-view").Click();
        });
    }

    private void Ecrire(string dossier, string nom, Action<IRenderedComponent<MainLayout>>? gestes = null)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("admin");
        RenderFragment page = b =>
        {
            b.OpenComponent<AdminPage>(0);
            b.CloseComponent();
        };
        IRenderedComponent<MainLayout> coque = Render<MainLayout>(p => p.Add(l => l.Body, page));
        coque.WaitForState(() => coque.FindAll(".la-cards").Count > 0, TimeSpan.FromSeconds(5));
        gestes?.Invoke(coque);

        foreach (string theme in Themes)
        {
            File.WriteAllText(Path.Combine(dossier, $"{nom}.{theme}.html"), $"""
                <!DOCTYPE html>
                <html lang="fr" data-theme="{theme}">
                <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover" />
                <title>{nom} · {theme}</title>
                <link rel="stylesheet" href="css/lettra.css" />
                </head>
                <body>
                {coque.Markup}
                </body>
                </html>
                """);
        }
    }
}
