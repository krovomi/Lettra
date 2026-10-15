using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Catalog;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;

namespace Lettra.Ai.Tools.Tools;

/// <summary>
/// Outils de la facturation (annexe D, points 1 et 3). Ils passent par les dispatchers, donc par les mêmes droits, la
/// même validation et le même audit que l'écran. Le seul qui écrit crée un devis <b>brouillon</b> : sans numéro,
/// modifiable, que l'utilisateur relit puis valide lui-même (ADR-0006). Aucun outil ne valide, n'émet ni ne supprime.
/// </summary>
internal static class BillingToolSupport
{
    public const int MaxResults = 10;

    public static readonly string[] VatRates = ["FR-20", "FR-10", "FR-5.5", "FR-2.1", "FR-0"];

    public static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Message d'échec sans détail technique : seuls les codes de l'application repartent vers le moteur.</summary>
    public static AiToolResult Failure(IEnumerable<Error> errors) =>
        AiToolResult.Error("Refusé par Lettra : " + string.Join(", ", errors.Select(e => e.Target is null ? e.Code : $"{e.Code} ({e.Target})")));

    public static Guid? ParseId(JsonElement input, string name) =>
        input.TryGetProperty(name, out JsonElement value) && Guid.TryParse(value.GetString(), out Guid id) ? id : null;
}

/// <summary>Cherche un client du dossier par son nom, pour retrouver l'identifiant à mettre sur le brouillon.</summary>
internal sealed class FindCustomerTool(IQueryDispatcher queries) : IAiTool
{
    public string Name => "chercher_client";

    public string Description =>
        "Cherche un client existant du dossier par son nom (ou une partie). Renvoie au plus dix clients avec leur client_id. " +
        "Ne crée jamais de client : si aucun ne correspond, dis-le à l'utilisateur.";

    public AssistantToolRisk Risk => AssistantToolRisk.ReadOnly;

    public Permission? RequiredPermission => Permission.TiersConsulter;

    public IReadOnlySet<AssistantFeature> Features { get; } = new HashSet<AssistantFeature> { AssistantFeature.DraftFromText };

    public bool ReceivesRealValues => true;

    public JsonElement InputSchema { get; } = Schema.Object(("recherche", Schema.String("Nom du client, ou une partie du nom"), true)).ToElement();

    public bool IsEnabledBy(AiSettings settings) => true;

    public async Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        string search = Truncate(input.GetProperty("recherche").GetString(), ListCustomersQuery.MaxSearchLength);
        Result<PagedResponse<CustomerSummary>> found = await queries
            .QueryAsync(new ListCustomersQuery(context.Dossier, 1, BillingToolSupport.MaxResults, search), cancellationToken)
            .ConfigureAwait(false);
        return found.IsFailure
            ? BillingToolSupport.Failure(found.Errors)
            : AiToolResult.Json(found.Value.Items.Select(c => new { client_id = c.Id, nom = c.Name, ville = c.City }));
    }

    internal static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];
}

/// <summary>Cherche un article du catalogue, pour reprendre sa désignation, son prix et son taux de TVA.</summary>
internal sealed class FindCatalogItemTool(IQueryDispatcher queries) : IAiTool
{
    public string Name => "chercher_article";

    public string Description =>
        "Cherche un article du catalogue du dossier (référence ou désignation). Renvoie au plus dix articles avec article_id, " +
        "désignation, unité, prix unitaire HT et code de taux de TVA. Reprends ces valeurs telles quelles dans le brouillon.";

    public AssistantToolRisk Risk => AssistantToolRisk.ReadOnly;

    public Permission? RequiredPermission => Permission.CatalogueConsulter;

    public IReadOnlySet<AssistantFeature> Features { get; } = new HashSet<AssistantFeature> { AssistantFeature.DraftFromText };

    public bool ReceivesRealValues => true;

    public JsonElement InputSchema { get; } = Schema.Object(("recherche", Schema.String("Référence ou désignation de l'article"), true)).ToElement();

    public bool IsEnabledBy(AiSettings settings) => true;

