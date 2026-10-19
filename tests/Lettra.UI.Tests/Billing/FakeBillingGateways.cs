using Lettra.Calculations;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Pricing;
using Lettra.Exports.Application.EInvoicing;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Billing;

/// <summary>Données de référence et tiers communs aux tests des pièces.</summary>
internal static class BillingFixtures
{
    public static readonly BillingReferenceData Reference = new(
        [
            new VatRegimeOption(VatRegimeDto.Standard, VatCategoryDto.Standard, null),
            new VatRegimeOption(VatRegimeDto.Franchise, VatCategoryDto.Exempt, "TVA non applicable, art. 293 B du CGI"),
        ],
        [new VatExemptionReasonOption("VATEX-FR-261", "Exonération, article 261 du CGI")],
        [new VatRateOption("FR-20", 20m), new VatRateOption("FR-10", 10m), new VatRateOption("FR-5.5", 5.5m), new VatRateOption("FR-0", 0m)]);

    public static readonly PartySnapshotResponse Dupont = new("Dupont SARL", "3 rue des Lilas", null, "69003", "Lyon", "FR",
        "732829320", null, "FR44732829320", "SARL", 10000m, "RCS Lyon");

    public static readonly PartySnapshotResponse Cabinet = new("Cabinet Martin", "12 avenue Foch", "Bâtiment B", "75116", "Paris", "FR",
        "552032534", null, "FR16552032534", "SAS", 50000m, "RCS Paris");

    public static readonly Guid DupontId = Guid.Parse("0199f0aa-0000-7000-8000-000000000001");

    public static CompanyProfileResponse Company(bool franchise = false) => new(3, "Cabinet Martin", "SAS", 50000m, "552032534", null,
        "RCS Paris", "FR16552032534", "12 avenue Foch", "Bâtiment B", "75116", "Paris", "FR", franchise, false, 30, "30 jours fin de mois",
        10m, "Pas d'escompte", "FR7630006000011234567890189", "AGRIFRPP", DateTimeOffset.UnixEpoch);

    public static decimal Percent(string code) => Reference.VatRates.Single(r => r.Code == code).Percent;

    public static (List<DocumentLineResponse> Lines, DocumentTotalsResponse Totals) Compute(IEnumerable<DocumentLineRequest> requests, VatRegimeDto regime)
    {
        List<DocumentLineResponse> lines = [];
        int position = 1;
        foreach (DocumentLineRequest l in requests)
        {
            decimal percent = regime == VatRegimeDto.Standard && l.VatExemptionCode is null ? Percent(l.VatRateCode) : 0m;
            decimal total = LineAmounts.TotalExclTax(new LineInput(l.Quantity, l.UnitPriceExclTax, percent, l.DiscountPercent));
            lines.Add(new DocumentLineResponse(position++, l.CatalogItemId, l.Reference, l.Designation, l.Description, l.Kind, l.UnitCode,
                l.Quantity, l.UnitPriceExclTax, l.DiscountPercent, l.VatRateCode, Percent(l.VatRateCode),
                percent == 0m ? VatCategoryDto.Exempt : VatCategoryDto.Standard, l.VatExemptionCode, total));
        }

        DocumentTotals totals = DocumentCalculator.Compute(lines.Select(l =>
            new LineInput(l.Quantity, l.UnitPriceExclTax, l.VatCategory == VatCategoryDto.Standard ? l.VatRatePercent : 0m, l.DiscountPercent)));
        return (lines, new DocumentTotalsResponse(
            [.. totals.Breakdown.Select(b => new VatBreakdownResponse(b.VatRatePercent == 0m ? VatCategoryDto.Exempt : VatCategoryDto.Standard,
                b.VatRatePercent, b.BaseExclTax, b.VatAmount, null))],
            totals.TotalExclTax, totals.TotalVat, totals.TotalInclTax));
    }

    public static ComplianceReportResponse Ready { get; } = new(false, [new ComplianceFindingResponse("Mention.SellerName", ComplianceSeverityDto.Ok, "CGI ann. II art. 242 nonies A")]);

    public static ComplianceReportResponse Blocking { get; } = new(true,
    [
        new ComplianceFindingResponse("Mention.CustomerAddress", ComplianceSeverityDto.Error, "CGI ann. II art. 242 nonies A"),
        new ComplianceFindingResponse("Mention.LineDesignation", ComplianceSeverityDto.Error, "CGI ann. II art. 242 nonies A", 2),
        new ComplianceFindingResponse("Mention.SellerName", ComplianceSeverityDto.Ok, null),
    ]);
}

