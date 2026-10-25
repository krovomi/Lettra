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

    private static Task<OperationResult<T>> Answer<T>(T? value)
        where T : class =>
        Task.FromResult(value is null
            ? OperationResult<T>.Failure([new ErrorDetail(ErrorCodes.Tenancy.DossierNotFound)])
            : OperationResult<T>.Success(value));
}
