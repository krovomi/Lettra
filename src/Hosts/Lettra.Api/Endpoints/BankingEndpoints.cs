using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.DemoData;
using Lettra.Domain.Common;
using Lettra.Exports.Application.Banking;
using Lettra.Exports.Application.Banking.Statements;
using Microsoft.AspNetCore.Mvc;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Encaissements, rapprochement bancaire et lettrage (module Exports, vague 6) : saisie et contre-passation des
/// encaissements, import de relevés (CAMT.053, OFX, CSV), propositions de rapprochement et leur validation, réglages,
/// état du rapprochement pour la vue cabinet. Aucune connexion bancaire : les relevés viennent de fichiers.
/// </summary>
internal static class BankingEndpoints
{
    /// <summary>Un relevé de 5 Mo encodé en base64 dans du JSON, avec de la marge.</summary>
    private const long MaxImportBodyLength = (BankStatementParser.MaxContentLength / 3 * 4) + (64 * 1024);

    public static RouteGroupBuilder MapBanking(this RouteGroupBuilder api)
    {
        RouteGroupBuilder payments = api.MapGroup("/payments").WithTags("Banking");

        payments.MapGet("/", ListPaymentsAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("ListPayments")
            .Produces<PagedResponse<PaymentSummary>>();

        payments.MapGet("/open-invoices", GetOpenInvoicesAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetOpenInvoices")
            .Produces<IReadOnlyList<OpenInvoiceResponse>>();

        payments.MapGet("/{id:guid}", GetPaymentAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetPayment")
            .Produces<PaymentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        payments.MapPost("/", RecordPaymentAsync)
            .RequirePermission(Permission.FactureEmettre)
            .WithName("RecordPayment")
            .Produces<PaymentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        payments.MapPost("/{id:guid}/reversal", ReversePaymentAsync)
            .RequirePermission(Permission.FactureEmettre)
            .WithName("ReversePayment")
            .Produces<PaymentResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        RouteGroupBuilder bank = api.MapGroup("/bank").WithTags("Banking");

        bank.MapPost("/statements", ImportStatementAsync)
            .RequirePermission(Permission.FactureEmettre)
            .WithName("ImportBankStatement")
            .WithMetadata(new RequestSizeLimitAttribute(MaxImportBodyLength))
            .Produces<BankImportResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // Démo : un relevé fictif fabriqué à partir des factures dues, dans les mêmes conditions que le jeu de démonstration.
        bank.MapPost("/demo-statement", ImportDemoStatementAsync)
            .RequirePermission(Permission.FactureEmettre)
            .WithName("ImportDemoBankStatement")
            .Produces<BankImportResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        bank.MapGet("/reconciliation", GetReconciliationAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetReconciliation")
            .Produces<ReconciliationResponse>();

        bank.MapPost("/reconciliation/confirm", ConfirmMatchesAsync)
            .RequirePermission(Permission.FactureEmettre)
            .WithName("ConfirmMatches")
            .Produces<ConfirmMatchesResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        bank.MapPost("/lines/{id:guid}/ignore", IgnoreLineAsync)
            .RequirePermission(Permission.FactureEmettre)
            .WithName("IgnoreBankLine")
            .Produces<BankLineResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        bank.MapGet("/settings", GetSettingsAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetBankingSettings")
            .Produces<BankingSettingsResponse>();

        bank.MapPut("/settings", UpdateSettingsAsync)
            .RequirePermission(Permission.CatalogueGerer)
            .WithName("UpdateBankingSettings")
            .Produces<BankingSettingsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        bank.MapGet("/status", GetStatusAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetBankReconciliationStatus")
            .Produces<BankReconciliationStatusResponse>();

        return bank;
    }

    private static async Task<IResult> ListPaymentsAsync(
        ITenantContext tenant,
        IQueryDispatcher queries,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null,
        Guid? customerId = null,
        string? search = null,
        int page = 1,
        int pageSize = 50,
        PaymentSortDto sort = PaymentSortDto.Date,
        SortDirectionDto direction = SortDirectionDto.Descending,
        decimal? minAmount = null,
        decimal? maxAmount = null) =>
        Ok(await queries.QueryAsync(
            new ListPaymentsQuery(
                tenant.Current.DossierId, from, to, customerId, ListSearch.Normalize(search), page, pageSize,
                sort, direction, minAmount, maxAmount),
            cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetOpenInvoicesAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken, Guid? customerId = null) =>
        Ok(await queries.QueryAsync(new GetOpenInvoicesQuery(tenant.Current.DossierId, customerId), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetPaymentAsync(Guid id, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new GetPaymentQuery(tenant.Current.DossierId, id), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RecordPaymentAsync(RecordPaymentRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result<PaymentResponse> result = await commands.SendAsync(new RecordPaymentCommand(tenant.Current.DossierId, request), cancellationToken).ConfigureAwait(false);
        return Created(p => $"/api/v1/payments/{p.Id}", result);
    }

    private static async Task<IResult> ReversePaymentAsync(
        Guid id, ReversePaymentRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(new ReversePaymentCommand(tenant.Current.DossierId, id, request), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ImportStatementAsync(
        ImportBankStatementRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(new ImportBankStatementCommand(tenant.Current.DossierId, request), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ImportDemoStatementAsync(
        IConfiguration configuration, IHostEnvironment environment, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        if (!LettraDemoData.IsAvailable(configuration, environment.EnvironmentName))
        {
            return TypedResults.NotFound();
        }

        return Ok(await commands.SendAsync(new ImportDemoBankStatementCommand(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> GetReconciliationAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken, int max = 500) =>
        Ok(await queries.QueryAsync(new GetReconciliationQuery(tenant.Current.DossierId, max), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ConfirmMatchesAsync(
        ConfirmMatchesRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(new ConfirmMatchesCommand(tenant.Current.DossierId, request), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> IgnoreLineAsync(
        Guid id, IgnoreBankLineRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(new IgnoreBankLineCommand(tenant.Current.DossierId, id, request), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetSettingsAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new GetBankingSettingsQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> UpdateSettingsAsync(
        UpdateBankingSettingsRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(new UpdateBankingSettingsCommand(tenant.Current.DossierId, request), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetStatusAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new GetBankReconciliationStatusQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false));
}