internal sealed class FakeBillingSettingsGateway : IBillingSettingsGateway
{
    public CompanyProfileResponse? Company { get; set; } = BillingFixtures.Company();

    /// <summary>Serveur injoignable (brouillons hors ligne, étape 4.5).</summary>
    public bool Offline { get; set; }

    public List<(SaveCompanyProfileRequest Request, long? Version)> Saves { get; } = [];

    public Task<OperationResult<BillingReferenceData>> GetReferenceDataAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Offline ? OperationResult<BillingReferenceData>.Failure(ErrorCodes.Api.Unreachable)
            : OperationResult<BillingReferenceData>.Success(BillingFixtures.Reference));

    public Task<OperationResult<CompanyProfileResponse>> GetCompanyProfileAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Offline ? OperationResult<CompanyProfileResponse>.Failure(ErrorCodes.Api.Unreachable)
            : Company is null
            ? OperationResult<CompanyProfileResponse>.Failure([new ErrorDetail(ErrorCodes.CompanyProfile.NotFound)])
            : OperationResult<CompanyProfileResponse>.Success(Company));

    public Task<OperationResult<CompanyProfileResponse>> SaveCompanyProfileAsync(SaveCompanyProfileRequest request, long? expectedVersion, CancellationToken cancellationToken)
    {
        Saves.Add((request, expectedVersion));
        Company = new CompanyProfileResponse((Company?.Version ?? 0) + 1, request.Name, request.LegalForm, request.ShareCapital, request.Siren,
            request.Siret, request.Rcs, request.VatNumber, request.AddressLine1, request.AddressLine2, request.PostalCode, request.City,
            request.CountryCode, request.VatFranchise, request.VatOnDebits, request.PaymentDays, request.PaymentTerms, request.LatePenaltyRate,
            request.EarlyPaymentDiscount, request.Iban, request.Bic, DateTimeOffset.UnixEpoch);
        return Task.FromResult(OperationResult<CompanyProfileResponse>.Success(Company));
    }
}

internal sealed class FakeQuotesGateway : IQuotesGateway
{
    public Dictionary<Guid, QuoteResponse> Quotes { get; } = [];

    public List<SaveQuoteRequest> Creations { get; } = [];

    public List<(Guid Id, SaveQuoteRequest Request, long? Version)> Updates { get; } = [];

    public List<Guid> Validations { get; } = [];

    public List<Guid> Checks { get; } = [];

    public ComplianceReportResponse Report { get; set; } = BillingFixtures.Ready;

    /// <summary>Simule une modification concurrente : la prochaine écriture échoue.</summary>
    public bool NextVersionMismatch { get; set; }

    /// <summary>Serveur injoignable (brouillons hors ligne, étape 4.5).</summary>
    public bool Offline { get; set; }

