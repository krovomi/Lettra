using Bunit;
using Lettra.UI.Shared.Demo;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Lettra.UI.Tests.Demo;

/// <summary>Gestes joués dans le navigateur : appels au module lettra-demo.js, navigation, échec sans exception.</summary>
public sealed class JsDemoDriverTests : BunitContext
{
    private readonly BunitJSModuleInterop _module;
    private readonly JsDemoDriver _driver;

    public JsDemoDriverTests()
    {
        _module = JSInterop.SetupModule(DemoModule.Path);
        _driver = new JsDemoDriver(JSInterop.JSRuntime, Services.GetRequiredService<NavigationManager>());
    }

    [Fact]
    public async Task Chaque_geste_appelle_le_module_avec_le_delai_d_apparition()
    {
        _module.Setup<bool>("type", _ => true).SetResult(true);
        _module.Setup<bool>("click", _ => true).SetResult(true);
        _module.Setup<bool>("highlight", _ => true).SetResult(true);
        _module.Setup<bool>("waitForElement", _ => true).SetResult(false);

        (await _driver.TypeAsync("#name", "Boulangerie", 1.5, CancellationToken.None)).Should().BeTrue();
        (await _driver.ClickAsync("#save", 1, CancellationToken.None)).Should().BeTrue();
        (await _driver.HighlightAsync(null, CancellationToken.None)).Should().BeTrue();
        (await _driver.WaitForAsync("#absent", CancellationToken.None)).Should().BeFalse();

        JSRuntimeInvocation type = _module.Invocations["type"].Single();
        type.Arguments.Should().Equal("#name", "Boulangerie", 1.5, 15_000);
        _module.Invocations["highlight"].Single().Arguments.Should().Equal(null, 15_000);
    }

    [Fact]
    public async Task Une_erreur_du_navigateur_fait_echouer_le_geste_sans_exception()
    {
        _module.Setup<bool>("click", _ => true).SetException(new JSException("élément détaché"));

        (await _driver.ClickAsync("#save", 1, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task Ouvrir_un_ecran_navigue_et_la_fin_efface_l_anneau()
    {
        await _driver.ClearAsync();   // module pas encore chargé : rien à effacer
        _module.SetupVoid("clear").SetVoidResult();
        _module.Setup<bool>("highlight", _ => true).SetResult(true);
        await _driver.HighlightAsync("#x", CancellationToken.None);

        await _driver.NavigateAsync("customers", CancellationToken.None);
        await _driver.ClearAsync();

        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/customers");
        _module.VerifyInvoke("clear");
    }

    [Fact]
    public async Task Effacer_une_page_deja_quittee_n_est_pas_une_erreur()
    {
        _module.Setup<bool>("highlight", _ => true).SetResult(true);
        _module.SetupVoid("clear").SetException(new JSException("page quittée"));
        await _driver.HighlightAsync("#x", CancellationToken.None);

        await FluentActions.Awaiting(_driver.ClearAsync).Should().NotThrowAsync();
    }
}
