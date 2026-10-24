using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Pricing;
using Lettra.Application.Mapping;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Pricing;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Pricing;
using Lettra.Domain.SharedKernel;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Routes des devis, factures, acomptes, avoirs, de la fiche entreprise et des grilles tarifaires. Chaque pièce porte sa
/// version en <c>ETag</c> ; une écriture la renvoie en <c>If-Match</c>, obligatoire (428 sans, 412 si la pièce a changé
/// entre-temps, avec la pièce actuelle dans <c>current</c>, ADR-0013). Les écritures renvoient la pièce relue, comme la
/// gateway in-process.
/// </summary>
internal static class BillingEndpoints
{
    public static RouteGroupBuilder MapBilling(this RouteGroupBuilder api)
    {
        api.MapGet("/billing/reference-data", ReferenceDataAsync)
            .RequirePermission(Permission.DossierLire)
            .WithTags("Billing")
            .WithName("GetBillingReferenceData")
            .Produces<BillingReferenceData>();

        api.MapGet("/billing/dashboard", DashboardAsync)
            .RequirePermission(Permission.DossierLire)
            .WithTags("Billing")
            .WithName("GetBillingDashboard")
            .Produces<BillingDashboardResponse>();

        RouteGroupBuilder profile = api.MapGroup("/company-profile").WithTags("Billing");
        profile.MapGet("/", GetProfileAsync).RequirePermission(Permission.DossierLire).WithName("GetCompanyProfile")
            .Produces<CompanyProfileResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        profile.MapPut("/", SaveProfileAsync).RequirePermission(Permission.CatalogueGerer).WithName("SaveCompanyProfile")
            .Produces<CompanyProfileResponse>().ProducesProblem(StatusCodes.Status400BadRequest).RequireIfMatch();

        MapQuotes(api.MapGroup("/quotes").WithTags("Quotes"));
        MapInvoices(api.MapGroup("/invoices").WithTags("Invoices"));
        MapPriceLists(api.MapGroup("/price-lists").WithTags("PriceLists"));
        api.MapRecurringInvoices();
        return api;
    }