    public QuoteResponse Add(QuoteStatusDto status = QuoteStatusDto.Draft, string? number = null, params DocumentLineRequest[] lines)
    {
        (List<DocumentLineResponse> computed, DocumentTotalsResponse totals) = BillingFixtures.Compute(lines, VatRegimeDto.Standard);
        QuoteResponse quote = new(Guid.CreateVersion7(), 1, number, status, BillingFixtures.DupontId, BillingFixtures.Dupont,
            number is null ? null : new DateOnly(2026, 10, 15), new DateOnly(2026, 11, 15), false, VatRegimeDto.Standard, "Mission annuelle",
            null, computed, totals, [], 0m, null, Guid.Empty, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
        Quotes[quote.Id] = quote;
        return quote;
    }

    public List<BillingListQuery> ListQueries { get; } = [];

    public Task<OperationResult<PagedResponse<QuoteSummary>>> ListAsync(BillingListQuery query, QuoteStatusDto? status, CancellationToken cancellationToken)
    {
        ListQueries.Add(query);
        List<QuoteSummary> items = Quotes.Values
            .Where(q => status is null || q.Status == status)
            .Where(q => query.Search is null || (q.Number ?? string.Empty).Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || q.Customer.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .Select(q => new QuoteSummary(q.Id, q.Number, q.Status, q.CustomerId, q.Customer.Name, q.IssueDate, q.ValidUntil, q.IsExpired,
                q.Totals.TotalExclTax, q.Totals.TotalInclTax))
            .ToList();
        return Ok(new PagedResponse<QuoteSummary>(items, query.Page, query.PageSize, items.Count));
    }

    public Task<OperationResult<QuoteResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Offline ? Fail<QuoteResponse>(ErrorCodes.Api.Unreachable) : Quotes.TryGetValue(id, out QuoteResponse? quote) ? Ok(quote) : Fail<QuoteResponse>(ErrorCodes.Quote.NotFound);

    public Task<OperationResult<QuoteResponse>> CreateAsync(SaveQuoteRequest request, CancellationToken cancellationToken)
    {
        if (Offline)
        {
            return Fail<QuoteResponse>(ErrorCodes.Api.Unreachable);
        }

        Creations.Add(request with { Lines = [.. request.Lines] });
        QuoteResponse quote = Add() with { CustomerId = request.CustomerId };
        Quotes[quote.Id] = Apply(quote, request, 1);
        return Ok(Quotes[quote.Id]);
    }

    public Task<OperationResult<QuoteResponse>> UpdateAsync(Guid id, SaveQuoteRequest request, long? expectedVersion, CancellationToken cancellationToken)
    {
        if (Offline)
        {
            return Fail<QuoteResponse>(ErrorCodes.Api.Unreachable);
        }

        Updates.Add((id, request with { Lines = [.. request.Lines] }, expectedVersion));
        if (TakeMismatch() || Quotes[id].Version != expectedVersion)
        {
            return Fail<QuoteResponse>(ErrorCodes.Concurrency.VersionMismatch);
        }

        Quotes[id] = Apply(Quotes[id], request, Quotes[id].Version + 1);
        return Ok(Quotes[id]);
    }

    public Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        Ok(Quotes.Remove(id));

    public Task<OperationResult<ComplianceReportResponse>> CheckAsync(Guid id, CancellationToken cancellationToken)
    {
        Checks.Add(id);
        return Ok(Report);
    }

    public Task<OperationResult<QuoteResponse>> ValidateAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken)
    {
        Validations.Add(id);
        if (TakeMismatch())
        {
            return Fail<QuoteResponse>(ErrorCodes.Concurrency.VersionMismatch);
        }

        Quotes[id] = Quotes[id] with { Status = QuoteStatusDto.Validated, Number = "DEV-2026-0007", IssueDate = new DateOnly(2026, 10, 15), Version = Quotes[id].Version + 1 };
        return Ok(Quotes[id]);
    }

    public Task<OperationResult<QuoteResponse>> SetOutcomeAsync(Guid id, bool accepted, long? expectedVersion, CancellationToken cancellationToken)
    {
        Quotes[id] = Quotes[id] with { Status = accepted ? QuoteStatusDto.Accepted : QuoteStatusDto.Refused, Version = Quotes[id].Version + 1 };
        return Ok(Quotes[id]);
    }

    public CreateDownPaymentRequest? LastDownPayment { get; private set; }

    public Task<OperationResult<InvoiceResponse>> CreateDownPaymentAsync(Guid id, CreateDownPaymentRequest request, CancellationToken cancellationToken)
    {
        LastDownPayment = request;
        return Ok(FakeInvoicesGateway.Build(InvoiceKindDto.DownPayment, InvoiceStatusDto.Draft, null));
    }

    public Task<OperationResult<InvoiceResponse>> CreateInvoiceAsync(Guid id, CancellationToken cancellationToken) =>
        Ok(FakeInvoicesGateway.Build(InvoiceKindDto.Invoice, InvoiceStatusDto.Draft, null) with { QuoteId = id, QuoteNumber = Quotes[id].Number });

    private bool TakeMismatch()
    {
        bool mismatch = NextVersionMismatch;
        NextVersionMismatch = false;
        return mismatch;
    }

    private static QuoteResponse Apply(QuoteResponse quote, SaveQuoteRequest request, long version)
    {
        (List<DocumentLineResponse> lines, DocumentTotalsResponse totals) = BillingFixtures.Compute(request.Lines, request.VatRegime);
        return quote with
        {
            Version = version, CustomerId = request.CustomerId, ValidUntil = request.ValidUntil, VatRegime = request.VatRegime,
            Subject = request.Subject, Notes = request.Notes, Lines = lines, Totals = totals,
        };
    }

    private static Task<OperationResult<T>> Ok<T>(T value) => Task.FromResult(OperationResult<T>.Success(value));

    private static Task<OperationResult<T>> Fail<T>(string code) => Task.FromResult(OperationResult<T>.Failure([new ErrorDetail(code)]));
}

internal sealed class FakeInvoicesGateway : IInvoicesGateway
{
    public Dictionary<Guid, InvoiceResponse> Invoices { get; } = [];

