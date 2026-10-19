using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Billing;

/// <summary>Modèles récurrents en mémoire : garde les requêtes reçues (filtres, versions) et rend la passe programmée.</summary>
internal sealed class FakeRecurringInvoicesGateway : IRecurringInvoicesGateway
{
    public List<RecurringInvoiceResponse> Templates { get; } = [];

    public List<RecurringInvoiceListQuery> Queries { get; } = [];

    public List<(Guid? Id, SaveRecurringInvoiceRequest Request, long? Version)> Saved { get; } = [];

    public List<Guid?> Runs { get; } = [];

    public RecurringRunResponse NextRun { get; set; } = new([], []);

    public IReadOnlyList<ErrorDetail>? SaveErrors { get; set; }

    public static RecurringInvoiceResponse Template(
        string name, bool active = true, bool due = false, bool awaitsIndex = false, IReadOnlyList<RecurringOccurrenceResponse>? occurrences = null)
    {
        DocumentLineRequest line = new() { Designation = "Tenue comptable", UnitCode = "C62", Quantity = 1m, UnitPriceExclTax = 250m };
        (List<DocumentLineResponse> lines, DocumentTotalsResponse totals) = BillingFixtures.Compute([line], VatRegimeDto.Standard);
        DateOnly start = new(2026, 10, 1);
        return new RecurringInvoiceResponse(Guid.CreateVersion7(), 3, name, BillingFixtures.DupontId, "Dupont SARL", VatRegimeDto.Standard, "Honoraires", null,
            "Virement à 30 jours", lines, totals, RecurrenceFrequencyDto.Monthly, BillingTimingDto.InAdvance, start, null, active, false,
            awaitsIndex ? PriceRevisionKindDto.SyntecIndex : PriceRevisionKindDto.None, null, awaitsIndex ? 300m : null, awaitsIndex ? 300m : null,
            awaitsIndex ? new DateOnly(2027, 10, 1) : null, awaitsIndex, active ? start : null, active ? new DateOnly(2026, 10, 31) : null, active ? start : null,
            due, occurrences ?? [], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    }

    public RecurringInvoiceResponse Add(RecurringInvoiceResponse template)
    {
        Templates.Add(template);
        return template;
    }

    public Task<OperationResult<PagedResponse<RecurringInvoiceSummary>>> ListAsync(RecurringInvoiceListQuery query, CancellationToken cancellationToken)
    {
        Queries.Add(query);
        List<RecurringInvoiceSummary> items = [.. Templates
            .Where(t => query.Search is null || t.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .Where(t => query.Active is null || t.IsActive == query.Active)
            .Where(t => !query.DueOnly || t.IsDue)
            .Select(t => new RecurringInvoiceSummary(t.Id, t.Name, t.CustomerId, t.CustomerName, t.Frequency, t.Totals.TotalExclTax, t.IsActive,
                t.ValidateAutomatically, t.NextIssueDate, t.IsDue, t.RevisionAwaitsIndex, t.Occurrences.Count))];
        return Task.FromResult(OperationResult<PagedResponse<RecurringInvoiceSummary>>.Success(
            new PagedResponse<RecurringInvoiceSummary>(items, query.Page, query.PageSize, items.Count)));
    }

    public Task<OperationResult<RecurringInvoiceResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Templates.Find(t => t.Id == id) is { } found
            ? OperationResult<RecurringInvoiceResponse>.Success(found)
            : OperationResult<RecurringInvoiceResponse>.Failure([new ErrorDetail(ErrorCodes.RecurringInvoice.NotFound, null)]));

    public Task<OperationResult<RecurringInvoiceResponse>> CreateAsync(SaveRecurringInvoiceRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Save(null, request, null));

    public Task<OperationResult<RecurringInvoiceResponse>> UpdateAsync(Guid id, SaveRecurringInvoiceRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        Task.FromResult(Save(id, request, expectedVersion));

    public Task<OperationResult<RecurringInvoiceResponse>> SetActiveAsync(Guid id, bool isActive, long? expectedVersion, CancellationToken cancellationToken) =>
        Task.FromResult(Replace(id, t => t with { IsActive = isActive, NextIssueDate = isActive ? t.StartDate : null, Version = t.Version + 1 }));

    public Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken)
    {
        Templates.RemoveAll(t => t.Id == id);
        return Task.FromResult(OperationResult<bool>.Success(true));
    }

    public Task<OperationResult<RecurringRunResponse>> RunAsync(Guid? id, CancellationToken cancellationToken)
    {
        Runs.Add(id);
        foreach (RecurringInvoiceResponse template in Templates.ToList())
        {
            Replace(template.Id, t => t with { IsDue = false });
        }

        return Task.FromResult(OperationResult<RecurringRunResponse>.Success(NextRun));
    }

    private OperationResult<RecurringInvoiceResponse> Save(Guid? id, SaveRecurringInvoiceRequest request, long? version)
    {
        Saved.Add((id, request, version));
        if (SaveErrors is { } errors)
        {
            return OperationResult<RecurringInvoiceResponse>.Failure(errors);
        }

        (List<DocumentLineResponse> lines, DocumentTotalsResponse totals) = BillingFixtures.Compute(request.Lines, request.VatRegime);
        RecurringInvoiceResponse saved = Template(request.Name) with
        {
            Id = id ?? Guid.CreateVersion7(), CustomerId = request.CustomerId, Lines = lines, Totals = totals, Frequency = request.Frequency,
            ValidateAutomatically = request.ValidateAutomatically, Revision = request.Revision, RevisionPercent = request.RevisionPercent,
        };
        Templates.RemoveAll(t => t.Id == saved.Id);
        Templates.Add(saved);
        return OperationResult<RecurringInvoiceResponse>.Success(saved);
    }

    private OperationResult<RecurringInvoiceResponse> Replace(Guid id, Func<RecurringInvoiceResponse, RecurringInvoiceResponse> change)
    {
        int index = Templates.FindIndex(t => t.Id == id);
        Templates[index] = change(Templates[index]);
        return OperationResult<RecurringInvoiceResponse>.Success(Templates[index]);
    }
}
