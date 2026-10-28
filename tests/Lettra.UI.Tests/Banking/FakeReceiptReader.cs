using Lettra.Calculations.Receipts;
using Lettra.UI.Shared.Receipts;

namespace Lettra.UI.Tests.Banking;

/// <summary>Lecture des pièces du test : rend ce qu'on lui dit, garde le type des fichiers qu'on lui a donnés.</summary>
internal sealed class FakeReceiptReader : IReceiptReader
{
    public ReceiptReadOutcome Outcome { get; set; } =
        new(ReceiptReadStatus.Read, new ReceiptReading(950m, new DateOnly(2026, 10, 17), 158.33m, 20m));

    public List<string> Read { get; } = [];

    public Task<ReceiptReadOutcome> ReadAsync(byte[] content, string contentType, CancellationToken cancellationToken)
    {
        Read.Add(contentType);
        return Task.FromResult(Outcome);
    }
}