    public List<(Guid Id, CreateCreditNoteRequest Request)> CreditNotes { get; } = [];

    /// <summary>Serveur injoignable (brouillons hors ligne, étape 4.5).</summary>
    public bool Offline { get; set; }

    private static Task<OperationResult<InvoiceResponse>> Unreachable() =>
        Task.FromResult(OperationResult<InvoiceResponse>.Failure(ErrorCodes.Api.Unreachable));

    public static InvoiceResponse Build(InvoiceKindDto kind, InvoiceStatusDto status, string? number, params DocumentLineRequest[] lines)
    {
        (List<DocumentLineResponse> computed, DocumentTotalsResponse totals) = BillingFixtures.Compute(lines, VatRegimeDto.Standard);
        return new InvoiceResponse(Guid.CreateVersion7(), 1, kind, status, number, kind == InvoiceKindDto.CreditNote ? "AV" : "FAC",
            BillingFixtures.DupontId, BillingFixtures.Dupont, status == InvoiceStatusDto.Draft ? null : BillingFixtures.Cabinet,
            number is null ? null : new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 15), new DateOnly(2026, 11, 14), "30 jours", null,
            VatRegimeDto.Standard, OperationCategoryDto.Services, null, null, null, null, null, null, null, null, computed, totals, [], 0m,
            null, Guid.Empty, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null);
    }

    public InvoiceResponse Add(InvoiceKindDto kind, InvoiceStatusDto status, string? number, params DocumentLineRequest[] lines)
    {
        InvoiceResponse invoice = Build(kind, status, number, lines);
        Invoices[invoice.Id] = invoice;
        return invoice;
    }

    public List<(InvoiceKindDto? Kind, InvoiceStatusDto? Status, string? Search)> ListCalls { get; } = [];

    public List<BillingListQuery> ListQueries { get; } = [];

    public Task<OperationResult<PagedResponse<InvoiceSummary>>> ListAsync(BillingListQuery query, InvoiceKindDto? kind, InvoiceStatusDto? status, CancellationToken cancellationToken)
    {
        ListCalls.Add((kind, status, query.Search));
        ListQueries.Add(query);
        List<InvoiceSummary> items = Invoices.Values
            .Where(i => kind is null || i.Kind == kind)
            .Where(i => status is null || i.Status == status)
            .Where(i => query.Search is null || (i.Number ?? string.Empty).Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || i.Customer.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .Select(i => new InvoiceSummary(i.Id, i.Kind, i.Status, i.Number, i.CustomerId, i.Customer.Name, i.IssueDate, i.DueDate,
                i.Totals.TotalExclTax, i.Totals.TotalInclTax))
            .ToList();
        return Task.FromResult(OperationResult<PagedResponse<InvoiceSummary>>.Success(new PagedResponse<InvoiceSummary>(items, query.Page, query.PageSize, items.Count)));
    }

    public Task<OperationResult<InvoiceResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Invoices.TryGetValue(id, out InvoiceResponse? invoice)
            ? OperationResult<InvoiceResponse>.Success(invoice)
            : OperationResult<InvoiceResponse>.Failure([new ErrorDetail(ErrorCodes.Invoice.NotFound)]));

