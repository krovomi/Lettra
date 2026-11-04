using Lettra.Contracts.Common;
using Lettra.Contracts.Decisions;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Decisions;

/// <summary>Décisions en mémoire : chaque réponse change l'état comme le serveur et monte la version.</summary>
internal sealed class FakeDecisionsGateway : IDecisionsGateway
{
    public static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    public List<DecisionRow> Rows { get; } = [];

    public AutonomyLevelDto Level { get; set; } = AutonomyLevelDto.Prepare;

    public long? PolicyVersion { get; set; } = 4;

    public DateTimeOffset? RefreshedAt { get; set; } = Now.AddHours(-1);

    public bool CanConfigure { get; set; } = true;

    public int Refreshes { get; private set; }

    public List<(Guid Id, AnswerDecisionRequest Request, long? Version)> Answers { get; } = [];

    public List<(AutonomyLevelDto Level, long? Version)> Policies { get; } = [];

    /// <summary>La prochaine écriture échoue avec ce code.</summary>
    public string? NextError { get; set; }

    /// <summary>La prochaine réponse arrive trop tard : quelqu'un a changé la décision entre-temps.</summary>
    public bool NextStale { get; set; }

    public DecisionRow Add(
        string kind, Dictionary<string, string> facts, DecisionUrgencyDto urgency = DecisionUrgencyDto.Normal, bool canAccept = false,
        decimal? impact = null, DateOnly? dueOn = null, DecisionAudienceDto audience = DecisionAudienceDto.Entreprise,
        DecisionStatusDto status = DecisionStatusDto.Proposed, DateOnly? snoozedUntil = null)
    {
        DecisionRow row = new(
            Guid.CreateVersion7(), kind, audience, urgency, impact, dueOn, facts, "somewhere", canAccept, status, snoozedUntil, Now.AddDays(-2),
            status is DecisionStatusDto.Accepted or DecisionStatusDto.Dismissed or DecisionStatusDto.Resolved ? Now.AddDays(-1) : null, 2);
        Rows.Add(row);
        return row;
    }

    private DecisionsResponse View() => new([.. Rows], Level, PolicyVersion, RefreshedAt, CanConfigure);

    public Task<OperationResult<DecisionsResponse>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<DecisionsResponse>.Success(View()));

    public Task<OperationResult<DecisionsResponse>> RefreshAsync(CancellationToken cancellationToken)
    {
        Refreshes++;
        RefreshedAt = Now;
        return ListAsync(cancellationToken);
    }

    public Task<OperationResult<DecisionsResponse>> AnswerAsync(Guid decisionId, AnswerDecisionRequest request, long? expectedVersion, CancellationToken cancellationToken)
    {
        Answers.Add((decisionId, request, expectedVersion));
        if (Refused() is { } refused)
        {
            return Task.FromResult(refused);
        }

        int index = Rows.FindIndex(r => r.Id == decisionId);
        DecisionRow row = Rows[index];
        DecisionRow answered = request.Choice switch
        {
            DecisionChoiceDto.Accept => row with { Status = DecisionStatusDto.Accepted, ClosedAt = Now, CanAccept = false },
            DecisionChoiceDto.Snooze => row with { Status = DecisionStatusDto.Snoozed, SnoozedUntil = request.SnoozeUntil },
            DecisionChoiceDto.Dismiss => row with { Status = DecisionStatusDto.Dismissed, ClosedAt = Now },
            _ => row with { Status = DecisionStatusDto.Proposed, SnoozedUntil = null, ClosedAt = null },
        };
        Rows[index] = answered with { Version = row.Version + 1 };
        return ListAsync(cancellationToken);
    }

    public Task<OperationResult<DecisionsResponse>> SetPolicyAsync(SetDecisionPolicyRequest request, long? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Policies.Add((request.Level, expectedVersion));
        if (Refused() is { } refused)
        {
            return Task.FromResult(refused);
        }

        Level = request.Level;
        PolicyVersion = (PolicyVersion ?? 0) + 1;
        return ListAsync(cancellationToken);
    }

    private OperationResult<DecisionsResponse>? Refused()
    {
        if (NextStale)
        {
            NextStale = false;
            return OperationResult<DecisionsResponse>.Stale(View());
        }

        if (NextError is { } code)
        {
            NextError = null;
            return OperationResult<DecisionsResponse>.Failure(code);
        }

        return null;
    }
}
