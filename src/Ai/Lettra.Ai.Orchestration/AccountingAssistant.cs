using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Lettra.Ai.Privacy;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Features.Assistant;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Ai.Orchestration;

/// <summary>
/// Orchestrateur côté serveur (annexe B §4.2, annexe F §10). Il garde la boucle d'outils pour que chaque étape passe
/// par les mêmes contrôles : politique du cabinet, droits, minimisation, pseudonymisation, outils sans pouvoir,
/// validation des entrées, journal, contrôle de la réponse. Le moteur ne fait que proposer.
/// </summary>
internal sealed class AccountingAssistant(
    IAiUsagePolicy policy,
    IAiEngineFactory engines,
    IAiSkillCatalog skills,
    ToolRegistry tools,
    IAiDossierContext dossiers,
    IAiAuditLog audit,
    TimeProvider time) : IAccountingAssistant
{
    public async IAsyncEnumerable<AssistantEvent> AskAsync(AssistantRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Politique du cabinet et droits de l'utilisateur (défense en profondeur : le pipeline l'a déjà vérifié).
        AiSettings settings = await policy.GetSettingsAsync(request.Organisation, cancellationToken).ConfigureAwait(false);
        Error? refusal = request.Permissions.Contains(Permission.IaUtiliser)
            ? AiErrors.CheckPolicy(settings, request.Feature, request.Dossier)
            : SecurityErrors.Forbidden;
        IAiEngine? engine = refusal is null ? engines.Resolve(settings.Engine) : null;
        if (refusal is null && engine is null)
        {
            refusal = AiErrors.EngineUnavailable;
        }

        if (refusal is not null || engine is null)
        {
            string code = refusal?.Code ?? ErrorCodes.Ai.EngineUnavailable;
            await AuditAsync(request, AiAuditKind.Unavailable, code, null, cancellationToken).ConfigureAwait(false);
            yield return new AssistantUnavailableEvent(code);
            yield break;
        }

        DateOnly today = LegalCalendar.Today(time);
        HashSet<AiSkill> staleSignalled = [];
        if (StaleWarning(request.Skill, today, staleSignalled) is { } stale)
        {
            yield return stale;
        }

        // 2. Minimisation et pseudonymisation : rien ne part en clair, pas même dans le journal.
        PseudonymizationSession privacy = new(await dossiers.GetKnownIdentitiesAsync(request.Dossier, cancellationToken).ConfigureAwait(false));
        string companyJson = privacy.PseudonymizeJson(await dossiers.GetCompanyJsonAsync(request.Dossier, cancellationToken).ConfigureAwait(false));
        string context = BuildContext(request, companyJson, today);
        IReadOnlyList<IAiTool> available = tools.For(request.Feature, request.Permissions, settings);
        List<EngineToolDefinition> definitions = [.. available.Select(t => new EngineToolDefinition(t.Name, t.Description, t.InputSchema))];
        AiToolContext toolContext = new(request.Organisation, request.Dossier, request.UserId, settings, request.OpenDocumentId);

        string userMessage = BuildUserMessage(request, privacy);
        await AuditAsync(request, AiAuditKind.Request, $"{request.Feature} : {userMessage}", engine.Name, cancellationToken).ConfigureAwait(false);

        List<EngineMessage> messages = [EngineMessage.UserText(userMessage)];
        List<string> trustedSources = [request.Question, request.ImportedContent ?? string.Empty];
        string stable = skills.GetOverlay() + "\n\n---\n\n" + skills.GetSkillInstructions(request.Skill);
        EngineUsage? usage = null;
        int rounds = 0;

        // 3. Boucle d'outils, tenue par le serveur.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EngineTurn turn = await engine.CompleteAsync(new EngineRequest(stable, context, messages, definitions), cancellationToken).ConfigureAwait(false);
            usage = EngineUsage.Add(usage, turn.Usage);
            List<ToolCallPart> calls = [.. turn.ToolCalls];
            await AuditAsync(request, AiAuditKind.EngineCall, $"arrêt={turn.StopReason} appels={calls.Count}", engine.Name, cancellationToken).ConfigureAwait(false);

            if (turn.StopReason == EngineStopReason.Refusal)
            {
                await AuditAsync(request, AiAuditKind.Refused, turn.Text, engine.Name, cancellationToken).ConfigureAwait(false);
                yield return new AssistantRefusedEvent(ErrorCodes.Ai.Refused);
                yield break;
            }

            if (calls.Count == 0 || turn.StopReason != EngineStopReason.ToolUse)
            {
                foreach (AssistantEvent answer in FinalAnswer(turn, privacy, trustedSources))
                {
                    yield return answer;
                }

                await AuditAsync(request, AiAuditKind.Answer, turn.Text, engine.Name, cancellationToken).ConfigureAwait(false);
                yield return Completed(engine, rounds, usage);
                yield break;
            }

            if (++rounds > settings.MaxToolRounds)
            {
                yield return new AssistantWarningEvent(ErrorCodes.Ai.ToolRoundsExceeded);
                yield return Completed(engine, rounds - 1, usage);
                yield break;
            }

            messages.Add(new EngineMessage(EngineRole.Assistant, turn.Parts));
            List<EnginePart> results = [];
            foreach (ToolCallPart call in calls)
            {
                (AiToolResult result, List<AssistantEvent> events) = await RunToolAsync(call, available, toolContext, privacy, request, engine.Name, cancellationToken).ConfigureAwait(false);
                foreach (AssistantEvent toolEvent in events)
                {
                    yield return toolEvent;

                    // Une fiche lue dans un autre skill (contrôleur fiscal, CAC) a sa propre fraîcheur.
                    if (toolEvent is AssistantCitationEvent { Reference: var reference }
                        && AiSkillNames.TryParse(reference.Split('/')[0], out AiSkill cited)
                        && StaleWarning(cited, today, staleSignalled) is { } citedStale)
                    {
                        yield return citedStale;
                    }
                }

                if (!result.IsError)
                {
                    trustedSources.Add(result.Content);
                }

                // Le résultat repart vers le moteur pseudonymisé, comme tout le reste.
                results.Add(new ToolResultPart(call.Id, privacy.Pseudonymize(result.Content), result.IsError));
            }

            messages.Add(new EngineMessage(EngineRole.User, results));
        }
    }

    /// <summary>Signale une fois par réponse un skill dont la mise à jour et la relecture ont plus de six mois.</summary>
    private AssistantWarningEvent? StaleWarning(AiSkill skill, DateOnly today, HashSet<AiSkill> signalled) =>
        skills.Info.IsStale(skill, today) && signalled.Add(skill)
            ? new AssistantWarningEvent(
                ErrorCodes.Ai.SkillPackStale,
                $"{AiSkillNames.Name(skill)} {skills.Info.FreshAsOf(skill).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}")
            : null;

    private static IEnumerable<AssistantEvent> FinalAnswer(EngineTurn turn, PseudonymizationSession privacy, List<string> trustedSources)
    {
        if (turn.StopReason == EngineStopReason.MaxTokens)
        {
            yield return new AssistantWarningEvent(ErrorCodes.Ai.Truncated);
        }

        List<string> invented = [.. privacy.UnknownTokens(turn.Text)];
        string text = ResponseGuard.Sanitize(privacy.Restore(turn.Text));
        if (invented.Count > 0)
        {
            yield return new AssistantWarningEvent(ErrorCodes.Ai.UnknownReferences, string.Join(", ", invented));
        }

        IReadOnlyList<string> ungrounded = ResponseGuard.UngroundedAmounts(text, trustedSources);
        if (ungrounded.Count > 0)
        {
            yield return new AssistantWarningEvent(ErrorCodes.Ai.UngroundedAmounts, string.Join(", ", ungrounded));
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            yield return new AssistantTextEvent(text);
        }
    }

    private static string BuildContext(AssistantRequest request, string companyJson, DateOnly today)
    {
        StringBuilder context = new();
        context.Append("Date du jour : ").Append(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        context.Append("Fonction demandée : ").Append(request.Feature).Append('\n');
        if (request.OpenDocumentId is { } id)
        {
            context.Append("Document ouvert à l'écran (document_id) : ").Append(id).Append('\n');
        }

        context.Append("company.json du dossier :\n```json\n").Append(companyJson).Append("\n```\n");
        return context.ToString();
    }

    private static string BuildUserMessage(AssistantRequest request, PseudonymizationSession privacy)
    {
        string question = privacy.Pseudonymize(request.Question);
        if (string.IsNullOrWhiteSpace(request.ImportedContent))
        {
            return question;
        }

        // Contenu importé : délimité et neutralisé, jamais suivi comme une consigne.
        string imported = privacy.Pseudonymize(request.ImportedContent)
            .Replace("<donnees_importees>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</donnees_importees>", string.Empty, StringComparison.OrdinalIgnoreCase);
        return question + "\n\n<donnees_importees>\n" + imported + "\n</donnees_importees>";
    }

    private AssistantCompletedEvent Completed(IAiEngine engine, int rounds, EngineUsage? usage) =>
        new(engine.Name, rounds, usage?.InputTokens, usage?.OutputTokens, skills.Info.Commit);

    private async Task<(AiToolResult Result, List<AssistantEvent> Events)> RunToolAsync(
        ToolCallPart call,
        IReadOnlyList<IAiTool> available,
        AiToolContext context,
        PseudonymizationSession privacy,
        AssistantRequest request,
        string engineName,
        CancellationToken cancellationToken)
    {
        List<AssistantEvent> events = [];
        IAiTool? tool = available.FirstOrDefault(t => string.Equals(t.Name, call.Name, StringComparison.Ordinal));
        if (tool is null)
        {
            await AuditAsync(request, AiAuditKind.ToolRejected, $"outil inconnu ou non autorisé : {call.Name}", engineName, cancellationToken).ConfigureAwait(false);
            events.Add(new AssistantWarningEvent(ErrorCodes.Ai.ToolRejected, call.Name));
            return (AiToolResult.Error($"Outil « {call.Name} » indisponible. Outils disponibles : {string.Join(", ", available.Select(t => t.Name))}."), events);
        }

        if (JsonSchemaValidator.Validate(call.Input, tool.InputSchema) is { } invalid)
        {
            await AuditAsync(request, AiAuditKind.ToolRejected, $"{call.Name} : entrée invalide ({invalid})", engineName, cancellationToken).ConfigureAwait(false);
            events.Add(new AssistantToolUsedEvent(tool.Name, tool.Risk, Succeeded: false));
            return (AiToolResult.Error($"Paramètres invalides : {invalid}"), events);
        }

        AiToolResult result;
        try
        {
            result = await tool
                .InvokeAsync(tool.ReceivesRealValues ? privacy.RestoreJson(call.Input) : call.Input, context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException or FormatException or KeyNotFoundException
            && !cancellationToken.IsCancellationRequested)
        {
            // Le message technique ne part ni vers le moteur ni vers le journal : il peut contenir des données.
            result = AiToolResult.Error("L'outil a échoué.");
        }

        // Le journal garde l'entrée telle que le moteur l'a écrite, donc pseudonymisée.
        await AuditAsync(request, AiAuditKind.ToolCall, $"{tool.Name} {call.Input.GetRawText()} → {(result.IsError ? "erreur" : "ok")}", engineName, cancellationToken).ConfigureAwait(false);
        events.Add(new AssistantToolUsedEvent(tool.Name, tool.Risk, !result.IsError));
        if (result.Citation is { } citation)
        {
            events.Add(new AssistantCitationEvent(citation));
        }

        if (result.IsDeterministicRule && !result.IsError)
        {
            events.Add(new AssistantRuleResultEvent(tool.Name, result.Content));
        }

        if (result.Draft is { } draft)
        {
            await AuditAsync(request, AiAuditKind.Draft, $"{draft.Kind} {draft.DraftId}", engineName, cancellationToken).ConfigureAwait(false);
            events.Add(new AssistantDraftProposedEvent(draft.DraftId, draft.Kind, draft.Summary));
        }

        return (result, events);
    }

    private ValueTask AuditAsync(AssistantRequest request, AiAuditKind kind, string detail, string? engine, CancellationToken cancellationToken) =>
        audit.AppendAsync(new AiAuditEntry(time.GetUtcNow(), request.Dossier, request.UserId, kind, detail, engine), cancellationToken);
}
