using Lettra.Contracts.Alerts;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests;

internal sealed class FakeAlertsGateway : IAlertsGateway
{
    public List<AlertDetailResponse> Alerts { get; } = [];

    public List<(bool CabinetWide, bool IncludeClosed)> Lists { get; } = [];

    public IReadOnlyList<ErrorDetail>? NextErrors { get; set; }

    public ChangeAlertSeverityRequest? LastSeverityChange { get; private set; }

    public AlertDetailResponse Add(string ruleId, AlertSeverity severity, AlertState state = AlertState.New)
    {
        DateTimeOffset at = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
        AlertSummary summary = new(Guid.CreateVersion7(), ruleId, "Titre technique", severity, AlertCategory.Compliance, state,
            AlertScopeLevel.Dossier, Guid.CreateVersion7(), null, Guid.CreateVersion7(), at, at, at, 1, 0, null, at.AddMinutes(15));
        AlertDetailResponse detail = new(summary, [new AlertTimelineItem(at, "Raised", "Rule", ruleId, null)]);
        Alerts.Add(detail);
        return detail;
    }

    public Task<OperationResult<AlertCentreResponse>> ListAsync(bool cabinetWide, bool includeClosed, CancellationToken cancellationToken)
    {
        Lists.Add((cabinetWide, includeClosed));
        AlertSummary[] open = [.. Alerts.Select(a => a.Alert).Where(a => includeClosed || a.State != AlertState.Closed)];
        AlertSeverityCounter[] counters = [.. Enum.GetValues<AlertSeverity>().Select(s => new AlertSeverityCounter(
            s, open.Count(a => a.Severity == s && a.State is AlertState.New or AlertState.Acknowledged),
            open.Where(a => a.Severity == s && a.State == AlertState.New).Min(a => (DateTimeOffset?)a.OpenedAt)))];
        return Task.FromResult(OperationResult<AlertCentreResponse>.Success(new AlertCentreResponse(counters, open)));
    }

    public Task<OperationResult<AlertDetailResponse>> GetAsync(Guid alertId, bool cabinetWide, CancellationToken cancellationToken) =>
        Task.FromResult(Find(alertId));

    public Task<OperationResult<AlertDetailResponse>> AcknowledgeAsync(Guid alertId, CancellationToken cancellationToken) =>
        Task.FromResult(Update(alertId, a => a with { State = AlertState.Acknowledged, AcknowledgedBy = "user-1" }, "Acknowledged", null));

    public Task<OperationResult<AlertDetailResponse>> ResolveAsync(Guid alertId, CancellationToken cancellationToken) =>
        Task.FromResult(Update(alertId, a => a with { State = AlertState.Resolved }, "Resolved", null));

    public Task<OperationResult<AlertDetailResponse>> ChangeSeverityAsync(Guid alertId, ChangeAlertSeverityRequest request, CancellationToken cancellationToken)
    {
        LastSeverityChange = request with { };
        return Task.FromResult(Update(alertId, a => a with { Severity = request.Severity }, $"SeverityChanged:{request.Severity}", request.Reason));
    }

    private OperationResult<AlertDetailResponse> Find(Guid alertId)
    {
        AlertDetailResponse? found = Alerts.SingleOrDefault(a => a.Alert.Id == alertId);
        return found is null
            ? OperationResult<AlertDetailResponse>.Failure(ErrorCodes.Alerts.NotFound)
            : OperationResult<AlertDetailResponse>.Success(found);
    }

    private OperationResult<AlertDetailResponse> Update(Guid alertId, Func<AlertSummary, AlertSummary> change, string kind, string? reason)
    {
        if (NextErrors is not null)
        {
            return OperationResult<AlertDetailResponse>.Failure(NextErrors);
        }

        int index = Alerts.FindIndex(a => a.Alert.Id == alertId);
        AlertDetailResponse current = Alerts[index];
        AlertDetailResponse updated = new(change(current.Alert),
            [.. current.Timeline, new AlertTimelineItem(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero), kind, "Human", "user-1", reason)]);
        Alerts[index] = updated;
        return OperationResult<AlertDetailResponse>.Success(updated);
    }
}
