using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Insights;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Exports.Application.Fec;
using Lettra.Exports.Application.Insights;
using Microsoft.AspNetCore.Mvc;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Éclairages du dossier (module Exports) : des lectures calculées en C# déterministe sur les pièces et les encaissements,
/// sans rien écrire : la météo des paiements (un bulletin par client et la prévision des encaissements), le contrôle
/// fiscal d'entraînement (les tests d'un vérificateur sur le FEC du dossier ou sur celui d'un autre logiciel), la
/// preuve d'authenticité des factures (signée par la clé du dossier, vérifiable hors ligne) et le dossier tel qu'il
/// était à un instant passé (rejoué depuis le journal inaltérable).
/// </summary>
internal static class InsightEndpoints
{
    public static RouteGroupBuilder MapInsights(this RouteGroupBuilder api)
    {
        RouteGroupBuilder insights = api.MapGroup("/insights").WithTags("Insights");

        insights.MapGet("/payment-weather", (ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                QueryAsync(queries, new GetPaymentWeatherQuery(tenant.Current.DossierId), ct))
            .RequirePermission(Permission.DossierLire)
            .WithName("GetPaymentWeather")
            .Produces<PaymentWeatherResponse>();

        insights.MapGet("/practice-audit", (DateOnly from, DateOnly to, ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                QueryAsync(queries, new GetPracticeAuditQuery(tenant.Current.DossierId, from, to), ct))
            .RequirePermission(Permission.FecExporter)
            .WithName("GetPracticeAudit")
            .Produces<PracticeAuditResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        insights.MapPost("/practice-audit", RunPracticeAuditAsync)
            .RequirePermission(Permission.FecExporter)
            .WithName("RunPracticeAudit")
            .WithMetadata(new RequestSizeLimitAttribute(ValidateFecQuery.MaxContentLength))
            .Produces<PracticeAuditResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        insights.MapGet("/invoice-proof/{invoiceId:guid}", (Guid invoiceId, ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                QueryAsync(queries, new GetInvoiceProofQuery(tenant.Current.DossierId, new InvoiceId(invoiceId)), ct))
            .RequirePermission(Permission.DossierLire)
            .WithName("GetInvoiceProof")
            .Produces<InvoiceProofResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        insights.MapPost("/invoice-proof/verification", (VerifyInvoiceProofRequest request, ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                QueryAsync(queries, new VerifyInvoiceProofQuery(tenant.Current.DossierId, request.Code), ct))
            .RequirePermission(Permission.DossierLire)
            .WithName("VerifyInvoiceProof")
            .Produces<InvoiceProofCheckResponse>();

        insights.MapGet("/invoice-proof/keys", (ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                QueryAsync(queries, new ListInvoiceProofKeysQuery(tenant.Current.DossierId), ct))
            .RequirePermission(Permission.DossierLire)
            .WithName("ListInvoiceProofKeys")
            .Produces<IReadOnlyList<InvoiceProofKeyResponse>>();

        insights.MapGet("/as-of", (DateTimeOffset at, ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                QueryAsync(queries, new GetDossierAsOfQuery(tenant.Current.DossierId, at), ct))
            .RequirePermission(Permission.DossierLire)
            .WithName("GetDossierAsOf")
            .Produces<DossierAsOfResponse>();

        return insights;
    }

    /// <summary>Le FEC d'un autre logiciel, envoyé tel quel dans le corps de la requête.</summary>
    private static async Task<IResult> RunPracticeAuditAsync(
        string fileName, HttpRequest request, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken, DateOnly? from = null, DateOnly? to = null)
    {
        using MemoryStream content = new();
        await request.Body.CopyToAsync(content, cancellationToken).ConfigureAwait(false);
        return await QueryAsync(
            queries, new RunPracticeAuditQuery(tenant.Current.DossierId, fileName, content.GetBuffer().AsMemory(0, (int)content.Length), from, to), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> QueryAsync<T>(IQueryDispatcher queries, IQuery<Result<T>> query, CancellationToken cancellationToken)
    {
        Result<T> result = await queries.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }
}
