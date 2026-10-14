namespace Lettra.Ai.Abstractions;

public interface IAccountingAssistant
{
    IAsyncEnumerable<AssistantEvent> AskAsync(AssistantRequest request, CancellationToken ct = default);
}

/// <param name="OpenInvoiceId">Document ouvert à l'écran : seul ce document peut être lu (minimisation).</param>
/// <param name="ImportedContent">Contenu importé (e-mail, OCR, pièce) : toujours traité comme des données hostiles.</param>
public sealed record AssistantRequest(
    Guid CabinetId,
    Guid DossierId,
    Guid UserId,
    IReadOnlySet<string> Permissions,
    AiFeature Feature,
    string Question,
    AiSkill Skill = AiSkill.Comptable,
    Guid? OpenInvoiceId = null,
    string? ImportedContent = null);

public abstract record AssistantEvent;

/// <summary>L'assistant ne peut pas répondre (désactivé, droit manquant, hors ligne…), avec la raison à afficher.</summary>
public sealed record AssistantUnavailable(string Reason) : AssistantEvent;
/// <summary>Texte proposé par l'IA (affiché en bleu : suggestion).</summary>
public sealed record AssistantText(string Text) : AssistantEvent;
/// <summary>Résultat d'une règle déterministe C# (affiché en vert : certain).</summary>
public sealed record AssistantRuleResult(string Tool, string Content) : AssistantEvent;
public sealed record AssistantToolUsed(string Tool, AiToolRisk Risk, bool Succeeded) : AssistantEvent;
public sealed record AssistantCitation(string Reference) : AssistantEvent;
/// <summary>Brouillon proposé : l'utilisateur l'accepte ou le refuse, l'IA ne le valide jamais.</summary>
public sealed record AssistantDraftProposed(DraftCreated Draft) : AssistantEvent;
public sealed record AssistantWarning(string Message) : AssistantEvent;
public sealed record AssistantRefused(string Reason) : AssistantEvent;
public sealed record AssistantCompleted(string Engine, int ToolRounds, EngineUsage? Usage, SkillPackInfo SkillPack) : AssistantEvent;
