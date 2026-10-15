using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Assistant;

/// <summary>
/// Points d'IA activables un par un par le cabinet (annexe D, points numérotés). Le point 1 crée au plus un
/// brouillon ; les autres ne font que lire (annexe C, phase 6).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssistantFeature>))]
public enum AssistantFeature
{
    /// <summary>Point 1 : saisie en langage naturel, brouillon de devis que l'utilisateur relit et valide lui-même.</summary>
    DraftFromText = 1,

    /// <summary>Point 3 : « Pourquoi ? » sur un constat de la checklist de conformité du document ouvert.</summary>
    ExplainCompliance = 3,

    /// <summary>Point 4 : diagnostic e-facturation du document ouvert.</summary>
    EInvoicingDiagnosis = 4,

    /// <summary>Point 6 : questions TVA et réglementaires.</summary>
    RegulatoryQuestions = 6,
}

/// <summary>Pouvoir d'un outil. Il n'existe volontairement ni « valide », ni « émet », ni « supprime » (ADR-0006, ADR-0012).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssistantToolRisk>))]
public enum AssistantToolRisk
{
    ReadOnly = 1,
    CreatesDraft = 2,
}

/// <summary>Question posée à l'assistant, dans le dossier ouvert (en-tête <c>X-Dossier</c>).</summary>
public sealed record AskAssistantRequest
{
    public const int MaxQuestionLength = 4000;

    public AssistantFeature Feature { get; set; } = AssistantFeature.RegulatoryQuestions;

    [Required(ErrorMessage = ErrorCodes.Validation.Required)]
    [StringLength(MaxQuestionLength, ErrorMessage = ErrorCodes.Validation.TooLong)]
    public string Question { get; set; } = string.Empty;

    /// <summary>Document ouvert à l'écran : le seul que les outils peuvent lire (minimisation, annexe F §10).</summary>
    public Guid? OpenDocumentId { get; set; }
}

/// <summary>
/// Événement de la réponse, diffusé au fil de l'eau (une ligne JSON par événement). Les textes du système sont des
/// <b>codes</b> traduits par l'UI ; seul le texte rédigé par le modèle arrive tel quel, dans la langue de la question.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AssistantUnavailableEvent), "unavailable")]
[JsonDerivedType(typeof(AssistantTextEvent), "text")]
[JsonDerivedType(typeof(AssistantRuleResultEvent), "rule")]
[JsonDerivedType(typeof(AssistantToolUsedEvent), "tool")]
[JsonDerivedType(typeof(AssistantCitationEvent), "citation")]
[JsonDerivedType(typeof(AssistantDraftProposedEvent), "draft")]
[JsonDerivedType(typeof(AssistantWarningEvent), "warning")]
[JsonDerivedType(typeof(AssistantRefusedEvent), "refused")]
[JsonDerivedType(typeof(AssistantCompletedEvent), "completed")]
public abstract record AssistantEvent;

/// <summary>L'assistant ne peut pas répondre (désactivé, dossier exclu, pas de moteur), avec le code de la raison.</summary>
public sealed record AssistantUnavailableEvent(string Code) : AssistantEvent;

/// <summary>Texte proposé par l'IA, affiché comme une suggestion.</summary>
public sealed record AssistantTextEvent(string Text) : AssistantEvent;

/// <summary>Résultat d'une règle déterministe C#, affiché comme certain.</summary>
public sealed record AssistantRuleResultEvent(string Tool, string Content) : AssistantEvent;

public sealed record AssistantToolUsedEvent(string Tool, AssistantToolRisk Risk, bool Succeeded) : AssistantEvent;

/// <summary>Fiche de référence lue par l'assistant (skill Paperasse) ou page officielle consultée.</summary>
public sealed record AssistantCitationEvent(string Reference) : AssistantEvent;

/// <summary>Brouillon proposé : l'utilisateur l'accepte ou le refuse, l'IA ne le valide jamais.</summary>
public sealed record AssistantDraftProposedEvent(Guid DraftId, string Kind, string Summary) : AssistantEvent;

/// <summary>Point d'attention, avec un code et un détail factuel (montants ou références en cause).</summary>
public sealed record AssistantWarningEvent(string Code, string? Detail = null) : AssistantEvent;

public sealed record AssistantRefusedEvent(string Code) : AssistantEvent;

/// <summary>Fin de la réponse : moteur utilisé, étapes d'outils, consommation et version du référentiel.</summary>
public sealed record AssistantCompletedEvent(
    string Engine,
    int ToolRounds,
    long? InputTokens,
    long? OutputTokens,
    string SkillPackCommit) : AssistantEvent;

/// <summary>
/// Ce que l'assistant peut faire pour l'utilisateur dans le dossier ouvert : vide quand l'IA est désactivée (réglage par
/// défaut), le dossier exclu, le moteur absent ou la permission manquante. L'UI n'affiche le panneau que s'il y a une fonction.
/// </summary>
public sealed record AssistantStatusResponse(IReadOnlyList<AssistantFeature> Features, string? Engine);

/// <summary>Entrée du journal IA du dossier (annexe F §10).</summary>
public sealed record AssistantJournalEntry(
    DateTimeOffset At,
    Guid UserId,
    string Kind,
    string Detail,
    string? Engine);
