using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lettra.Infrastructure.External.Vies;

// POST check-vat-number de l'API REST de VIES. Les États membres qui ne publient pas le nom ou
// l'adresse renvoient « --- ».

internal sealed record ViesRequest(
    [property: JsonPropertyName("countryCode")] string CountryCode,
    [property: JsonPropertyName("vatNumber")] string VatNumber);

internal sealed record ViesResponse(
    [property: JsonPropertyName("countryCode")] string? CountryCode,
    [property: JsonPropertyName("vatNumber")] string? VatNumber,
    [property: JsonPropertyName("requestDate")] DateTimeOffset? RequestDate,
    [property: JsonPropertyName("valid")] bool? Valid,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("userError")] string? UserError,
    [property: JsonPropertyName("actionSucceed")] bool? ActionSucceed,
    [property: JsonPropertyName("errorWrappers")] IReadOnlyList<ViesError>? ErrorWrappers);

internal sealed record ViesError(
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("message")] string? Message);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ViesRequest))]
[JsonSerializable(typeof(ViesResponse))]
internal sealed partial class ViesJsonContext : JsonSerializerContext;
