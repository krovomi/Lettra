using System.Net;
using System.Net.Http.Headers;
using Lettra.UI.Abstractions;
using Microsoft.JSInterop;

namespace Lettra.UI.Shared.Services;

/// <summary>
/// Envoi d'un fichier choisi par le navigateur lui-même (<c>postFile</c> de lettra-ui.js) : le fichier part du disque
/// en flux, sans passer par la mémoire de WebAssembly. Le cookie de session suit la requête (même origine).
/// </summary>
public sealed class JsBrowserFileSender(IJSRuntime js) : IBrowserFileSender, IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async Task<HttpResponseMessage> PostAsync(
        Uri url,
        string inputElementId,
        string fileField,
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, UiModule.Path).ConfigureAwait(false);
        PostedFileResponse posted;
        try
        {
            posted = await _module.InvokeAsync<PostedFileResponse>("postFile", cancellationToken, url.AbsoluteUri, inputElementId, fileField, fields, headers)
                .ConfigureAwait(false);
        }
        catch (JSException exception)
        {
            // fetch rejeté (réseau coupé, serveur injoignable) : la même erreur qu'un HttpClient.
            throw new HttpRequestException(exception.Message, exception);
        }

        if (posted.Status == 0)
        {
            throw new HttpRequestException("Aucun fichier dans le champ « " + inputElementId + " ».");
        }

        StringContent content = new(posted.Body ?? string.Empty);
        content.Headers.ContentType = posted.ContentType is { } type && MediaTypeHeaderValue.TryParse(type, out MediaTypeHeaderValue? parsed) ? parsed : null;
        return new HttpResponseMessage((HttpStatusCode)posted.Status) { Content = content };
    }

    public async ValueTask DisposeAsync() => await JsModules.ReleaseAsync(_module).ConfigureAwait(false);

    /// <summary>Réponse lue par le navigateur : statut, type et corps (une réponse de l'API reste petite).</summary>
    public sealed record PostedFileResponse(int Status, string? ContentType, string? Body);
}
