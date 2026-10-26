using Lettra.Contracts.Common;
using Lettra.Contracts.Portal;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Portal;

/// <summary>Espace client en mémoire : liens créés, révoqués, et preuve de signature d'un devis.</summary>
internal sealed class FakePortalGateway : IPortalGateway
{
    public List<CreatePortalLinkRequest> Created { get; } = [];

    public List<PortalLinkSummary> Links { get; } = [];

    public List<Guid> Revoked { get; } = [];

    public Dictionary<Guid, QuoteSignatureResponse> Signatures { get; } = [];

    public string? NextError { get; set; }

    public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    public Task<OperationResult<PortalLinkCreated>> CreateLinkAsync(CreatePortalLinkRequest request, CancellationToken cancellationToken)
    {
        if (TakeError() is { } error)
        {
            return Task.FromResult(OperationResult<PortalLinkCreated>.Failure(error));
        }

        Created.Add(request);
        Guid id = Guid.CreateVersion7();
        DateTimeOffset expires = Now.AddDays(request.ValidityDays);
        Links.Add(new PortalLinkSummary(id, request.QuoteId, request.DownPaymentPercent, Now, expires, null, null, IsActive: true));
        PortalLinkResponse link = new(id, "jeton-" + Created.Count, "espace-client/jeton-" + Created.Count, expires, request.QuoteId, request.DownPaymentPercent);
        return Task.FromResult(OperationResult<PortalLinkCreated>.Success(new PortalLinkCreated(link, "https://lettra.test/" + link.Path)));
    }

    public Task<OperationResult<IReadOnlyList<PortalLinkSummary>>> ListLinksAsync(Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<IReadOnlyList<PortalLinkSummary>>.Success([.. Links]));

    public Task<OperationResult<bool>> RevokeLinkAsync(Guid linkId, CancellationToken cancellationToken)
    {
        if (TakeError() is { } error)
        {
            return Task.FromResult(OperationResult<bool>.Failure(error));
        }

        Revoked.Add(linkId);
        int index = Links.FindIndex(l => l.Id == linkId);
        Links[index] = Links[index] with { RevokedAt = Now, IsActive = false };
        return Task.FromResult(OperationResult<bool>.Success(true));
    }

    public Task<OperationResult<QuoteSignatureResponse>> GetQuoteSignatureAsync(Guid quoteId, CancellationToken cancellationToken) =>
        Task.FromResult(Signatures.TryGetValue(quoteId, out QuoteSignatureResponse? signature)
            ? OperationResult<QuoteSignatureResponse>.Success(signature)
            : OperationResult<QuoteSignatureResponse>.Failure(ErrorCodes.Portal.SignatureNotFound));

    private string? TakeError()
    {
        string? error = NextError;
        NextError = null;
        return error;
    }
}