    public async Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        string search = FindCustomerTool.Truncate(input.GetProperty("recherche").GetString(), ListCatalogItemsQuery.MaxSearchLength);
        Result<PagedResponse<CatalogItemSummary>> found = await queries
            .QueryAsync(new ListCatalogItemsQuery(context.Dossier, search, IncludeArchived: false, 1, BillingToolSupport.MaxResults), cancellationToken)
            .ConfigureAwait(false);
        return found.IsFailure
            ? BillingToolSupport.Failure(found.Errors)
            : AiToolResult.Json(found.Value.Items.Select(i => new
            {
                article_id = i.Id,
                reference = i.Reference,
                designation = i.Designation,
                unite = i.UnitCode,
                prix_unitaire_ht = BillingToolSupport.Amount(i.UnitPriceExclTax),
                taux_tva = i.VatRateCode,
            }));
    }
}

/// <summary>
/// Point 1 : saisie en langage naturel. Crée un devis <b>brouillon</b> par <see cref="CreateQuoteCommand"/>, avec les
/// droits de l'utilisateur (<see cref="Permission.DevisCreer"/>). Les totaux sont calculés par Lettra, pas par le modèle.
/// </summary>
internal sealed class CreateQuoteDraftTool(ICommandDispatcher commands, IQueryDispatcher queries) : IAiTool
{
    public const string ToolName = "creer_brouillon_devis";

    public string Name => ToolName;

    public string Description =>
        "Crée un devis BROUILLON (sans numéro, modifiable) à partir de la demande de l'utilisateur. client_id vient de " +
        "chercher_client, article_id et les prix de chercher_article quand l'article existe. L'utilisateur relit le brouillon " +
        "puis le valide lui-même : tu ne peux ni valider, ni envoyer, ni numéroter un devis. Les totaux renvoyés sont ceux de Lettra.";

    public AssistantToolRisk Risk => AssistantToolRisk.CreatesDraft;

    public Permission? RequiredPermission => Permission.DevisCreer;

    public IReadOnlySet<AssistantFeature> Features { get; } = new HashSet<AssistantFeature> { AssistantFeature.DraftFromText };

    public bool ReceivesRealValues => true;

    public JsonElement InputSchema { get; } = Schema.Object(
        ("client_id", Schema.String("Identifiant du client, renvoyé par chercher_client"), true),
        ("objet", Schema.String("Objet du devis, en une ligne"), false),
        ("lignes", Schema.Array(
            Schema.Object(
                ("designation", Schema.String("Désignation de la ligne"), true),
                ("quantite", Schema.Number("Quantité"), true),
                ("prix_unitaire_ht", Schema.Number("Prix unitaire HT en euros"), true),
                ("taux_tva", Schema.String("Code du taux de TVA", BillingToolSupport.VatRates), true),
                ("article_id", Schema.String("Article du catalogue, s'il existe"), false),
                ("unite", Schema.String("Code d'unité CEE-ONU (C62 : unité, HUR : heure, DAY : jour)"), false)),
            "Lignes du devis",
            minItems: 1), true)).ToElement();

    public bool IsEnabledBy(AiSettings settings) => true;

    public async Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (BillingToolSupport.ParseId(input, "client_id") is not { } customerId)
        {
            return AiToolResult.Error("client_id invalide : utilise chercher_client pour trouver le client.");
        }

        SaveQuoteRequest request = new()
        {
            CustomerId = customerId,
            Subject = input.TryGetProperty("objet", out JsonElement subject) ? FindCustomerTool.Truncate(subject.GetString(), 200) : null,
            Lines = [.. input.GetProperty("lignes").EnumerateArray().Select(ToLine)],
        };

        Result<QuoteId> created = await commands.SendAsync(new CreateQuoteCommand(context.Dossier, request), cancellationToken).ConfigureAwait(false);
        if (created.IsFailure)
        {
            return BillingToolSupport.Failure(created.Errors);
        }

        Result<QuoteResponse> quote = await queries.QueryAsync(new GetQuoteQuery(context.Dossier, created.Value), cancellationToken).ConfigureAwait(false);
        if (quote.IsFailure)
        {
            return BillingToolSupport.Failure(quote.Errors);
        }

