using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Exports;

/// <summary>
/// Factures reçues en mémoire : la plateforme « rend » <see cref="Pending"/> au relevé, les réponses suivent le cycle de
/// vie simplifié (refus définitif, litige et refus motivés), l'annuaire connaît les SIREN de <see cref="Registered"/>.
/// </summary>
internal sealed class FakeReceivedInvoicesGateway : IReceivedInvoicesGateway
{
    public const string CompanySiren = "552032534";

    public bool PlatformConfigured { get; set; } = true;

    public string? NextError { get; set; }

    public List<ReceivedInvoiceResponse> Received { get; } = [];

    public List<ReceivedInvoiceResponse> Pending { get; } = [];

    public HashSet<string> Registered { get; } = ["732829320", CompanySiren];

    public List<RespondToReceivedInvoiceRequest> Responses { get; } = [];

    public List<string> Lookups { get; } = [];

    public static ReceivedInvoiceResponse Invoice(
        string number,
        string supplier = "Papeterie Moreau SARL",
        string? supplierSiren = "842157307",
        decimal totalExclTax = 137m,
        ReceivedInvoiceKindDto kind = ReceivedInvoiceKindDto.Invoice,
        DateOnly? issued = null,
        PlatformStatusDto status = PlatformStatusDto.MadeAvailable)
    {
        DateOnly date = issued ?? new DateOnly(2026, 10, 18);
        DateTimeOffset at = new(date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
        decimal vat = decimal.Round(totalExclTax * 0.2m, 2);
        ReceivedInvoiceSummary summary = new(
            Guid.CreateVersion7(), $"PAS-R-{number}", kind, number, date, kind == ReceivedInvoiceKindDto.CreditNote ? null : date.AddDays(30),
            supplier, supplierSiren, null, totalExclTax, vat, totalExclTax + vat, "EUR", status, at);
        return Build(summary, [new PlatformStatusEventResponse(PlatformStatusDto.MadeAvailable, at, null, null)]);
    }

    public Task<OperationResult<ReceivedInvoicesResponse>> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Answer(() => new ReceivedInvoicesResponse(
            PlatformConfigured,
            CompanySiren,
            PlatformConfigured ? new DirectoryEntryResponse(CompanySiren, $"0225:{CompanySiren}", "Plateforme agréée simulée", new DateOnly(2026, 6, 1)) : null,
            new DateOnly(2026, 9, 1),
            new DateOnly(2027, 9, 1),
            [.. Received.Select(r => r.Summary).OrderByDescending(s => s.ReceivedAt)])));

    public Task<OperationResult<FetchReceivedInvoicesResponse>> FetchAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Answer(() =>
        {
            int count = Pending.Count;
            Received.AddRange(Pending);
            Pending.Clear();
            return new FetchReceivedInvoicesResponse(count, 0);
        }));

    public Task<OperationResult<ReceivedInvoiceResponse>> GetInvoiceAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Received.Find(r => r.Summary.Id == id) is { } found
            ? OperationResult<ReceivedInvoiceResponse>.Success(found)
            : OperationResult<ReceivedInvoiceResponse>.Failure(ErrorCodes.Reception.NotFound));

    public Task<OperationResult<ReceiptDocument>> DownloadAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Received.Find(r => r.Summary.Id == id) is { } found
            ? OperationResult<ReceiptDocument>.Success(new ReceiptDocument(found.FileName, "application/xml", "<facture/>"u8.ToArray()))
            : OperationResult<ReceiptDocument>.Failure(ErrorCodes.Reception.NotFound));

    public Task<OperationResult<ReceivedInvoiceResponse>> RespondAsync(Guid id, RespondToReceivedInvoiceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        int index = Received.FindIndex(r => r.Summary.Id == id);
        if (index < 0)
        {
            return Task.FromResult(OperationResult<ReceivedInvoiceResponse>.Failure(ErrorCodes.Reception.NotFound));
        }

        ReceivedInvoiceResponse current = Received[index];
        string? error = NextError
            ?? (!current.AvailableResponses.Contains(request.Status) ? ErrorCodes.Reception.ResponseNotAllowed
                : request.Status is PlatformStatusDto.Disputed or PlatformStatusDto.Refused && string.IsNullOrWhiteSpace(request.Reason) ? ErrorCodes.Reception.ReasonRequired
                : request.Status == PlatformStatusDto.PaymentSent && request.Amount is not > 0m ? ErrorCodes.Reception.PaymentAmountInvalid
                : null);
        NextError = null;
        if (error is not null)
        {
            return Task.FromResult(OperationResult<ReceivedInvoiceResponse>.Failure(error));
        }

        Responses.Add(request);
        ReceivedInvoiceResponse updated = Build(
            current.Summary with { Status = request.Status },
            [.. current.History, new PlatformStatusEventResponse(request.Status, current.Summary.ReceivedAt.AddHours(current.History.Count), request.Reason, request.Amount)]);
        Received[index] = updated;
        return Task.FromResult(OperationResult<ReceivedInvoiceResponse>.Success(updated));
    }

    public Task<OperationResult<DirectoryLookupResponse>> LookupDirectoryAsync(string siren, CancellationToken cancellationToken)
    {
        Lookups.Add(siren);
        return Task.FromResult(OperationResult<DirectoryLookupResponse>.Success(!PlatformConfigured
            ? new DirectoryLookupResponse(siren, null, Consulted: false)
            : new DirectoryLookupResponse(siren, Registered.Contains(siren)
                ? new DirectoryEntryResponse(siren, $"0225:{siren}", "Plateforme agréée simulée", new DateOnly(2026, 6, 1))
                : null)));
    }

    private OperationResult<T> Answer<T>(Func<T> value)
    {
        if (NextError is { } error)
        {
            NextError = null;
            return OperationResult<T>.Failure(error);
        }

        return PlatformConfigured || typeof(T) == typeof(ReceivedInvoicesResponse)
            ? OperationResult<T>.Success(value())
            : OperationResult<T>.Failure(ErrorCodes.Platform.NotConfigured);
    }

    private static ReceivedInvoiceResponse Build(ReceivedInvoiceSummary summary, IReadOnlyList<PlatformStatusEventResponse> history)
    {
        PlatformStatusDto[] available = summary.Status switch
        {
            PlatformStatusDto.Refused => [],
            PlatformStatusDto.MadeAvailable => [PlatformStatusDto.InProgress, PlatformStatusDto.Approved, PlatformStatusDto.Disputed, PlatformStatusDto.Refused],
            PlatformStatusDto.Approved => [PlatformStatusDto.Disputed, PlatformStatusDto.PaymentSent],
            _ => [PlatformStatusDto.Approved, PlatformStatusDto.Disputed, PlatformStatusDto.Refused],
        };
        if (summary.Kind == ReceivedInvoiceKindDto.CreditNote)
        {
            available = [.. available.Where(s => s != PlatformStatusDto.PaymentSent)];
        }

        return new ReceivedInvoiceResponse(
            summary,
            "Plateforme agréée simulée",
            EInvoiceFormatDto.Cii,
            $"{summary.Number}.xml",
            "FR39842157307",
            "B1",
            [new ReceivedInvoiceLineResponse("Ramettes de papier A4 80 g", 1m, "C62", summary.TotalExclTax, summary.TotalExclTax, "S", 20m)],
            [new ReceivedInvoiceVatResponse("S", 20m, summary.TotalExclTax, summary.TotalVat)],
            ["Pénalités de retard : trois fois le taux d'intérêt légal"],
            history,
            available);
    }
}
