using Lettra.Contracts.Administration;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Banking;

/// <summary>Justificatifs fictifs : les tests posent les dépenses, la passerelle change leur statut comme le serveur.</summary>
internal sealed class FakeReceiptsGateway : IReceiptsGateway
{
    public List<ReceiptLineResponse> Lines { get; } = [];

    public List<RequestReceiptsRequest> Requests { get; } = [];

    public List<(Guid LineId, string FileName, byte[] Content)> Uploads { get; } = [];

    public string? LastRecipient { get; set; }

    public bool EmailConfigured { get; set; } = true;

    public string? NextError { get; set; }

    public static ReceiptLineResponse Line(int day, decimal amount, string label, string? name = null, ReceiptStatusDto status = ReceiptStatusDto.Missing) =>
        new(Guid.CreateVersion7(), new DateOnly(2026, 10, day), amount, label, name, status, null, null, 0, null, 1);

    public Task<OperationResult<ReceiptsResponse>> GetReceiptsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Error<ReceiptsResponse>() ?? OperationResult<ReceiptsResponse>.Success(View(Lines)));

    public Task<OperationResult<ReceiptsResponse>> GetRequestedAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Error<ReceiptsResponse>() ?? OperationResult<ReceiptsResponse>.Success(View([.. Lines.Where(l => l.Status == ReceiptStatusDto.Requested)])));

    public Task<OperationResult<RequestReceiptsResponse>> RequestAsync(RequestReceiptsRequest request, CancellationToken cancellationToken)
    {
        if (Error<RequestReceiptsResponse>() is { } failure)
        {
            return Task.FromResult(failure);
        }

        Requests.Add(request);
        LastRecipient = request.Email;
        Change(request.LineIds, l => l with { Status = ReceiptStatusDto.Requested, RequestedTo = request.Email, Requests = l.Requests + 1, RequestedAt = DateTimeOffset.UnixEpoch });
        InvitationEmailStatusDto email = EmailConfigured ? InvitationEmailStatusDto.Sent : InvitationEmailStatusDto.NotConfigured;
        return Task.FromResult(OperationResult<RequestReceiptsResponse>.Success(new RequestReceiptsResponse(request.LineIds.Count, email)));
    }

    public Task<OperationResult<ReceiptLineResponse>> SetNotNeededAsync(Guid lineId, bool notNeeded, CancellationToken cancellationToken) =>
        Task.FromResult(Error<ReceiptLineResponse>() ?? OperationResult<ReceiptLineResponse>.Success(
            Change([lineId], l => l with { Status = notNeeded ? ReceiptStatusDto.NotNeeded : ReceiptStatusDto.Missing })));

    public Task<OperationResult<ReceiptLineResponse>> UploadAsync(Guid lineId, UploadReceiptRequest request, CancellationToken cancellationToken)
    {
        if (Error<ReceiptLineResponse>() is { } failure)
        {
            return Task.FromResult(failure);
        }

        byte[] content = Convert.FromBase64String(request.ContentBase64);
        Uploads.Add((lineId, request.FileName, content));
        ReceiptFileResponse file = new(Guid.CreateVersion7(), request.FileName, "image/jpeg", content.Length, DateTimeOffset.UnixEpoch);
        return Task.FromResult(OperationResult<ReceiptLineResponse>.Success(Change([lineId], l => l with { Status = ReceiptStatusDto.Received, Receipt = file })));
    }

    public Task<OperationResult<ReceiptDocument>> DownloadAsync(Guid lineId, CancellationToken cancellationToken) =>
        Task.FromResult(Error<ReceiptDocument>() ?? (Lines.Single(l => l.LineId == lineId).Receipt is { } file
            ? OperationResult<ReceiptDocument>.Success(new ReceiptDocument(file.FileName, file.ContentType, [1, 2, 3]))
            : OperationResult<ReceiptDocument>.Failure(ErrorCodes.Receipt.NotFound)));

    private ReceiptLineResponse Change(IEnumerable<Guid> ids, Func<ReceiptLineResponse, ReceiptLineResponse> change)
    {
        ReceiptLineResponse last = null!;
        foreach (Guid id in ids)
        {
            int index = Lines.FindIndex(l => l.LineId == id);
            last = Lines[index] = change(Lines[index]);
        }

        return last;
    }

    private ReceiptsResponse View(IReadOnlyList<ReceiptLineResponse> lines)
    {
        ReceiptLineResponse[] missing = [.. lines.Where(l => l.Status is ReceiptStatusDto.Missing or ReceiptStatusDto.Requested)];
        return new ReceiptsResponse(lines, missing.Length, missing.Sum(l => l.Amount), lines.Count(l => l.Status == ReceiptStatusDto.Requested),
            lines.Count(l => l.Status == ReceiptStatusDto.Received), LastRecipient, EmailConfigured);
    }

    private OperationResult<T>? Error<T>()
    {
        if (NextError is not { } code)
        {
            return null;
        }

        NextError = null;
        return OperationResult<T>.Failure(code);
    }
}
