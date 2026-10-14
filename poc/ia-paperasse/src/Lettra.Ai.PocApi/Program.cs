using System.Text.Json.Serialization;
using Lettra.Ai.Abstractions;
using Lettra.Ai.Composition;
using Lettra.Ai.Orchestration;
using Lettra.Poc.Application;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLettraAiPoc();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
var app = builder.Build();

// Erreurs métier traduites en réponses HTTP lisibles.
app.UseExceptionHandler(errors => errors.Run(async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var (status, title) = error switch
    {
        DossierNotFoundException or DocumentNotFoundException => (StatusCodes.Status404NotFound, error.Message),
        HumanActionRequiredException => (StatusCodes.Status403Forbidden, error.Message),
        _ => (StatusCodes.Status500InternalServerError, "Erreur interne"),
    };
    context.Response.StatusCode = status;
    await Results.Problem(title: title, statusCode: status).ExecuteAsync(context);
}));

// Identité simulée pour le POC : dans Lettra, elle vient du jeton Keycloak (ADR-0010) et des rôles à portée.
static (Guid UserId, HashSet<string> Permissions) Caller(HttpRequest r) => (
    Guid.TryParse(r.Headers["X-User-Id"], out var id) ? id : Guid.Empty,
    r.Headers["X-Permissions"].ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet());

var api = app.MapGroup("/api");

// Réglages IA du cabinet : désactivée par défaut, activée fonction par fonction par l'administrateur.
api.MapGet("/cabinets/{cabinetId:guid}/ia/reglages", async (Guid cabinetId, IAiUsagePolicy policy) =>
    Results.Ok(await policy.GetSettingsAsync(cabinetId)));

api.MapPut("/cabinets/{cabinetId:guid}/ia/reglages", (Guid cabinetId, AiSettingsDto dto, InMemoryAiUsagePolicy policy) =>
{
    policy.Set(cabinetId, new AiSettings
    {
        Enabled = dto.Enabled,
        Engine = dto.Engine,
        Features = dto.Features.ToHashSet(),
        ExcludedDossiers = (dto.ExcludedDossiers ?? []).ToHashSet(),
        OfficialWebLookup = dto.OfficialWebLookup,
    });
    return Results.NoContent();
});

// Contrôle de conformité déterministe : disponible même quand l'IA est désactivée.
api.MapGet("/dossiers/{dossierId:guid}/factures/{invoiceId:guid}/conformite", (Guid dossierId, Guid invoiceId, DossierUseCases useCases) =>
{
    var check = useCases.CheckInvoiceCompliance(dossierId, invoiceId);
    return Results.Ok(new { check.InvoiceNumber, check.Report.Status, check.Report.BlocksValidation, check.Report, check.Totals });
});

// Assistant : la réponse est la suite des événements (texte IA, résultats de règles, brouillons, avertissements).
api.MapPost("/dossiers/{dossierId:guid}/assistant", async (Guid dossierId, AssistantQuestionDto dto, HttpRequest http,
    IAccountingAssistant assistant, InMemoryDossierStore store, CancellationToken ct) =>
{
    var (userId, permissions) = Caller(http);
    var request = new AssistantRequest(store.Get(dossierId).CabinetId, dossierId, userId, permissions, dto.Feature, dto.Question,
        OpenInvoiceId: dto.OpenInvoiceId, ImportedContent: dto.ImportedContent);
    var events = new List<object>();
    await foreach (var e in assistant.AskAsync(request, ct)) events.Add(AssistantEventDto.From(e));
    return Results.Ok(events);
});

// Validation d'un brouillon : action humaine uniquement.
api.MapPost("/dossiers/{dossierId:guid}/devis/{draftId:guid}/valider", (Guid dossierId, Guid draftId, HttpRequest http, DossierUseCases useCases) =>
{
    var (userId, _) = Caller(http);
    var draft = useCases.ValidateDraft(dossierId, draftId, new Actor(userId, ActorKind.Human));
    return Results.Ok(new { draft.Id, draft.Status, draft.CreatedBy, draft.ValidatedBy });
});

api.MapGet("/dossiers/{dossierId:guid}/ia/journal", (Guid dossierId, IAiAuditLog log) =>
    Results.Ok(((InMemoryAiAuditLog)log).Entries.Where(e => e.DossierId == dossierId)));

// Écran « À propos » : notice de la licence MIT de Paperasse et version du skill pack (obligation de la licence).
api.MapGet("/a-propos", (IAiSkillCatalog skills) => Results.Ok(new
{
    skillPack = skills.Info,
    notice = "Contient les skills Paperasse (https://github.com/romainsimon/paperasse), licence MIT, (c) 2026 Romain Simon. " +
             "Les skills ne remplacent pas un expert-comptable inscrit à l'Ordre.",
}));

app.Run();

public sealed record AiSettingsDto(bool Enabled, AiEngineKind Engine, AiFeature[] Features, Guid[]? ExcludedDossiers, bool OfficialWebLookup);

public sealed record AssistantQuestionDto(AiFeature Feature, string Question, Guid? OpenInvoiceId, string? ImportedContent);

public static class AssistantEventDto
{
    public static object From(AssistantEvent e) => e switch
    {
        AssistantText t => new { type = "texte_ia", t.Text },
        AssistantRuleResult r => new { type = "regle", r.Tool, r.Content },
        AssistantToolUsed u => new { type = "outil", u.Tool, risk = u.Risk.ToString(), u.Succeeded },
        AssistantCitation c => new { type = "source", c.Reference },
        AssistantDraftProposed d => new { type = "brouillon", d.Draft },
        AssistantWarning w => new { type = "avertissement", w.Message },
        AssistantRefused r => new { type = "refus", r.Reason },
        AssistantUnavailable u => new { type = "indisponible", u.Reason },
        AssistantCompleted c => new { type = "fin", c.Engine, c.ToolRounds, c.Usage, skillPackCommit = c.SkillPack.Commit[..7] },
        _ => new { type = "inconnu" },
    };
}

public partial class Program;