    private static void MapQuotes(RouteGroupBuilder group)
    {
        group.MapGet("/", ListQuotesAsync).RequirePermission(Permission.DossierLire).WithName("ListQuotes")
            .Produces<PagedResponse<QuoteSummary>>();
        group.MapGet("/{id:guid}", GetQuoteAsync).RequirePermission(Permission.DossierLire).WithName("GetQuote")
            .Produces<QuoteResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/", CreateQuoteAsync).RequirePermission(Permission.DevisCreer).WithName("CreateQuote")
            .Produces<QuoteResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapPut("/{id:guid}", UpdateQuoteAsync).RequirePermission(Permission.DevisCreer).WithName("UpdateQuote")
            .Produces<QuoteResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireIfMatch();
        group.MapDelete("/{id:guid}", DeleteQuoteAsync).RequirePermission(Permission.DevisCreer).WithName("DeleteQuote")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status409Conflict).RequireIfMatch();
        group.MapGet("/{id:guid}/compliance", CheckQuoteAsync).RequirePermission(Permission.DossierLire).WithName("CheckQuote")
            .Produces<ComplianceReportResponse>();
        group.MapPost("/{id:guid}/validate", ValidateQuoteAsync).RequirePermission(Permission.DevisCreer).WithName("ValidateQuote")
            .Produces<QuoteResponse>().ProducesProblem(StatusCodes.Status409Conflict).RequireIfMatch();
        group.MapPut("/{id:guid}/outcome", SetOutcomeAsync).RequirePermission(Permission.DevisCreer).WithName("SetQuoteOutcome")
            .Produces<QuoteResponse>().ProducesProblem(StatusCodes.Status409Conflict).RequireIfMatch();
        group.MapPost("/{id:guid}/down-payments", CreateDownPaymentAsync).RequirePermission(Permission.FactureSaisir).WithName("CreateDownPayment")
            .Produces<InvoiceResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/{id:guid}/invoice", CreateFinalInvoiceAsync).RequirePermission(Permission.FactureSaisir).WithName("CreateFinalInvoice")
            .Produces<InvoiceResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapInvoices(RouteGroupBuilder group)
    {
        group.MapGet("/", ListInvoicesAsync).RequirePermission(Permission.DossierLire).WithName("ListInvoices")
            .Produces<PagedResponse<InvoiceSummary>>();
        group.MapGet("/{id:guid}", GetInvoiceAsync).RequirePermission(Permission.DossierLire).WithName("GetInvoice")
            .Produces<InvoiceResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/", CreateInvoiceAsync).RequirePermission(Permission.FactureSaisir).WithName("CreateInvoice")
            .Produces<InvoiceResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapPut("/{id:guid}", UpdateInvoiceAsync).RequirePermission(Permission.FactureSaisir).WithName("UpdateInvoice")
            .Produces<InvoiceResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireIfMatch();
        group.MapDelete("/{id:guid}", DeleteInvoiceAsync).RequirePermission(Permission.FactureSaisir).WithName("DeleteInvoice")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status409Conflict).RequireIfMatch();
        group.MapGet("/{id:guid}/compliance", CheckInvoiceAsync).RequirePermission(Permission.DossierLire).WithName("CheckInvoice")
            .Produces<ComplianceReportResponse>();
        group.MapPost("/{id:guid}/validate", ValidateInvoiceAsync).RequirePermission(Permission.FactureValider).WithName("ValidateInvoice")
            .Produces<InvoiceResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireIfMatch();
        group.MapPost("/{id:guid}/issue", IssueInvoiceAsync).RequirePermission(Permission.FactureEmettre).WithName("IssueInvoice")
            .Produces<InvoiceResponse>().ProducesProblem(StatusCodes.Status409Conflict).RequireIfMatch();
        group.MapPost("/{id:guid}/approve-credit", ApproveCreditNoteAsync).RequirePermission(Permission.AvoirApprouver).WithName("ApproveCreditNote")
            .Produces<InvoiceResponse>().ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict).RequireIfMatch();
        group.MapPost("/{id:guid}/credit-notes", CreateCreditNoteAsync).RequirePermission(Permission.AvoirCreer).WithName("CreateCreditNote")
            .Produces<InvoiceResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapPriceLists(RouteGroupBuilder group)
    {
        group.MapGet("/", ListPriceListsAsync).RequirePermission(Permission.CatalogueConsulter).WithName("ListPriceLists")
            .Produces<PagedResponse<PriceListSummary>>();
        group.MapGet("/resolve", ResolvePricesAsync).RequirePermission(Permission.CatalogueConsulter).WithName("ResolvePrices")
            .Produces<IReadOnlyList<ResolvedPrice>>();
        group.MapGet("/{id:guid}", GetPriceListAsync).RequirePermission(Permission.CatalogueConsulter).WithName("GetPriceList")
            .Produces<PriceListResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/", CreatePriceListAsync).RequirePermission(Permission.CatalogueGerer).WithName("CreatePriceList")
            .Produces<PriceListResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapPut("/{id:guid}", UpdatePriceListAsync).RequirePermission(Permission.CatalogueGerer).WithName("UpdatePriceList")
            .Produces<PriceListResponse>().ProducesProblem(StatusCodes.Status400BadRequest).RequireIfMatch();
        group.MapDelete("/{id:guid}", DeletePriceListAsync).RequirePermission(Permission.CatalogueGerer).WithName("DeletePriceList")
            .Produces(StatusCodes.Status204NoContent);
    }

    // --- Paramétrage

    private static async Task<IResult> DashboardAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new GetBillingDashboardQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ReferenceDataAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new GetBillingReferenceDataQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetProfileAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Versioned(await queries.QueryAsync(new GetCompanyProfileQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false), p => p.Version);

    private static async Task<IResult> SaveProfileAsync(
        SaveCompanyProfileRequest request, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        Result saved = await commands.SendAsync(new SaveCompanyProfileCommand(dossier, request.ToDraft(), http.IfMatchVersion()), cancellationToken).ConfigureAwait(false);
        Task<Result<CompanyProfileResponse>> Read() => queries.QueryAsync(new GetCompanyProfileQuery(dossier), cancellationToken);
        return saved.IsFailure
            ? await ConcurrencyResults.WriteFailureAsync(saved.Errors, Read, p => p.Version).ConfigureAwait(false)
            : Versioned(await Read().ConfigureAwait(false), p => p.Version);
    }

    // --- Devis

    private static async Task<IResult> ListQuotesAsync(
        ITenantContext tenant,
        IQueryDispatcher queries,
        CancellationToken cancellationToken,
        string? search = null,
        Guid? customerId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        QuoteStatusDto? status = null,
        int page = 1,
        int pageSize = 50,
        BillingSortDto sort = BillingSortDto.IssueDate,
        SortDirectionDto direction = SortDirectionDto.Descending,
        decimal? minAmount = null,
        decimal? maxAmount = null) =>
        Ok(await queries.QueryAsync(
            new ListQuotesQuery(tenant.Current.DossierId, search, customerId, from, to, status, page, pageSize, sort, direction, minAmount, maxAmount), cancellationToken).ConfigureAwait(false));

    private static Task<IResult> GetQuoteAsync(Guid id, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        ReadQuoteAsync(queries, tenant.Current.DossierId, new QuoteId(id), cancellationToken);

    private static async Task<IResult> CreateQuoteAsync(
        SaveQuoteRequest request, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        Result<QuoteId> created = await commands.SendAsync(new CreateQuoteCommand(dossier, request), cancellationToken).ConfigureAwait(false);
        return created.IsFailure
            ? created.Errors.ToProblem()
            : Created($"/api/v1/quotes/{created.Value.Value}", await queries.QueryAsync(new GetQuoteQuery(dossier, created.Value), cancellationToken).ConfigureAwait(false), q => q.Version);
    }

    private static Task<IResult> UpdateQuoteAsync(
        Guid id, SaveQuoteRequest request, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        QuoteCommandAsync(new UpdateQuoteCommand(tenant.Current.DossierId, new QuoteId(id), request, http.IfMatchVersion()), commands, queries, cancellationToken);

    private static async Task<IResult> DeleteQuoteAsync(
        Guid id, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        QuoteId quoteId = new(id);
        Result deleted = await commands.SendAsync(new DeleteQuoteCommand(dossier, quoteId, http.IfMatchVersion()), cancellationToken).ConfigureAwait(false);
        return deleted.IsSuccess
            ? TypedResults.NoContent()
            : await ConcurrencyResults.WriteFailureAsync(deleted.Errors, () => queries.QueryAsync(new GetQuoteQuery(dossier, quoteId), cancellationToken), q => q.Version).ConfigureAwait(false);
    }

    private static async Task<IResult> CheckQuoteAsync(Guid id, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new CheckQuoteQuery(tenant.Current.DossierId, new QuoteId(id)), cancellationToken).ConfigureAwait(false));

    private static Task<IResult> ValidateQuoteAsync(
        Guid id, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        QuoteCommandAsync(new ValidateQuoteCommand(tenant.Current.DossierId, new QuoteId(id), http.IfMatchVersion()), commands, queries, cancellationToken);

    private static Task<IResult> SetOutcomeAsync(
        Guid id, SetQuoteOutcomeRequest request, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        QuoteCommandAsync(new SetQuoteOutcomeCommand(tenant.Current.DossierId, new QuoteId(id), request.Accepted, http.IfMatchVersion()), commands, queries, cancellationToken);

    private static async Task<IResult> CreateDownPaymentAsync(
        Guid id, CreateDownPaymentRequest request, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        Result<InvoiceId> created = await commands.SendAsync(new CreateDownPaymentCommand(dossier, new QuoteId(id), request), cancellationToken).ConfigureAwait(false);
        return await CreatedInvoiceAsync(queries, dossier, created, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> CreateFinalInvoiceAsync(
        Guid id, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        Result<InvoiceId> created = await commands.SendAsync(new CreateFinalInvoiceCommand(dossier, new QuoteId(id)), cancellationToken).ConfigureAwait(false);
        return await CreatedInvoiceAsync(queries, dossier, created, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> QuoteCommandAsync<TCommand>(
        TCommand command, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
        where TCommand : ICommand<Result>, IRequirePermission
    {
        Result done = await commands.SendAsync(command, cancellationToken).ConfigureAwait(false);
        QuoteId id = command switch
        {
            UpdateQuoteCommand c => c.QuoteId,
            ValidateQuoteCommand c => c.QuoteId,
            SetQuoteOutcomeCommand c => c.QuoteId,
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        Task<Result<QuoteResponse>> Read() => queries.QueryAsync(new GetQuoteQuery(DossierOf(command), id), cancellationToken);
        return done.IsFailure
            ? await ConcurrencyResults.WriteFailureAsync(done.Errors, Read, q => q.Version).ConfigureAwait(false)
            : Versioned(await Read().ConfigureAwait(false), q => q.Version);
    }

    private static async Task<IResult> ReadQuoteAsync(IQueryDispatcher queries, DossierId dossier, QuoteId id, CancellationToken cancellationToken) =>
        Versioned(await queries.QueryAsync(new GetQuoteQuery(dossier, id), cancellationToken).ConfigureAwait(false), q => q.Version);

    // --- Factures, acomptes, avoirs

    private static async Task<IResult> ListInvoicesAsync(
        ITenantContext tenant,
        IQueryDispatcher queries,
        CancellationToken cancellationToken,
        string? search = null,
        Guid? customerId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        InvoiceKindDto? kind = null,
        InvoiceStatusDto? status = null,
        int page = 1,
        int pageSize = 50,
        BillingSortDto sort = BillingSortDto.IssueDate,
        SortDirectionDto direction = SortDirectionDto.Descending,
        decimal? minAmount = null,
        decimal? maxAmount = null) =>
        Ok(await queries.QueryAsync(
            new ListInvoicesQuery(tenant.Current.DossierId, search, customerId, from, to, kind, status, page, pageSize, sort, direction, minAmount, maxAmount), cancellationToken).ConfigureAwait(false));

    private static Task<IResult> GetInvoiceAsync(Guid id, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        ReadInvoiceAsync(queries, tenant.Current.DossierId, new InvoiceId(id), cancellationToken);

    private static async Task<IResult> CreateInvoiceAsync(
        SaveInvoiceRequest request, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        Result<InvoiceId> created = await commands.SendAsync(new CreateInvoiceCommand(dossier, request), cancellationToken).ConfigureAwait(false);
        return await CreatedInvoiceAsync(queries, dossier, created, cancellationToken).ConfigureAwait(false);
    }

    private static Task<IResult> UpdateInvoiceAsync(
        Guid id, SaveInvoiceRequest request, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        InvoiceCommandAsync(new UpdateInvoiceCommand(tenant.Current.DossierId, new InvoiceId(id), request, http.IfMatchVersion()), commands, queries, cancellationToken);

    private static async Task<IResult> DeleteInvoiceAsync(
        Guid id, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        InvoiceId invoiceId = new(id);
        Result deleted = await commands.SendAsync(new DeleteInvoiceCommand(dossier, invoiceId, http.IfMatchVersion()), cancellationToken).ConfigureAwait(false);
        return deleted.IsSuccess
            ? TypedResults.NoContent()
            : await ConcurrencyResults.WriteFailureAsync(deleted.Errors, () => queries.QueryAsync(new GetInvoiceQuery(dossier, invoiceId), cancellationToken), i => i.Version).ConfigureAwait(false);
    }

    private static async Task<IResult> CheckInvoiceAsync(Guid id, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new CheckInvoiceQuery(tenant.Current.DossierId, new InvoiceId(id)), cancellationToken).ConfigureAwait(false));

    private static Task<IResult> ValidateInvoiceAsync(
        Guid id, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        InvoiceCommandAsync(new ValidateInvoiceCommand(tenant.Current.DossierId, new InvoiceId(id), http.IfMatchVersion()), commands, queries, cancellationToken);

    private static Task<IResult> IssueInvoiceAsync(
        Guid id, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        InvoiceCommandAsync(new IssueInvoiceCommand(tenant.Current.DossierId, new InvoiceId(id), http.IfMatchVersion()), commands, queries, cancellationToken);

    private static Task<IResult> ApproveCreditNoteAsync(
        Guid id, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        InvoiceCommandAsync(new ApproveCreditNoteCommand(tenant.Current.DossierId, new InvoiceId(id), http.IfMatchVersion()), commands, queries, cancellationToken);

    private static async Task<IResult> CreateCreditNoteAsync(
        Guid id, CreateCreditNoteRequest request, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        Result<InvoiceId> created = await commands.SendAsync(new CreateCreditNoteCommand(dossier, new InvoiceId(id), request), cancellationToken).ConfigureAwait(false);
        return await CreatedInvoiceAsync(queries, dossier, created, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> InvoiceCommandAsync<TCommand>(
        TCommand command, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
        where TCommand : ICommand<Result>, IRequirePermission, IInvoiceLifecycleCommand
    {
        Result done = await commands.SendAsync(command, cancellationToken).ConfigureAwait(false);
        Task<Result<InvoiceResponse>> Read() => queries.QueryAsync(new GetInvoiceQuery(DossierOf(command), command.Invoice), cancellationToken);
        return done.IsFailure
            ? await ConcurrencyResults.WriteFailureAsync(done.Errors, Read, i => i.Version).ConfigureAwait(false)
            : Versioned(await Read().ConfigureAwait(false), i => i.Version);
    }

    private static async Task<IResult> CreatedInvoiceAsync(IQueryDispatcher queries, DossierId dossier, Result<InvoiceId> created, CancellationToken cancellationToken) =>
        created.IsFailure
            ? created.Errors.ToProblem()
            : Created($"/api/v1/invoices/{created.Value.Value}", await queries.QueryAsync(new GetInvoiceQuery(dossier, created.Value), cancellationToken).ConfigureAwait(false), i => i.Version);

    private static async Task<IResult> ReadInvoiceAsync(IQueryDispatcher queries, DossierId dossier, InvoiceId id, CancellationToken cancellationToken) =>
        Versioned(await queries.QueryAsync(new GetInvoiceQuery(dossier, id), cancellationToken).ConfigureAwait(false), i => i.Version);

    // --- Grilles tarifaires

    private static async Task<IResult> ListPriceListsAsync(
        ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken, int page = 1, int pageSize = 50) =>
        Ok(await queries.QueryAsync(new ListPriceListsQuery(tenant.Current.DossierId, page, pageSize), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ResolvePricesAsync(
        Guid customerId, Guid[] itemId, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new ResolvePricesQuery(tenant.Current.DossierId, customerId, itemId), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetPriceListAsync(Guid id, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Versioned(await queries.QueryAsync(new GetPriceListQuery(tenant.Current.DossierId, new PriceListId(id)), cancellationToken).ConfigureAwait(false), l => l.Version);

    private static async Task<IResult> CreatePriceListAsync(
        SavePriceListRequest request, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        Result<PriceListId> created = await commands.SendAsync(new CreatePriceListCommand(dossier, request), cancellationToken).ConfigureAwait(false);
        return created.IsFailure
            ? created.Errors.ToProblem()
            : Created($"/api/v1/price-lists/{created.Value.Value}", await queries.QueryAsync(new GetPriceListQuery(dossier, created.Value), cancellationToken).ConfigureAwait(false), l => l.Version);
    }

    private static async Task<IResult> UpdatePriceListAsync(
        Guid id, SavePriceListRequest request, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        PriceListId listId = new(id);
        Result updated = await commands.SendAsync(new UpdatePriceListCommand(dossier, listId, request, http.IfMatchVersion()), cancellationToken).ConfigureAwait(false);
        Task<Result<PriceListResponse>> Read() => queries.QueryAsync(new GetPriceListQuery(dossier, listId), cancellationToken);
        return updated.IsFailure
            ? await ConcurrencyResults.WriteFailureAsync(updated.Errors, Read, l => l.Version).ConfigureAwait(false)
            : Versioned(await Read().ConfigureAwait(false), l => l.Version);
    }

    private static async Task<IResult> DeletePriceListAsync(Guid id, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        NoContent(await commands.SendAsync(new DeletePriceListCommand(tenant.Current.DossierId, new PriceListId(id)), cancellationToken).ConfigureAwait(false));

    // --- Aides

    private static DossierId DossierOf(IDossierScoped command) => command.Dossier;
}
