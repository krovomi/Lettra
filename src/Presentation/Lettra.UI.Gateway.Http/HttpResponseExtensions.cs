using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;

namespace Lettra.UI.Gateway.Http;

/// <summary>Traduit une réponse HTTP en <see cref="OperationResult{T}"/> : corps typé, ou codes d'erreur du <c>ProblemResponse</c>.</summary>
internal static class HttpResponseExtensions
{
    public static async Task<OperationResult<T>> ToOperationResultAsync<T>(
        this HttpResponseMessage response, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            T? value = await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken).ConfigureAwait(false);
            return value is null ? OperationResult<T>.Failure(ErrorCodes.Api.Unexpected) : OperationResult<T>.Success(value);
        }

        ProblemResponse? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ProblemResponse, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Corps illisible (proxy, page d'erreur HTML) : erreur générique ci-dessous.
        }

        // 412 (ADR-0013) : le serveur joint le document actuel, pour la fusion à l'écran.
        if (problem?.Code == ErrorCodes.Concurrency.VersionMismatch && problem.Current is JsonElement current
            && current.ValueKind == JsonValueKind.Object && TryRead(current, typeInfo) is T currentValue)
        {
            return OperationResult<T>.Stale(currentValue);
        }

        List<ErrorDetail> errors = [];
        foreach ((string field, string[] codes) in problem?.Errors ?? new Dictionary<string, string[]>())
        {
            string? target = string.IsNullOrEmpty(field) ? null : char.ToUpperInvariant(field[0]) + field[1..];
            errors.AddRange(codes.Select(code => new ErrorDetail(code, target)));
        }

        // Le code principal, s'il n'est pas déjà rattaché à un champ (ex. Dossier.NotFound, Security.Forbidden).
        string? mainCode = problem?.Code;
        if (errors.Count == 0 || (mainCode is not null && !errors.Exists(e => e.Code == mainCode)))
        {
            errors.Add(new ErrorDetail(mainCode ?? ErrorCodes.Api.Unexpected));
        }

        return OperationResult<T>.Failure(errors);
    }

    /// <summary>
    /// La pièce jointe au 412 n'a pas toujours le type attendu : une suppression la lit comme un
    /// <see cref="ProblemResponse"/>. Illisible, elle est ignorée et le refus garde son code.
    /// </summary>
    private static T? TryRead<T>(JsonElement current, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return current.Deserialize(typeInfo);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
