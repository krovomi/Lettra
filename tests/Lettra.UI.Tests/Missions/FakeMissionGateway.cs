using Lettra.Contracts.Common;
using Lettra.Contracts.Missions;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Missions;

/// <summary>Mission fictive : la passerelle garde la mission et les temps, et refait la synthèse comme le serveur (sans prorata).</summary>
internal sealed class FakeMissionGateway : IMissionGateway
{
    public static readonly Guid Me = Guid.CreateVersion7();

    public MissionResponse? Mission { get; set; }

    public List<TimeEntryResponse> Entries { get; } = [];

    public List<(SaveMissionRequest Request, long? Version)> Saves { get; } = [];

    public decimal Earned { get; set; }

    public string? NextError { get; set; }

    /// <summary>La fiche anti-blanchiment est à jour : la lettre peut se signer.</summary>
    public bool ClientKnown { get; set; }

    public static MissionResponse Sample(decimal? targetRate = 80m) =>
        new(MissionKindDto.Presentation, [MissionServiceDto.Bookkeeping, MissionServiceDto.AnnualAccounts], new DateOnly(2026, 1, 1),
            2400m, FeeScheduleDto.Monthly, 200m, targetRate, targetRate is { } r ? Math.Round(2400m / r, 1) : null, "Jeanne Martin", "Paul Leroy", null, 3);

    public static TimeEntryResponse Entry(int month, int day, int minutes, TimeTaskDto task, string? note = null, bool mine = true, string? who = "Paul Leroy") =>
        new(Guid.CreateVersion7(), new DateOnly(2026, month, day), mine ? Me : Guid.CreateVersion7(), who, minutes, task, note, mine);

    public Task<OperationResult<MissionOverviewResponse>> GetAsync(CancellationToken cancellationToken) => Answer(() => { });

    public Task<OperationResult<MissionOverviewResponse>> SaveAsync(SaveMissionRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        Answer(() =>
        {
            Saves.Add((request, expectedVersion));
            int instalments = request.Schedule switch { FeeScheduleDto.Monthly => 12, FeeScheduleDto.Quarterly => 4, _ => 1 };
            Mission = new MissionResponse(request.Kind, request.Services, request.StartDate, request.AnnualFeeExclTax, request.Schedule,
                Math.Round(request.AnnualFeeExclTax / instalments, 2), request.TargetHourlyRate,
                request.TargetHourlyRate is > 0 and { } r ? Math.Round(request.AnnualFeeExclTax / r, 1) : null,
                request.ClientSignatory, request.Manager, request.SignedOn, (expectedVersion ?? 0) + 1);
        });

    public Task<OperationResult<MissionOverviewResponse>> AddTimeAsync(AddTimeEntryRequest request, CancellationToken cancellationToken) =>
        Answer(() => Entries.Add(new TimeEntryResponse(Guid.CreateVersion7(), request.Date, Me, "Paul Leroy", request.Minutes, request.Task, request.Note, true)));

    public Task<OperationResult<MissionOverviewResponse>> DeleteTimeAsync(Guid id, CancellationToken cancellationToken) =>
        Answer(() => Entries.RemoveAll(e => e.Id == id));

    private Task<OperationResult<MissionOverviewResponse>> Answer(Action change)
    {
        if (NextError is { } code)
        {
            NextError = null;
            return Task.FromResult(OperationResult<MissionOverviewResponse>.Failure(code));
        }

        change();
        int minutes = Entries.Sum(e => e.Minutes);
        TimeTaskTotal[] byTask = [.. Entries.GroupBy(e => e.Task).Select(g => new TimeTaskTotal(g.Key, g.Sum(e => e.Minutes))).OrderByDescending(t => t.Minutes)];
        return Task.FromResult(OperationResult<MissionOverviewResponse>.Success(new MissionOverviewResponse(
            Mission, "Cabinet Exemple", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), minutes, Earned,
            minutes == 0 ? null : Math.Round(Earned / (minutes / 60m), 2), byTask, [.. Entries], ClientKnown)));
    }
}
