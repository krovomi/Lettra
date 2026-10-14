using System.Text.Encodings.Web;
using System.Text.Json;
using Lettra.Application.Abstractions.Identity;
using Lettra.Contracts.Assistant;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Ai;

/// <summary>
/// Contrat d'un outil exposé au modèle. Un outil appelle les cas d'usage de l'application par les dispatchers,
/// donc avec les droits, la validation et l'audit de l'utilisateur, exactement comme l'UI.
/// </summary>
public interface IAiTool
{
    /// <summary>Nom vu par le modèle (ex. « lire_reference »).</summary>
    string Name { get; }

    /// <summary>Description en français, lue par le modèle.</summary>
    string Description { get; }

    /// <summary>JSON Schema strict de l'entrée.</summary>
    JsonElement InputSchema { get; }

    AssistantToolRisk Risk { get; }

    /// <summary>Permission exigée en plus de <see cref="Permission.IaUtiliser"/>, ou <c>null</c> pour un outil qui ne lit pas le dossier.</summary>
    Permission? RequiredPermission { get; }

    /// <summary>Points d'IA pour lesquels l'outil est proposé au modèle.</summary>
    IReadOnlySet<AssistantFeature> Features { get; }

    /// <summary>
    /// Vrai si l'outil reçoit ses paramètres ré-identifiés (jetons remplacés par les vraies valeurs, côté serveur).
    /// Un outil qui sort du serveur (consultation web) reste sur les jetons, pour ne rien laisser fuiter.
    /// </summary>
    bool ReceivesRealValues { get; }

    /// <summary>Vrai si l'outil dépend d'un réglage du cabinet qu'il faut avoir activé (consultation web).</summary>
    bool IsEnabledBy(AiSettings settings);

    Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken cancellationToken);
}

/// <summary><c>OpenDocumentId</c> : Seul document que les outils peuvent lire (minimisation, annexe F §10).</summary>
public sealed record AiToolContext(
    OrganisationId Organisation,
    DossierId Dossier,
    Guid UserId,
    AiSettings Settings,
    Guid? OpenDocumentId);

public sealed record AiToolResult(string Content, bool IsError = false)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>Fiche de référence lue ou page officielle consultée, pour la citer.</summary>
    public string? Citation { get; init; }

    /// <summary>Brouillon créé, à relire par un humain.</summary>
    public DraftCreated? Draft { get; init; }

    /// <summary>Résultat issu d'une règle déterministe (affiché comme certain).</summary>
    public bool IsDeterministicRule { get; init; }

    public static AiToolResult Json<T>(T value) => new(JsonSerializer.Serialize(value, JsonOptions));

    public static AiToolResult Error(string message) => new(message, IsError: true);
}

public sealed record DraftCreated(Guid DraftId, string Kind, string Summary);
