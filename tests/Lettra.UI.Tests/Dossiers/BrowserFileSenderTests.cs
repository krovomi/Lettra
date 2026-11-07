using System.Net;
using Bunit;
using Lettra.UI.Shared.Services;
using Microsoft.JSInterop;

namespace Lettra.UI.Tests.Dossiers;

/// <summary>
/// Envoi d'un fichier par le navigateur lui-même : la réponse de l'API se lit comme celle d'un HttpClient, et un fetch
/// rejeté ou un champ vide se présentent comme un serveur injoignable.
/// </summary>
public sealed class BrowserFileSenderTests : BunitContext
{
    private static readonly Uri Import = new("https://lettra.example/api/v1/cabinets/c/dossiers/import");

    private readonly BunitJSModuleInterop _module;

    public BrowserFileSenderTests() => _module = JSInterop.SetupModule(UiModule.Path);

    [Fact]
    public async Task La_reponse_du_navigateur_devient_une_reponse_http()
    {
        _module.Setup<JsBrowserFileSender.PostedFileResponse>(
                "postFile", i => (string)i.Arguments[0]! == Import.AbsoluteUri && (string)i.Arguments[1]! == "champ" && (string)i.Arguments[2]! == "paquet")
            .SetResult(new JsBrowserFileSender.PostedFileResponse(409, "application/problem+json; charset=utf-8", "{\"code\":\"Dossier.AlreadyExists\"}"));
        await using JsBrowserFileSender sender = new(JSInterop.JSRuntime);

        using HttpResponseMessage response = await sender.PostAsync(
            Import, "champ", "paquet", new Dictionary<string, string> { ["phrase"] = "x" }, new Dictionary<string, string> { ["X-Lettra-Csrf"] = "1" }, default);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().Contain("Dossier.AlreadyExists");
    }

    [Fact]
    public async Task Un_champ_vide_ou_un_fetch_rejete_se_lit_comme_un_serveur_injoignable()
    {
        await using JsBrowserFileSender sender = new(JSInterop.JSRuntime);
        Dictionary<string, string> none = [];

        _module.Setup<JsBrowserFileSender.PostedFileResponse>("postFile", i => (string)i.Arguments[1]! == "vide")
            .SetResult(new JsBrowserFileSender.PostedFileResponse(0, null, null));
        await sender.Invoking(s => s.PostAsync(Import, "vide", "paquet", none, none, default)).Should().ThrowAsync<HttpRequestException>();

        _module.Setup<JsBrowserFileSender.PostedFileResponse>("postFile", i => (string)i.Arguments[1]! == "coupe")
            .SetException(new JSException("Failed to fetch"));
        await sender.Invoking(s => s.PostAsync(Import, "coupe", "paquet", none, none, default)).Should().ThrowAsync<HttpRequestException>()
            .WithMessage("Failed to fetch");
    }
}
