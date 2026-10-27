using Lettra.Contracts.Common;
using Lettra.Contracts.Tax;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Tax;

/// <summary>Bénéficiaires fictifs : la passerelle garde les fournisseurs et refait les totaux et le seuil comme le serveur.</summary>
internal sealed class FakeFeeReturnGateway : IFeeReturnGateway
{
    public static readonly DateOnly Start = new(2025, 1, 1);

    private readonly List<Payee> _payees = [];

    public List<DateOnly?> Queries { get; } = [];

    public List<(Guid SupplierId, SaveFeeBeneficiaryRequest Request, long? Version)> Saves { get; } = [];

    public List<(Guid SupplierId, DateOnly Start, long Version)> Removals { get; } = [];

    public IReadOnlyList<ErrorDetail>? NextErrors { get; set; }

    public FiscalDeadlineStateDto DeadlineState { get; set; } = FiscalDeadlineStateDto.Upcoming;

    public Guid Add(string name, decimal bank = 0m, decimal outside = 0m, bool tracked = true, string? siret = "73282932000074", FeeNatureDto nature = FeeNatureDto.Fees)
    {
        Payee payee = new(Guid.CreateVersion7(), name, siret, bank) { Tracked = tracked, Outside = outside, Nature = nature, Profession = "Avocat", Version = tracked ? 2 : 0 };
        _payees.Add(payee);
        return payee.Id;
    }

    public Task<OperationResult<FeeReturnResponse>> GetAsync(DateOnly? date, CancellationToken cancellationToken)
    {
        Queries.Add(date);
        return Answer(() => { });
    }

    public Task<OperationResult<FeeReturnResponse>> SaveAsync(Guid supplierId, SaveFeeBeneficiaryRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        Answer(() =>
        {
            Saves.Add((supplierId, request, expectedVersion));
            Payee payee = _payees.Single(p => p.Id == supplierId);
            (payee.Tracked, payee.Nature, payee.Profession, payee.Outside, payee.Version) = (true, request.Nature, request.Profession, request.PaidOutside + 0.0m, (expectedVersion ?? 0) + 1);
        });

    public Task<OperationResult<FeeReturnResponse>> RemoveAsync(Guid supplierId, DateOnly fiscalYearStart, long expectedVersion, CancellationToken cancellationToken) =>
        Answer(() =>
        {
            Removals.Add((supplierId, fiscalYearStart, expectedVersion));
            _payees.Single(p => p.Id == supplierId).Tracked = false;
        });

    private Task<OperationResult<FeeReturnResponse>> Answer(Action change)
    {
        if (NextErrors is { } errors)
        {
            NextErrors = null;
            return Task.FromResult(OperationResult<FeeReturnResponse>.Failure(errors));
        }

        change();
        List<Payee> tracked = [.. _payees.Where(p => p.Tracked)];
        FiscalDeadlineResponse? deadline = tracked.Count == 0 ? null
            : new(FiscalObligationDto.FeeReturnDas2, Start, new DateOnly(2025, 12, 31), new DateOnly(2026, 5, 5), DeadlineState, null, "Ca12");
        return Task.FromResult(OperationResult<FeeReturnResponse>.Success(new FeeReturnResponse(
            Start,
            new DateOnly(2025, 12, 31),
            deadline,
            2_400m,
            [.. tracked.Select(p => new FeeBeneficiaryRow(p.Id, p.Name, p.Siret, "2 chemin du Moulin, 45000 Orléans", p.Nature, p.Profession, p.Bank, p.Outside,
                p.Bank + p.Outside, p.Bank + p.Outside > 2_400m, p.Siret is null, true, p.Version)).OrderByDescending(r => r.Total)],
            [.. _payees.Where(p => !p.Tracked).Select(p => new FeeReturnCandidate(p.Id, p.Name))],
            new DateOnly(2026, 2, 27))));
    }

    private sealed record Payee(Guid Id, string Name, string? Siret, decimal Bank)
    {
        public bool Tracked { get; set; }

        public decimal Outside { get; set; }

        public FeeNatureDto Nature { get; set; }

        public string Profession { get; set; } = string.Empty;

        public long Version { get; set; }
    }
}