    public Task<OperationResult<InvoiceResponse>> CreateAsync(SaveInvoiceRequest request, CancellationToken cancellationToken)
    {
        if (Offline)
        {
            return Unreachable();
        }

        InvoiceResponse invoice = Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Draft, null, [.. request.Lines]);
        return Task.FromResult(OperationResult<InvoiceResponse>.Success(invoice));
    }

    public Task<OperationResult<InvoiceResponse>> UpdateAsync(Guid id, SaveInvoiceRequest request, long? expectedVersion, CancellationToken cancellationToken)
    {
        if (Offline)
        {
            return Unreachable();
        }

        (List<DocumentLineResponse> lines, DocumentTotalsResponse totals) = BillingFixtures.Compute(request.Lines, request.VatRegime);
        Invoices[id] = Invoices[id] with { Version = Invoices[id].Version + 1, Lines = lines, Totals = totals, PaymentTerms = request.PaymentTerms };
        return Task.FromResult(OperationResult<InvoiceResponse>.Success(Invoices[id]));
    }

    public Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<bool>.Success(Invoices.Remove(id)));

    public Task<OperationResult<ComplianceReportResponse>> CheckAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<ComplianceReportResponse>.Success(BillingFixtures.Ready));

    public Task<OperationResult<InvoiceResponse>> ValidateAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken)
    {
        if (CreditNotesNeedApproval && Invoices[id] is { Kind: InvoiceKindDto.CreditNote, CreditApprovedBy: null })
        {
            return Task.FromResult(OperationResult<InvoiceResponse>.Failure([new ErrorDetail(ErrorCodes.SegregationOfDuties.ManagerApprovalRequired)]));
        }

        Invoices[id] = Invoices[id] with { Status = InvoiceStatusDto.Validated, Number = "FAC-2026-0042", Version = Invoices[id].Version + 1 };
        return Task.FromResult(OperationResult<InvoiceResponse>.Success(Invoices[id]));
    }

    public Task<OperationResult<InvoiceResponse>> IssueAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken)
    {
        Invoices[id] = Invoices[id] with { Status = InvoiceStatusDto.Issued, Version = Invoices[id].Version + 1 };
        return Task.FromResult(OperationResult<InvoiceResponse>.Success(Invoices[id]));
    }

    public Task<OperationResult<InvoiceResponse>> CreateCreditNoteAsync(Guid id, CreateCreditNoteRequest request, CancellationToken cancellationToken)
    {
        CreditNotes.Add((id, request));
        InvoiceResponse credit = Add(InvoiceKindDto.CreditNote, InvoiceStatusDto.Draft, null) with
        {
            CreditedInvoiceId = id, CreditedInvoiceNumber = Invoices[id].Number, CreditReason = request.Reason, Lines = Invoices[id].Lines, Totals = Invoices[id].Totals,
        };
        Invoices[credit.Id] = credit;
        return Task.FromResult(OperationResult<InvoiceResponse>.Success(credit));
    }

    /// <summary>Séparation des tâches simulée : un avoir non approuvé est refusé à la validation.</summary>
    public bool CreditNotesNeedApproval { get; set; }

    /// <summary>Erreur renvoyée par l'approbation (approbateur auteur, permission manquante…), sinon approuvée.</summary>
    public string? ApprovalError { get; set; }

    public Task<OperationResult<InvoiceResponse>> ApproveCreditAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken)
    {
        if (ApprovalError is { } code)
        {
            return Task.FromResult(OperationResult<InvoiceResponse>.Failure([new ErrorDetail(code)]));
        }

        Invoices[id] = Invoices[id] with
        {
            CreditApprovedBy = Guid.CreateVersion7(), CreditApprovedAt = new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.Zero), Version = Invoices[id].Version + 1,
        };
        return Task.FromResult(OperationResult<InvoiceResponse>.Success(Invoices[id]));
    }

    /// <summary>Erreur renvoyée par la facture électronique (coupure réseau…), sinon le XML écrit par le module Exports.</summary>
    public string? EInvoiceError { get; set; }

    public Task<OperationResult<byte[]>> GetEInvoiceAsync(Guid id, EInvoiceFormatDto format, CancellationToken cancellationToken)
    {
        InvoiceResponse invoice = Invoices[id];
        string? error = EInvoiceError ?? (invoice.Number is null ? ErrorCodes.EInvoice.NotValidated : null);
        return Task.FromResult(error is null
            ? OperationResult<byte[]>.Success(EInvoiceSerializer.Write(invoice, BillingFixtures.Company(), format))
            : OperationResult<byte[]>.Failure(error));
    }
}

/// <summary>
/// Plateforme agréée simulée : dépôt (la pièce passe émise), statuts suivants à l'actualisation, encaissements cumulés
/// jusqu'au TTC. <see cref="NotConfigured"/> reproduit le réglage par défaut (aucune plateforme).
/// </summary>
internal sealed class FakePlatformTransmissionGateway(FakeInvoicesGateway invoices) : IPlatformTransmissionGateway
{
    private static readonly DateTimeOffset Depot = new(2026, 10, 15, 14, 0, 0, TimeSpan.Zero);

    public Dictionary<Guid, PlatformTransmissionResponse> Transmissions { get; } = [];

    public bool NotConfigured { get; set; }

    public List<ReportPaymentRequest> Payments { get; } = [];