        QuoteResponse draft = quote.Value;
        string summary = string.Format(
            CultureInfo.InvariantCulture,
            "{0} · {1} ligne(s) · {2} € HT · {3} € TTC",
            draft.Customer.Name,
            draft.Lines.Count,
            BillingToolSupport.Amount(draft.Totals.TotalExclTax),
            BillingToolSupport.Amount(draft.Totals.TotalInclTax));
        return AiToolResult.Json(new
        {
            devis_id = draft.Id,
            statut = "brouillon",
            client = draft.Customer.Name,
            lignes = draft.Lines.Select(l => new { l.Designation, quantite = l.Quantity, prix_unitaire_ht = BillingToolSupport.Amount(l.UnitPriceExclTax), taux_tva = l.VatRateCode }),
            total_ht = BillingToolSupport.Amount(draft.Totals.TotalExclTax),
            total_tva = BillingToolSupport.Amount(draft.Totals.TotalVat),
            total_ttc = BillingToolSupport.Amount(draft.Totals.TotalInclTax),
            a_faire = "L'utilisateur doit relire le brouillon puis le valider lui-même dans l'écran du devis.",
        }) with
        {
            Draft = new DraftCreated(draft.Id, "quote", summary),
        };
    }

    private static DocumentLineRequest ToLine(JsonElement line) => new()
    {
        CatalogItemId = BillingToolSupport.ParseId(line, "article_id"),
        Designation = FindCustomerTool.Truncate(line.GetProperty("designation").GetString(), 200),
        Quantity = line.GetProperty("quantite").GetDecimal(),
        UnitPriceExclTax = line.GetProperty("prix_unitaire_ht").GetDecimal(),
        VatRateCode = line.GetProperty("taux_tva").GetString() ?? "FR-20",
        UnitCode = line.TryGetProperty("unite", out JsonElement unit) && !string.IsNullOrWhiteSpace(unit.GetString()) ? unit.GetString()! : "C62",
    };
}

/// <summary>
/// Point 3 : « Pourquoi ? ». Relit la checklist de conformité du <b>document ouvert à l'écran</b>, et de lui seul
/// (minimisation, annexe F §10). Le constat vient des règles C# : le modèle l'explique, il ne le décide pas.
/// </summary>
internal sealed class CheckOpenDocumentTool(IQueryDispatcher queries) : IAiTool
{
    public const string ToolName = "controler_document_ouvert";

    public string Name => ToolName;

    public string Description =>
        "Relit la checklist de conformité (mentions obligatoires, TVA, numérotation) du document ouvert à l'écran : facture, " +
        "avoir ou devis. Chaque constat a un code, une gravité (Error bloque la validation, Warning alerte) et sa base légale. " +
        "Explique le constat demandé et ce qu'il faut corriger ; tu ne peux rien corriger ni valider toi-même.";

    public AssistantToolRisk Risk => AssistantToolRisk.ReadOnly;

    public Permission? RequiredPermission => Permission.DossierLire;

    public IReadOnlySet<AssistantFeature> Features { get; } = new HashSet<AssistantFeature> { AssistantFeature.ExplainCompliance, AssistantFeature.EInvoicingDiagnosis };

    public bool ReceivesRealValues => true;

    public JsonElement InputSchema { get; } = new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
        ["required"] = new JsonArray(),
        ["additionalProperties"] = false,
    }.ToElement();

    public bool IsEnabledBy(AiSettings settings) => true;

    public async Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.OpenDocumentId is not { } id)
        {
            return AiToolResult.Error("Aucun document n'est ouvert à l'écran.");
        }

        Result<ComplianceReportResponse> report = await queries.QueryAsync(new CheckInvoiceQuery(context.Dossier, new InvoiceId(id)), cancellationToken).ConfigureAwait(false);
        string kind = "facture";
        if (report.IsFailure && report.Errors.Any(e => e.Code == InvoiceErrors.NotFoundCode))
        {
            report = await queries.QueryAsync(new CheckQuoteQuery(context.Dossier, new QuoteId(id)), cancellationToken).ConfigureAwait(false);
            kind = "devis";
        }

        if (report.IsFailure)
        {
            return BillingToolSupport.Failure(report.Errors);
        }

        return AiToolResult.Json(new
        {
            document = kind,
            bloque_la_validation = report.Value.BlocksValidation,
            constats = report.Value.Findings
                .Where(f => f.Severity != ComplianceSeverityDto.Ok)
                .Select(f => new { code = f.Code, gravite = f.Severity.ToString(), base_legale = f.LegalBasis, ligne = f.Line }),
        }) with
        {
            IsDeterministicRule = true,
        };
    }
}
