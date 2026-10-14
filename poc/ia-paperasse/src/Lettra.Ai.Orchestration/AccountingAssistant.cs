using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Lettra.Ai.Abstractions;
using Lettra.Ai.Privacy;

namespace Lettra.Ai.Orchestration;

/// <summary>
/// Orchestrateur côté serveur (annexe B §4.2, annexe F §10). Il garde la boucle d'outils pour que chaque étape passe
/// par les mêmes contrôles : droits, minimisation, pseudonymisation, outils sans pouvoir, validation des entrées,
/// journal, contrôle de la réponse. Le moteur ne fait que proposer.
/// </summary>
public sealed class AccountingAssistant(
    IAiUsagePolicy policy,
    IAiEngineFactory engines,
    IAiSkillCatalog skills,
    ToolRegistry tools,
    IDossierContextProvider dossiers,
    IAiAuditLog audit,
    TimeProvider time) : IAccountingAssistant
{
    public async IAsyncEnumerable<AssistantEvent> AskAsync(AssistantRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 1. Politique du cabinet et droits de l'utilisateur
        var settings = await policy.GetSettingsAsync(request.CabinetId, ct);
        var refusal = CheckAccess(request, settings);
        var engine = refusal is null ? engines.Resolve(settings.Engine) : null;
        if (refusal is null && engine is null) refusal = "Aucun moteur d'IA n'est disponible pour ce cabinet sur ce déploiement.";
        if (refusal is not null)
        {
            await Audit(request, AiAuditKind.Unavailable, refusal, null, ct);
            yield return new AssistantUnavailable(refusal);
            yield break;
        }

        await Audit(request, AiAuditKind.Request, $"{request.Feature} : {request.Question}", engine!.Name, ct);
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        if (skills.Info.IsStale(today))
            yield return new AssistantWarning($"Référentiel métier daté du {skills.Info.SkillLastUpdated:dd/MM/yyyy} : vérifier les seuils et taux sur les sites officiels.");

        // 2. Minimisation et pseudonymisation : rien ne part en clair
        var privacy = new PseudonymizationSession(await dossiers.GetKnownIdentitiesAsync(request.DossierId, ct));
        var context = BuildContext(request, privacy.PseudonymizeJson(await dossiers.GetCompanyJsonAsync(request.DossierId, ct)), today);
        var available = tools.For(request.Feature, request.Permissions, settings);
        var definitions = available.Select(t => new EngineToolDefinition(t.Name, t.Description, t.InputSchema)).ToList();
        var toolContext = new AiToolContext(request.CabinetId, request.DossierId, request.UserId, settings, request.OpenInvoiceId);

        var messages = new List<EngineMessage> { EngineMessage.UserText(BuildUserMessage(request, privacy)) };
        var trustedSources = new List<string> { request.Question, request.ImportedContent ?? "" };
        var stable = skills.GetOverlay() + "\n\n---\n\n" + skills.GetSkillInstructions(request.Skill);
        EngineUsage? usage = null;
        var rounds = 0;

        // 3. Boucle d'outils, tenue par le serveur
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var turn = await engine.CompleteAsync(new EngineRequest(stable, context, messages, definitions), ct);
            usage = Add(usage, turn.Usage);
            await Audit(request, AiAuditKind.EngineCall, $"arrêt={turn.StopReason} appels={turn.ToolCalls.Count()}", engine.Name, ct);

            if (turn.StopReason == EngineStopReason.Refusal)
            {
                await Audit(request, AiAuditKind.Refused, turn.Text, engine.Name, ct);
                yield return new AssistantRefused("Le moteur a refusé de répondre à cette demande. Reformulez ou traitez-la sans l'assistant.");
                yield break;
            }

            var calls = turn.ToolCalls.ToList();
            if (calls.Count == 0 || turn.StopReason != EngineStopReason.ToolUse)
            {
                foreach (var evt in FinalAnswer(turn, privacy, trustedSources)) yield return evt;
                await Audit(request, AiAuditKind.Answer, turn.Text, engine.Name, ct);
                yield return new AssistantCompleted(engine.Name, rounds, usage, skills.Info);
                yield break;
            }

            if (++rounds > settings.MaxToolRounds)
            {
                yield return new AssistantWarning("L'assistant a atteint le nombre maximal d'étapes sans conclure.");
                yield return new AssistantCompleted(engine.Name, rounds - 1, usage, skills.Info);
                yield break;
            }

            messages.Add(new EngineMessage(EngineRole.Assistant, turn.Parts));
            var results = new List<EnginePart>();
            foreach (var call in calls)
            {
                var (result, events) = await RunTool(call, available, toolContext, privacy, request, engine.Name, ct);
                foreach (var e in events) yield return e;
                if (!result.IsError) trustedSources.Add(result.Content);
                // Le résultat repart vers le moteur pseudonymisé, comme tout le reste.
                results.Add(new ToolResultPart(call.Id, privacy.Pseudonymize(result.Content), result.IsError));
            }
            messages.Add(new EngineMessage(EngineRole.User, results));
        }
    }

    private static string? CheckAccess(AssistantRequest request, AiSettings settings)
    {
        if (!settings.Enabled) return "L'assistant IA est désactivé pour ce cabinet. L'administrateur du cabinet peut l'activer.";
        if (!settings.Features.Contains(request.Feature)) return "Cette fonction de l'assistant n'est pas activée par le cabinet.";
        if (settings.ExcludedDossiers.Contains(request.DossierId)) return "Le cabinet a exclu ce dossier de l'assistant IA.";
        if (!request.Permissions.Contains(AiPermissions.UseAssistant)) return "Vous n'avez pas le droit d'utiliser l'assistant IA.";
        if (settings.Engine == AiEngineKind.None) return "Aucun moteur d'IA n'est choisi pour ce cabinet. Les contrôles de conformité restent actifs.";
        return null;
    }

    private async Task<(AiToolResult Result, List<AssistantEvent> Events)> RunTool(
        ToolCallPart call, IReadOnlyList<IAiTool> available, AiToolContext context, PseudonymizationSession privacy,
        AssistantRequest request, string engineName, CancellationToken ct)
    {
        var events = new List<AssistantEvent>();
        var tool = available.FirstOrDefault(t => t.Name == call.Name);
        if (tool is null)
        {
            await Audit(request, AiAuditKind.ToolRejected, $"outil inconnu ou non autorisé : {call.Name}", engineName, ct);
            events.Add(new AssistantWarning($"L'assistant a demandé une action non disponible ({call.Name}) : refusée."));
            return (AiToolResult.Error($"Outil « {call.Name} » indisponible. Outils disponibles : {string.Join(", ", available.Select(t => t.Name))}."), events);
        }

        if (JsonSchemaValidator.Validate(call.Input, tool.InputSchema) is { } invalid)
        {
            await Audit(request, AiAuditKind.ToolRejected, $"{call.Name} : entrée invalide ({invalid})", engineName, ct);
            events.Add(new AssistantToolUsed(tool.Name, tool.Risk, false));
            return (AiToolResult.Error($"Paramètres invalides : {invalid}"), events);
        }

        var input = tool.ReceivesRealValues ? privacy.RestoreJson(call.Input) : call.Input;
        AiToolResult result;
        try
        {
            result = await tool.InvokeAsync(input, context, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            result = AiToolResult.Error($"L'outil a échoué : {e.Message}");
        }

        await Audit(request, AiAuditKind.ToolCall, $"{tool.Name} {call.Input.GetRawText()} → {(result.IsError ? "erreur" : "ok")}", engineName, ct);
        events.Add(new AssistantToolUsed(tool.Name, tool.Risk, !result.IsError));
        if (result.Citation is { } citation) events.Add(new AssistantCitation(citation));
        if (result.IsDeterministicRule && !result.IsError) events.Add(new AssistantRuleResult(tool.Name, result.Content));
        if (result.Draft is { } draft)
        {
            await Audit(request, AiAuditKind.Draft, $"{draft.Kind} {draft.DraftId} : {draft.Summary}", engineName, ct);
            events.Add(new AssistantDraftProposed(draft));
        }
        return (result, events);
    }

    private static IEnumerable<AssistantEvent> FinalAnswer(EngineTurn turn, PseudonymizationSession privacy, List<string> trustedSources)
    {
        if (turn.StopReason == EngineStopReason.MaxTokens)
            yield return new AssistantWarning("La réponse a été tronquée.");

        var invented = privacy.UnknownTokens(turn.Text).ToList();
        var text = ResponseGuard.Sanitize(privacy.Restore(turn.Text));
        if (invented.Count > 0)
            yield return new AssistantWarning($"La réponse cite des références inconnues ({string.Join(", ", invented)}) : à vérifier.");

        var ungrounded = ResponseGuard.UngroundedAmounts(text, trustedSources);
        if (ungrounded.Count > 0)
            yield return new AssistantWarning($"Montants non issus d'un calcul de Lettra : {string.Join(", ", ungrounded)}. À vérifier avant usage.");

        if (!string.IsNullOrWhiteSpace(text)) yield return new AssistantText(text);
    }

    private static string BuildContext(AssistantRequest request, string companyJson, DateOnly today)
    {
        var sb = new StringBuilder();
        sb.Append("Date du jour : ").Append(today.ToString("yyyy-MM-dd")).Append('\n');
        sb.Append("Fonction demandée : ").Append(request.Feature).Append('\n');
        if (request.OpenInvoiceId is { } id) sb.Append("Facture ouverte à l'écran (facture_id) : ").Append(id).Append('\n');
        sb.Append("company.json du dossier :\n```json\n").Append(companyJson).Append("\n```\n");
        return sb.ToString();
    }

    private static string BuildUserMessage(AssistantRequest request, PseudonymizationSession privacy)
    {
        var question = privacy.Pseudonymize(request.Question);
        if (string.IsNullOrWhiteSpace(request.ImportedContent)) return question;

        // Contenu importé : délimité et neutralisé, jamais suivi comme une consigne.
        var imported = privacy.Pseudonymize(request.ImportedContent)
            .Replace("<donnees_importees>", "", StringComparison.OrdinalIgnoreCase)
            .Replace("</donnees_importees>", "", StringComparison.OrdinalIgnoreCase);
        return question + "\n\n<donnees_importees>\n" + imported + "\n</donnees_importees>";
    }

    private static EngineUsage? Add(EngineUsage? a, EngineUsage? b) =>
        a is null ? b : b is null ? a
            : new EngineUsage(a.InputTokens + b.InputTokens, a.OutputTokens + b.OutputTokens,
                a.CacheReadTokens + b.CacheReadTokens, a.CacheWriteTokens + b.CacheWriteTokens);

    private Task Audit(AssistantRequest r, AiAuditKind kind, string detail, string? engine, CancellationToken ct) =>
        audit.AppendAsync(new AiAuditEntry(time.GetUtcNow(), r.CabinetId, r.DossierId, r.UserId, kind, detail, engine), ct);
}
