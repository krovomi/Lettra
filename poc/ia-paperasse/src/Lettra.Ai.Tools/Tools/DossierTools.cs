using System.Text.Json;
using Lettra.Ai.Abstractions;
using Lettra.Compliance.Invoices;
using Lettra.Compliance.Mentions;
using Lettra.Poc.Application;

namespace Lettra.Ai.Tools.Tools;

/// <summary>Donne le company.json virtuel du dossier (paramètres de l'émetteur).</summary>
public sealed class DossierContextTool(IDossierContextProvider provider) : IAiTool
{
    public string Name => "contexte_dossier";
    public string Description =>
        "Renvoie le company.json du dossier courant (émetteur, régime TVA, numérotation, plateforme agréée, conditions de paiement).";
    public AiToolRisk Risk => AiToolRisk.ReadOnly;
    public string RequiredPermission => AiPermissions.UseAssistant;
    public IReadOnlySet<AiFeature> Features { get; } = Enum.GetValues<AiFeature>().ToHashSet();
    public JsonElement InputSchema { get; } = Schema.Object().ToElement();

    public async Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken ct = default) =>
        new(await provider.GetCompanyJsonAsync(context.DossierId, ct));
}

/// <summary>Contrôle déterministe des mentions obligatoires de la facture ouverte (même code que l'écran).</summary>
public sealed class ValidateInvoiceTool(DossierUseCases useCases) : IAiTool
{
    public string Name => "valider_facture";
    public string Description =>
        "Vérifie les mentions obligatoires de la facture ouverte à l'écran (CGI art. 242 nonies A, C. com. L441-9, réforme 2026) " +
        "et calcule ses totaux. Le résultat vient des règles de Lettra : il fait foi. Ne modifie rien.";
    public AiToolRisk Risk => AiToolRisk.ReadOnly;
    public string RequiredPermission => AiPermissions.ReadInvoices;
    public IReadOnlySet<AiFeature> Features { get; } = new HashSet<AiFeature> { AiFeature.ExplainCompliance, AiFeature.RegulatoryQuestions };
    public JsonElement InputSchema { get; } = Schema.Object(
        ("facture_id", Schema.String("Identifiant de la facture ouverte (fourni dans le contexte)"), true)).ToElement();

    public Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken ct = default)
    {
        if (!Guid.TryParse(input.GetProperty("facture_id").GetString(), out var id))
            return Task.FromResult(AiToolResult.Error("facture_id invalide"));
        // Minimisation : seule la facture ouverte à l'écran est lisible par l'assistant.
        if (context.OpenInvoiceId != id)
            return Task.FromResult(AiToolResult.Error("Seule la facture ouverte à l'écran peut être lue."));

        var check = useCases.CheckInvoiceCompliance(context.DossierId, id);
        return Task.FromResult(AiToolResult.Json(new
        {
            numero = check.InvoiceNumber,
            statut = check.Report.Status,
            bloque_la_validation = check.Report.BlocksValidation,
            erreurs = check.Report.Errors,
            avertissements = check.Report.Warnings,
            mentions_presentes = check.Report.Ok.Select(o => o.Label),
            totaux = Totals(check.Totals),
        }) with { IsDeterministicRule = true });
    }

    internal static object Totals(InvoiceTotals t) => new
    {
        total_ht = t.TotalExcludingVat,
        total_tva = t.TotalVat,
        total_ttc = t.TotalIncludingVat,
        par_taux = t.ByRate.Select(b => new { taux = b.Rate, base_ht = b.TaxableAmount, tva = b.VatAmount }),
        franchise_tva = t.IsVatFranchise,
    };
}

public sealed class SearchClientTool(DossierUseCases useCases) : IAiTool
{
    public string Name => "rechercher_client";
    public string Description => "Cherche un client du dossier par son nom (ou son jeton, ex. [CLIENT_1]) et renvoie son identifiant.";
    public AiToolRisk Risk => AiToolRisk.ReadOnly;
    public string RequiredPermission => AiPermissions.ReadInvoices;
    public IReadOnlySet<AiFeature> Features { get; } = new HashSet<AiFeature> { AiFeature.DraftFromText };
    public JsonElement InputSchema { get; } = Schema.Object(("nom", Schema.String("Nom du client"), true)).ToElement();

    public Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken ct = default)
    {
        var found = useCases.SearchClients(context.DossierId, input.GetProperty("nom").GetString() ?? "");
        return Task.FromResult(AiToolResult.Json(found.Select(c => new
        {
            client_id = c.Id,
            nom = c.Name,
            pays = c.Country,
            professionnel = c.Siren is not null || c.VatNumber is not null,
        })));
    }
}

/// <summary>Crée un brouillon de devis : jamais validé, jamais envoyé. L'utilisateur l'accepte ou le refuse.</summary>
public sealed class DraftQuoteTool(DossierUseCases useCases) : IAiTool
{
    public string Name => "brouillon_devis";
    public string Description =>
        "Prépare un BROUILLON de devis pour un client du dossier. Les totaux sont calculés par Lettra. " +
        "Le brouillon doit être relu et validé par l'utilisateur ; tu ne peux ni le valider ni l'envoyer.";
    public AiToolRisk Risk => AiToolRisk.CreatesDraft;
    public string RequiredPermission => AiPermissions.DraftQuotes;
    public IReadOnlySet<AiFeature> Features { get; } = new HashSet<AiFeature> { AiFeature.DraftFromText };
    public JsonElement InputSchema { get; } = Schema.Object(
        ("client_id", Schema.String("Identifiant renvoyé par rechercher_client"), true),
        ("objet", Schema.String("Objet du devis"), true),
        ("lignes", Schema.Array(Schema.Object(
            ("designation", Schema.String("Désignation précise"), true),
            ("quantite", Schema.Number("Quantité"), true),
            ("prix_unitaire_ht", Schema.Number("Prix unitaire hors taxes en euros"), true),
            ("taux_tva", Schema.Number("Taux de TVA (0.20 pour 20 %), facultatif : sinon taux du dossier"), false)),
            "Lignes du devis"), true)).ToElement();

    public Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken ct = default)
    {
        if (!Guid.TryParse(input.GetProperty("client_id").GetString(), out var clientId))
            return Task.FromResult(AiToolResult.Error("client_id invalide : appelle d'abord rechercher_client."));

        var lines = input.GetProperty("lignes").EnumerateArray().Select(l => new QuoteDraftLine(
            l.GetProperty("designation").GetString()!,
            l.GetProperty("quantite").GetDecimal(),
            l.GetProperty("prix_unitaire_ht").GetDecimal(),
            l.TryGetProperty("taux_tva", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetDecimal() : null)).ToList();

        try
        {
            var draft = useCases.CreateQuoteDraft(
                new CreateQuoteDraftCommand(context.DossierId, clientId, input.GetProperty("objet").GetString()!, lines),
                new Actor(context.UserId, ActorKind.AssistantIA));
            var summary = $"Brouillon de devis « {draft.Subject} » : {draft.Totals.TotalIncludingVat:0.00} EUR TTC";
            return Task.FromResult(AiToolResult.Json(new
            {
                brouillon_id = draft.Id,
                statut = "Brouillon (à relire et valider par l'utilisateur)",
                totaux = ValidateInvoiceTool.Totals(draft.Totals),
            }) with { Draft = new DraftCreated(draft.Id, "Devis", summary), IsDeterministicRule = true });
        }
        catch (Exception e) when (e is ArgumentException or DocumentNotFoundException)
        {
            return Task.FromResult(AiToolResult.Error(e.Message));
        }
    }
}