    public PlatformTransmissionResponse Add(InvoiceResponse invoice, PlatformStatusDto status, decimal collected = 0m)
    {
        PlatformTransmissionResponse transmission = new(Guid.CreateVersion7(), invoice.Id, invoice.Number!, PlatformChannelDto.EInvoice, EInvoiceFormatDto.Cii,
            "Simulée", "PA-0001", status, status is PlatformStatusDto.Refused or PlatformStatusDto.Rejected, collected, Depot,
            [new PlatformStatusEventResponse(PlatformStatusDto.Deposited, Depot, null, null), .. status == PlatformStatusDto.Deposited ? Array.Empty<PlatformStatusEventResponse>() : [new PlatformStatusEventResponse(status, Depot.AddHours(1), status == PlatformStatusDto.Rejected ? "Doublon" : null, null)]]);
        Transmissions[invoice.Id] = transmission;
        return transmission;
    }

    public Task<OperationResult<PlatformTransmissionResponse>> GetAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        Task.FromResult(Transmissions.TryGetValue(invoiceId, out PlatformTransmissionResponse? found)
            ? OperationResult<PlatformTransmissionResponse>.Success(found)
            : OperationResult<PlatformTransmissionResponse>.Failure(ErrorCodes.Platform.NotTransmitted));

    public Task<OperationResult<PlatformTransmissionResponse>> TransmitAsync(Guid invoiceId, TransmitInvoiceRequest request, CancellationToken cancellationToken)
    {
        if (NotConfigured)
        {
            return Task.FromResult(OperationResult<PlatformTransmissionResponse>.Failure(ErrorCodes.Platform.NotConfigured));
        }

        InvoiceResponse invoice = invoices.Invoices[invoiceId];
        invoices.Invoices[invoiceId] = invoice with { Status = InvoiceStatusDto.Issued, Version = invoice.Version + 1 };
        Transmissions[invoiceId] = Add(invoice, PlatformStatusDto.Deposited) with { Format = request.Format };
        return Task.FromResult(OperationResult<PlatformTransmissionResponse>.Success(Transmissions[invoiceId]));
    }

    public Task<OperationResult<PlatformTransmissionResponse>> RefreshAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        PlatformTransmissionResponse current = Transmissions[invoiceId];
        Transmissions[invoiceId] = current with
        {
            Status = PlatformStatusDto.MadeAvailable,
            History = [.. current.History, new PlatformStatusEventResponse(PlatformStatusDto.Issued, Depot.AddMinutes(5), null, null),
                new PlatformStatusEventResponse(PlatformStatusDto.MadeAvailable, Depot.AddMinutes(10), null, null)],
        };
        return Task.FromResult(OperationResult<PlatformTransmissionResponse>.Success(Transmissions[invoiceId]));
    }

    public Task<OperationResult<PlatformTransmissionResponse>> ReportPaymentAsync(Guid invoiceId, ReportPaymentRequest request, CancellationToken cancellationToken)
    {
        PlatformTransmissionResponse current = Transmissions[invoiceId];
        decimal total = invoices.Invoices[invoiceId].Totals.TotalInclTax;
        if (request.Amount <= 0 || current.CollectedAmount + request.Amount > total)
        {
            return Task.FromResult(OperationResult<PlatformTransmissionResponse>.Failure([new ErrorDetail(ErrorCodes.Platform.PaymentAmountInvalid, "Amount")]));
        }

        Payments.Add(request);
        decimal collected = current.CollectedAmount + request.Amount;
        Transmissions[invoiceId] = current with
        {
            Status = PlatformStatusDto.Collected,
            CollectedAmount = collected,
            IsFinal = collected == total,
            History = [.. current.History, new PlatformStatusEventResponse(PlatformStatusDto.Collected, Depot.AddDays(Payments.Count), null, request.Amount)],
        };
        return Task.FromResult(OperationResult<PlatformTransmissionResponse>.Success(Transmissions[invoiceId]));
    }
}

internal sealed class FakePriceListsGateway : IPriceListsGateway
{
    public Dictionary<Guid, ResolvedPrice> Prices { get; } = [];

    public Task<OperationResult<PagedResponse<PriceListSummary>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<PriceListResponse>> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult<PriceListResponse>> CreateAsync(SavePriceListRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult<PriceListResponse>> UpdateAsync(Guid id, SavePriceListRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult<IReadOnlyList<ResolvedPrice>>> ResolveAsync(Guid customerId, IReadOnlyCollection<Guid> catalogItemIds, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<IReadOnlyList<ResolvedPrice>>.Success(
            [.. catalogItemIds.Where(Prices.ContainsKey).Select(id => Prices[id])]));
}
