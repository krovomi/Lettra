using System.Globalization;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

/// <summary>Requêtes des pièces versionnées : la version lue part en <c>If-Match</c> (412 avec la pièce actuelle si elle a changé).</summary>
internal static class VersionedCalls
{
    public static Task<OperationResult<T>> SendAsync<T>(
        HttpClient http, HttpMethod method, Uri uri, long? expectedVersion, HttpContent? content, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.SendAsync(Build(method, uri, expectedVersion, content), ct), typeInfo, cancellationToken);

    /// <summary>Suppression : 204 devient <c>true</c>, une erreur garde ses codes.</summary>
    public static async Task<OperationResult<bool>> DeleteAsync(HttpClient http, Uri uri, long? expectedVersion, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.SendAsync(Build(HttpMethod.Delete, uri, expectedVersion, null), cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return OperationResult<bool>.Success(true);
            }

            OperationResult<ProblemResponse> failure = await response.ToOperationResultAsync(ContractsJsonContext.Default.ProblemResponse, cancellationToken).ConfigureAwait(false);
            return OperationResult<bool>.Failure(failure.Errors);
        }
        catch (Exception e) when (HttpCalls.IsUnreachable(e, cancellationToken))
        {
            return OperationResult<bool>.Failure(ErrorCodes.Api.Unreachable);
        }
    }

    public static string ListQuery(BillingListQuery query, params (string Name, string? Value)[] extra)
    {
        ArgumentNullException.ThrowIfNull(query);
        StringBuilder text = new(string.Create(CultureInfo.InvariantCulture, $"page={query.Page}&pageSize={query.PageSize}"));
        Append(text, "search", ListSearch.Normalize(query.Search));
        Append(text, "customerId", query.CustomerId?.ToString());
        Append(text, "from", query.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(text, "to", query.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(text, "minAmount", query.MinAmount?.ToString(CultureInfo.InvariantCulture));
        Append(text, "maxAmount", query.MaxAmount?.ToString(CultureInfo.InvariantCulture));
        if (query.Sort != BillingSortDto.IssueDate || query.Direction != SortDirectionDto.Descending)
        {
            Append(text, "sort", query.Sort.ToString());
            Append(text, "direction", query.Direction.ToString());
        }
        foreach ((string name, string? value) in extra)
        {
            Append(text, name, value);
        }

        return text.ToString();
    }

    private static void Append(StringBuilder text, string name, string? value)
    {
        if (value is not null)
        {
            text.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
        }
    }

    private static HttpRequestMessage Build(HttpMethod method, Uri uri, long? expectedVersion, HttpContent? content)
    {
        // If-Match est obligatoire sur ces routes (428 sinon) : sans version lue (première fiche entreprise), « * ».
        HttpRequestMessage request = new(method, uri) { Content = content };
        request.Headers.TryAddWithoutValidation(
            "If-Match", expectedVersion is { } version ? string.Create(CultureInfo.InvariantCulture, $"\"{version}\"") : "*");
        return request;
    }
}
