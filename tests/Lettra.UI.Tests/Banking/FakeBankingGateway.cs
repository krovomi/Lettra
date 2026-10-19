using Lettra.Contracts.Accounting;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Banking;

/// <summary>Encaissements fictifs : les tests posent les factures dues, les opérations du relevé et les encaissements.</summary>
internal sealed class FakeBankingGateway : IBankingGateway
{
    public List<OpenInvoiceResponse> Open { get; } = [];

    public List<BankLineResponse> Lines { get; } = [];

    public List<PaymentResponse> Payments { get; } = [];

    public List<RecordPaymentRequest> Recorded { get; } = [];

    public List<ConfirmMatchesRequest> Confirmations { get; } = [];

    public List<(Guid Id, ReversePaymentRequest Request)> Reversals { get; } = [];

    public decimal Threshold { get; set; } = 1m;

    public static OpenInvoiceResponse Invoice(string number, Guid customerId, string customerName, DateOnly due, decimal outstanding) =>
        new(Guid.CreateVersion7(), InvoiceKindDto.Invoice, number, customerId, customerName, due.AddDays(-30), due, outstanding, 0m, 0m, outstanding, 0);

    public static PaymentResponse Payment(string number, Guid customerId, string customerName, OpenInvoiceResponse invoice, string? letter = "A") =>
        new(Guid.CreateVersion7(), 1, number, PaymentStatusDto.Posted, customerId, customerName, new DateOnly(2026, 10, 10), invoice.Outstanding,
            PaymentMethodDto.Transfer, null, null, [new PaymentAllocationResponse(invoice.InvoiceId, invoice.Number, invoice.Outstanding)],
            invoice.Outstanding, 0m, letter,
            [new JournalLineResponse("512", invoice.Outstanding, 0m, null), new JournalLineResponse("411", 0m, invoice.Outstanding, null)],
            [], null, null, DateTimeOffset.UnixEpoch);

    public List<PaymentListQuery> PaymentQueries { get; } = [];

    public Task<OperationResult<PagedResponse<PaymentSummary>>> ListPaymentsAsync(PaymentListQuery query, CancellationToken cancellationToken)
    {
        PaymentQueries.Add(query);
        return Task.FromResult(OperationResult<PagedResponse<PaymentSummary>>.Success(new(
            [.. Payments.Select(p => new PaymentSummary(p.Id, p.Number, p.Status, p.CustomerId, p.CustomerName, p.Date, p.Amount, p.Method, p.Reference,
                [.. p.Allocations.Select(a => a.InvoiceNumber)], p.LetteringCode))],
            query.Page, query.PageSize, Payments.Count)));
    }

    public Task<OperationResult<PaymentResponse>> GetPaymentAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Payments.FirstOrDefault(p => p.Id == id) is { } payment
            ? OperationResult<PaymentResponse>.Success(payment)
            : OperationResult<PaymentResponse>.Failure(ErrorCodes.Payment.NotFound));

    public Task<OperationResult<IReadOnlyList<OpenInvoiceResponse>>> GetOpenInvoicesAsync(Guid? customerId, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<IReadOnlyList<OpenInvoiceResponse>>.Success([.. Open.Where(i => customerId is null || i.CustomerId == customerId)]));

    public Task<OperationResult<PaymentResponse>> RecordPaymentAsync(RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        Recorded.Add(request);
        OperationResult<PaymentResponse> result = request.Allocations.Sum(a => a.Amount) - request.Amount is var gap && Math.Abs(gap) > Threshold
            ? OperationResult<PaymentResponse>.Failure([new ErrorDetail(ErrorCodes.Payment.GapAboveThreshold, "Amount")])
            : OperationResult<PaymentResponse>.Success(Payment($"ENC-{Recorded.Count:D5}", request.CustomerId, "Client", Open.First(i => i.InvoiceId == request.Allocations[0].InvoiceId)));
        return Task.FromResult(result);
    }

    public Task<OperationResult<PaymentResponse>> ReversePaymentAsync(Guid id, ReversePaymentRequest request, CancellationToken cancellationToken)
    {
        Reversals.Add((id, request));
        PaymentResponse payment = Payments.First(p => p.Id == id);
        PaymentResponse reversed = payment with { Status = PaymentStatusDto.Reversed, ReversalDate = new DateOnly(2026, 10, 16), ReversalReason = request.Reason, LetteringCode = null };
        Payments[Payments.IndexOf(payment)] = reversed;
        return Task.FromResult(OperationResult<PaymentResponse>.Success(reversed));
    }

    public Task<OperationResult<BankImportResponse>> ImportStatementAsync(ImportBankStatementRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<BankImportResponse>.Success(new BankImportResponse(Guid.CreateVersion7(), BankStatementFormatDto.Camt053, null, null, null, 3, 0, 1)));

    public Task<OperationResult<BankImportResponse>> ImportDemoStatementAsync(CancellationToken cancellationToken) => ImportStatementAsync(new(), cancellationToken);

    public Task<OperationResult<ReconciliationResponse>> GetReconciliationAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<ReconciliationResponse>.Success(new ReconciliationResponse(
            [.. Lines],
            Lines.Count(l => l.Status == BankLineStatusDto.Pending),
            Lines.Count(l => l.Status == BankLineStatusDto.Matched),
            Lines.Count(l => l.Status == BankLineStatusDto.Ignored),
            Lines.Count(l => l.Status == BankLineStatusDto.Pending && l.Proposal is not null),
            Threshold,
            [])));

    public Task<OperationResult<ConfirmMatchesResponse>> ConfirmMatchesAsync(ConfirmMatchesRequest request, CancellationToken cancellationToken)
    {
        Confirmations.Add(request);
        List<ConfirmMatchOutcome> outcomes = [];
        foreach (ConfirmMatchItem item in request.Items)
        {
            BankLineResponse line = Lines.First(l => l.Id == item.BankLineId);
            string number = $"ENC-{outcomes.Count + 1:D5}";
            Lines[Lines.IndexOf(line)] = line with { Status = BankLineStatusDto.Matched, PaymentNumber = number, PaymentId = Guid.CreateVersion7() };
            outcomes.Add(new ConfirmMatchOutcome(item.BankLineId, Guid.CreateVersion7(), number, []));
        }

        return Task.FromResult(OperationResult<ConfirmMatchesResponse>.Success(new ConfirmMatchesResponse(outcomes.Count, 0, outcomes)));
    }

    public Task<OperationResult<BankLineResponse>> IgnoreLineAsync(Guid lineId, bool ignored, CancellationToken cancellationToken)
    {
        BankLineResponse line = Lines.First(l => l.Id == lineId);
        BankLineResponse changed = line with { Status = ignored ? BankLineStatusDto.Ignored : BankLineStatusDto.Pending };
        Lines[Lines.IndexOf(line)] = changed;
        return Task.FromResult(OperationResult<BankLineResponse>.Success(changed));
    }

    public Task<OperationResult<BankingSettingsResponse>> GetSettingsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<BankingSettingsResponse>.Success(new BankingSettingsResponse(Threshold, 100m, 1)));

    public Task<OperationResult<BankingSettingsResponse>> UpdateSettingsAsync(UpdateBankingSettingsRequest request, CancellationToken cancellationToken)
    {
        Threshold = request.WriteOffThreshold;
        return GetSettingsAsync(cancellationToken);
    }

    public Task<OperationResult<BankReconciliationStatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<BankReconciliationStatusResponse>.Failure(ErrorCodes.Payment.NotFound));
}
