using System.Text.Json.Serialization;

namespace Lettra.Contracts.Common;

/// <summary>
/// Corps d'erreur HTTP (RFC 9457 « problem details ») avec l'extension <c>code</c> de Lettra et,
/// pour la validation, le détail par champ. Les valeurs du dictionnaire <c>errors</c> sont des codes.
/// </summary>
public sealed record ProblemResponse(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("status")] int? Status,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("errors")] IReadOnlyDictionary<string, string[]>? Errors,
    [property: JsonPropertyName("traceId")] string? TraceId);

/// <summary>Noms d'en-têtes HTTP propres à Lettra.</summary>
public static class LettraHeaders
{
    /// <summary>Dossier (tenant) visé par la requête, toujours revérifié contre les droits de l'utilisateur.</summary>
    public const string Dossier = "X-Dossier";

    /// <summary>Clé d'idempotence d'une commande (double clic, rejeu après coupure).</summary>
    public const string IdempotencyKey = "Idempotency-Key";
}
