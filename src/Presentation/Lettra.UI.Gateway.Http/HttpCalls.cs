using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

/// <summary>Appels HTTP communs : une coupure réseau devient <c>Api.Unreachable</c>, jamais une exception à l'écran.</summary>
internal static class HttpCalls
{
    public static Task<OperationResult<T>> SendAsync<T>(
        Func<CancellationToken, Task<HttpResponseMessage>> send, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken) =>
        SendAsync(send, (response, ct) => response.ToOperationResultAsync(typeInfo, ct), cancellationToken);

    /// <summary>Même appel, quand la réponse se lit autrement qu'en un corps JSON (fichier, 204, 401 attendu).</summary>
    public static async Task<OperationResult<T>> SendAsync<T>(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        Func<HttpResponseMessage, CancellationToken, Task<OperationResult<T>>> read,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await send(cancellationToken).ConfigureAwait(false);
            return await read(response, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (HttpCalls.IsUnreachable(e, cancellationToken))
        {
            return OperationResult<T>.Failure(ErrorCodes.Api.Unreachable);
        }
    }

    /// <summary>Le refus du serveur (<c>ProblemResponse</c>), quel que soit le type attendu en cas de réussite.</summary>
    public static async Task<OperationResult<T>> FailureAsync<T>(this HttpResponseMessage response, CancellationToken cancellationToken) =>
        OperationResult<T>.Failure((await response.ToOperationResultAsync(ContractsJsonContext.Default.ProblemResponse, cancellationToken).ConfigureAwait(false)).Errors);

    /// <summary>
    /// Serveur injoignable : erreur réseau, ou délai d'attente dépassé (le <see cref="HttpClient"/> annule alors sans
    /// que l'appelant l'ait demandé). Une annulation voulue par l'écran remonte telle quelle.
    /// </summary>
    public static bool IsUnreachable(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    /// <summary>Paramètre <c>&amp;search=</c> échappé, vide sans recherche.</summary>
    public static string SearchParameter(string? search) =>
        ListSearch.Normalize(search) is { } text ? "&search=" + Uri.EscapeDataString(text) : string.Empty;

    /// <summary>Paramètres d'une liste de clients ou de fournisseurs ; le tri n'apparaît que s'il diffère du nom de A à Z.</summary>
    public static string PartyListParameters(PartyListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        string text = string.Create(CultureInfo.InvariantCulture, $"page={query.Page}&pageSize={query.PageSize}") + SearchParameter(query.Search);
        if (query.Sort != PartySortDto.Name || query.Direction != SortDirectionDto.Ascending)
        {
            text += string.Create(CultureInfo.InvariantCulture, $"&sort={query.Sort}&direction={query.Direction}");
        }

        return query.Kind is { } kind ? string.Create(CultureInfo.InvariantCulture, $"{text}&kind={kind}") : text;
    }

    public static Uri Relative(FormattableString uri) => new(string.Create(CultureInfo.InvariantCulture, $"{uri}"), UriKind.Relative);

    /// <summary>PUT d'une modification avec la version lue en <c>If-Match</c> (ADR-0013).</summary>
    public static Task<HttpResponseMessage> PutVersionedAsync<TBody>(
        this HttpClient http, Uri uri, long expectedVersion, TBody body, JsonTypeInfo<TBody> typeInfo, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Put, uri) { Content = JsonContent.Create(body, typeInfo) };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue(string.Create(CultureInfo.InvariantCulture, $"\"{expectedVersion}\"")));
        return SendAndDisposeAsync(http, request, cancellationToken);
    }

    private static async Task<HttpResponseMessage> SendAndDisposeAsync(HttpClient http, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            return await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
