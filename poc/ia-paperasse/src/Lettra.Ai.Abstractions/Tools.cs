using System.Text.Json;

namespace Lettra.Ai.Abstractions;

/// <summary>Contrat d'un outil exposé au modèle. Un outil appelle un cas d'usage de l'application, avec les droits de l'utilisateur.</summary>
public interface IAiTool
{
    /// <summary>Nom vu par le modèle (ex. « verifier_facture »).</summary>
    string Name { get; }
    /// <summary>Description en français, lue par le modèle.</summary>
    string Description { get; }
    /// <summary>JSON Schema strict de l'entrée.</summary>
    JsonElement InputSchema { get; }
    AiToolRisk Risk { get; }
    /// <summary>Droit applicatif exigé, en plus de <see cref="AiPermissions.UseAssistant"/>.</summary>
    string RequiredPermission { get; }
    /// <summary>Fonctions IA pour lesquelles l'outil est proposé au modèle.</summary>
    IReadOnlySet<AiFeature> Features { get; }

    /// <summary>
    /// Vrai si l'outil reçoit ses paramètres ré-identifiés (jetons remplacés par les vraies valeurs, côté serveur).
    /// Un outil qui sort du serveur (consultation web) reste sur les jetons, pour ne rien laisser fuiter.
    /// </summary>
    bool ReceivesRealValues => true;

    Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken ct = default);
}

/// <param name="OpenInvoiceId">Seul document que les outils peuvent lire (minimisation, annexe F §10).</param>
public sealed record AiToolContext(Guid CabinetId, Guid DossierId, Guid UserId, AiSettings Settings, Guid? OpenInvoiceId);

public sealed record AiToolResult(string Content, bool IsError = false)
{
    /// <summary>Fiche de référence lue (pour la citer).</summary>
    public string? Citation { get; init; }
    /// <summary>Brouillon créé, à relire par un humain.</summary>
    public DraftCreated? Draft { get; init; }
    /// <summary>Résultat issu d'une règle déterministe (affiché comme certain, en vert).</summary>
    public bool IsDeterministicRule { get; init; }

    public static AiToolResult Json<T>(T value, JsonSerializerOptions? options = null) =>
        new(JsonSerializer.Serialize(value, options ?? ToolJson.Options));

    public static AiToolResult Error(string message) => new(message, IsError: true);
}

public sealed record DraftCreated(Guid DraftId, string Kind, string Summary);

public static class ToolJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };
}
