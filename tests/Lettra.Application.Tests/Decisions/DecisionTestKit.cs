using Lettra.Application.Abstractions.Decisions;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Domain.Common;
using Lettra.Domain.Decisions;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Tests.Decisions;

/// <summary>Les décisions et le cadran en mémoire ; l'enregistrement compte les appels et peut simuler une écriture concurrente.</summary>
internal sealed class InMemoryDecisionStore : IDecisionStore, IUnitOfWork
{
    public List<Decision> Decisions { get; } = [];

    public DecisionPolicy? Policy { get; set; }

    public int Saves { get; private set; }

    public bool ConflictOnSave { get; set; }

    public Task<IReadOnlyList<Decision>> ListForRefreshAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Decision>>([.. Decisions]);

    public Task<IReadOnlyList<Decision>> ListAsync(DateTimeOffset closedSince, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Decision>>([.. Decisions.Where(d => d.IsOpen || d.ClosedAt >= closedSince)]);

    public Task<Decision?> FindAsync(DecisionId id, CancellationToken cancellationToken) => Task.FromResult(Decisions.Find(d => d.Id == id));

    public Task AddAsync(Decision decision, CancellationToken cancellationToken)
    {
        Decisions.Add(decision);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Decision decision, CancellationToken cancellationToken)
    {
        Decisions.Remove(decision);
        return Task.CompletedTask;
    }

    public Task<DecisionPolicy?> FindPolicyAsync(CancellationToken cancellationToken) => Task.FromResult(Policy);

    public Task AddPolicyAsync(DecisionPolicy policy, CancellationToken cancellationToken)
    {
        Policy = policy;
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (ConflictOnSave)
        {
            throw new ConcurrencyConflictException();
        }

        Saves++;
        return Task.FromResult(1);
    }
}

/// <summary>Une règle de test : ce qu'elle propose se règle d'un test à l'autre ; elle peut aussi échouer.</summary>
internal sealed class FakeRule(string kind, Permission permission = Permission.DossierLire) : IDecisionRule
{
    public List<DecisionProposal> Proposals { get; } = [];

    public bool Fails { get; set; }

    public string Kind => kind;

    public int Version { get; set; } = 1;

    public Permission Permission => permission;

    public Task<IReadOnlyList<DecisionProposal>> ProposeAsync(DateOnly today, CancellationToken cancellationToken) =>
        Fails ? throw new InvalidOperationException("règle cassée") : Task.FromResult<IReadOnlyList<DecisionProposal>>([.. Proposals]);

    public DecisionProposal Propose(string subject, DecisionUrgency urgency = DecisionUrgency.Normal, DecisionAction? action = null, DateOnly? dueOn = null)
    {
        DecisionProposal proposal = new(kind, subject, DecisionAudience.Cabinet, urgency, null, dueOn, new Dictionary<string, string> { ["subject"] = subject }, "somewhere", action);
        Proposals.Add(proposal);
        return proposal;
    }
}

/// <summary>Une action de test : elle note ce qu'on lui demande et peut refuser.</summary>
internal sealed class FakeAction(string kind) : IDecisionActionHandler
{
    public List<(DossierId Dossier, string Payload)> Calls { get; } = [];

    public Error? Refusal { get; set; }

    public string ActionKind => kind;

    public bool Reversible => false;

    public Task<Result> ExecuteAsync(DossierId dossier, string payload, CancellationToken cancellationToken)
    {
        Calls.Add((dossier, payload));
        return Task.FromResult(Refusal is { } error ? Result.Failure([error]) : Result.Success());
    }
}

/// <summary>Une horloge qu'on avance à la main.</summary>
internal sealed class SettableClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
