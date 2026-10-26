using Lettra.Contracts.Common;
using Lettra.Contracts.Vigilance;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Vigilance;

/// <summary>Fiche fictive : la passerelle garde la fiche et refait son état comme le serveur (revue selon le risque).</summary>
internal sealed class FakeVigilanceGateway : IVigilanceGateway
{
    public DueDiligenceResponse? File { get; set; }

    public DueDiligenceStatusDto? Status { get; set; }

    public List<(SaveDueDiligenceRequest Request, long? Version)> Saves { get; } = [];

    public IReadOnlyList<ErrorDetail>? NextErrors { get; set; }

    public static DueDiligenceResponse Sample(RiskLevelDto level = RiskLevelDto.High, DateOnly? ended = null) =>
        new([IdentityEvidenceDto.CompanyRegistration, IdentityEvidenceDto.ManagerIdentity], new DateOnly(2026, 3, 2),
            [
                new BeneficialOwnerDto { Name = "Jeanne Martin", SharePercent = 60m, Kind = OwnershipKindDto.Capital },
                new BeneficialOwnerDto { Name = "Holding Martin", Kind = OwnershipKindDto.Control },
            ],
            level, [RiskFactorDto.PoliticallyExposed], "Élue municipale.", new DateOnly(2026, 3, 2), Guid.CreateVersion7(), "Paul Leroy",
            new DateOnly(2027, 3, 2), ended, ended?.AddYears(5), 4);

    public Task<OperationResult<VigilanceOverviewResponse>> GetAsync(CancellationToken cancellationToken) => Answer(() => { });

    public Task<OperationResult<VigilanceOverviewResponse>> SaveAsync(SaveDueDiligenceRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        Answer(() =>
        {
            Saves.Add((request, expectedVersion));
            int years = request.Level switch { RiskLevelDto.High => 1, RiskLevelDto.Low => 3, _ => 2 };
            File = new DueDiligenceResponse(request.Evidence, request.IdentityVerifiedOn, request.Owners, request.Level, request.Factors, request.Justification,
                request.ReviewedOn, Guid.CreateVersion7(), "Paul Leroy", request.ReviewedOn.AddYears(years), request.RelationshipEndedOn,
                request.RelationshipEndedOn?.AddYears(5), (expectedVersion ?? 0) + 1);
            Status = null;
        });

    private Task<OperationResult<VigilanceOverviewResponse>> Answer(Action change)
    {
        if (NextErrors is { } errors)
        {
            NextErrors = null;
            return Task.FromResult(OperationResult<VigilanceOverviewResponse>.Failure(errors));
        }

        change();
        DueDiligenceStatusDto status = Status ?? (File is null ? DueDiligenceStatusDto.Missing
            : File.RelationshipEndedOn is not null ? DueDiligenceStatusDto.Ended
            : File.Owners.Count == 0 || File.Evidence.Count < 2 ? DueDiligenceStatusDto.Incomplete
            : DueDiligenceStatusDto.UpToDate);
        return Task.FromResult(OperationResult<VigilanceOverviewResponse>.Success(new VigilanceOverviewResponse(File, status)));
    }
}
