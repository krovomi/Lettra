using Bunit;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Feedback;
using Lettra.UI.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace Lettra.UI.Tests.Design;

/// <summary>Bloc repliable des écrans de saisie (vague 5, lane E) : bouton de titre, résumé replié, état retenu par bloc.</summary>
[Collection("culture")]
public sealed class CollapsibleTests : BunitContext
{
    private readonly BunitJSModuleInterop _module;

    public CollapsibleTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _module = JSInterop.SetupModule(UiModule.Path);
        Services.AddLettraUi();
    }

    private IRenderedComponent<Collapsible> Bloc(bool initiallyOpen = true) => Render<Collapsible>(p => p
        .Add(c => c.Id, "devis-notes")
        .Add(c => c.Title, "Notes")
        .Add(c => c.Summary, "Aucune note")
        .Add(c => c.InitiallyOpen, initiallyOpen)
        .Add(c => c.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<textarea id=\"notes\"></textarea>"))));

    [Fact]
    public void Le_titre_replie_le_bloc_montre_son_resume_et_retient_le_choix()
    {
        IRenderedComponent<Collapsible> bloc = Bloc();
        bloc.Find("#devis-notes-toggle").GetAttribute("aria-expanded").Should().Be("true");
        bloc.Find("#devis-notes-body").HasAttribute("hidden").Should().BeFalse();
        bloc.FindAll(".lt-collapsible-summary").Should().BeEmpty("le résumé ne double pas un contenu visible");

        bloc.Find("#devis-notes-toggle").Click();

        bloc.Find("#devis-notes-toggle").GetAttribute("aria-expanded").Should().Be("false");
        bloc.Find("#devis-notes-body").HasAttribute("hidden").Should().BeTrue();
        bloc.Find("#notes").Should().NotBeNull("replié, le contenu reste rendu et garde sa saisie");
        bloc.Find(".lt-collapsible-summary").TextContent.Should().Be("Aucune note");
        bloc.Find("section").GetAttribute("aria-labelledby").Should().Be("devis-notes-title");
        _module.Invocations.Should().Contain(i => i.Identifier == "writePreference"
            && Equals(i.Arguments[0], "lettra.bloc.devis-notes") && Equals(i.Arguments[1], "false"));
    }

    [Fact]
    public void Un_bloc_rouvre_dans_l_etat_retenu_sur_l_appareil()
    {
        _module.Setup<string?>("readPreference", "lettra.bloc.devis-notes").SetResult("true");

        IRenderedComponent<Collapsible> bloc = Bloc(initiallyOpen: false);

        bloc.WaitForAssertion(() => bloc.Find("#devis-notes-toggle").GetAttribute("aria-expanded").Should().Be("true"));
    }

    [Fact]
    public async Task Deplier_un_bloc_replie_avant_d_y_placer_le_curseur()
    {
        IRenderedComponent<Collapsible> bloc = Bloc(initiallyOpen: false);
        bloc.Instance.IsOpen.Should().BeFalse();

        await bloc.InvokeAsync(bloc.Instance.ExpandAsync);

        bloc.Find("#devis-notes-body").HasAttribute("hidden").Should().BeFalse();
    }
}
