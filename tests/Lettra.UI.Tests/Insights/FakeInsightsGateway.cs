using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Insights;

/// <summary>Les éclairages tenus par le test : rien de posé, et l'écran reçoit le refus d'un dossier introuvable.</summary>
internal sealed class FakeInsightsGateway : IInsightsGateway
{
    public PaymentWeatherResponse? Weather { get; set; }

    public PracticeAuditResponse? Audit { get; set; }

    public List<(string FileName, int Length, DateOnly? From, DateOnly? To)> Runs { get; } = [];

    public Task<OperationResult<PaymentWeatherResponse>> GetPaymentWeatherAsync(CancellationToken cancellationToken) => Answer(Weather);

    public Task<OperationResult<PracticeAuditResponse>> GetPracticeAuditAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        Runs.Add((string.Empty, 0, from, to));
        return Answer(Audit);
    }

    public Task<OperationResult<PracticeAuditResponse>> RunPracticeAuditAsync(string fileName, byte[] content, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        Runs.Add((fileName, content.Length, from, to));
        return Answer(Audit);
    }

    public InvoiceProofCheckResponse? Check { get; set; }

    public IReadOnlyList<InvoiceProofKeyResponse>? Keys { get; set; } = [];

    public List<string> Verified { get; } = [];

    public Task<OperationResult<InvoiceProofResponse>> GetInvoiceProofAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        Answer<InvoiceProofResponse>(new(invoiceId, "FA-2026-0042", "LETTRA-PREUVE-1;552100554;FA-2026-0042;20261016;426.94;p256-0123456789abcdef;MEUCIQ", "p256-0123456789abcdef"));

    public Task<OperationResult<InvoiceProofCheckResponse>> VerifyInvoiceProofAsync(string code, CancellationToken cancellationToken)
    {
        Verified.Add(code);
        return Answer(Check);
    }

    public Task<OperationResult<IReadOnlyList<InvoiceProofKeyResponse>>> ListInvoiceProofKeysAsync(CancellationToken cancellationToken) => Answer(Keys);

    private static Task<OperationResult<T>> Answer<T>(T? value)
        where T : class =>
        Task.FromResult(value is null
            ? OperationResult<T>.Failure([new ErrorDetail(ErrorCodes.Tenancy.DossierNotFound)])
            : OperationResult<T>.Success(value));
}
