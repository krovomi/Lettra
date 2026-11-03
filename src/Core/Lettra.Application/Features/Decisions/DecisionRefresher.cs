using System.Globalization;
using Lettra.Application.Abstractions.Decisions;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Domain.Decisions;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Lettra.Application.Features.Decisions;

/// <summary>Ce qu'a donné une actualisation : décisions ouvertes, nouvelles, réglées d'elles-mêmes, règles en échec.</summary>
public sealed record DecisionRefreshOutcome(int Open, int Added, int Resolved, int FailedRules);

/// <summary>
/// L'actualisation des décisions du dossier courant : chaque règle propose, les propositions sont rapprochées des
/// décisions gardées par leur clé (type + sujet). Une nouvelle clé crée une décision ; une clé encore proposée met ses
/// faits à jour ; une décision ouverte que sa règle ne propose plus se règle d'elle-même. Une règle en échec laisse ses
/// décisions telles quelles. Les décisions closes depuis plus de treize mois sont effacées.
/// Sert la passe quotidienne (par l'indicateur de la vue cabinet) et le bouton « Actualiser ».
/// </summary>
public sealed partial class DecisionRefresher(
    IEnumerable<IDecisionRule> rules,
    IDecisionStore store,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<DecisionRefresher> logger)
{
    public async Task<DecisionRefreshOutcome> RefreshAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        DateOnly today = LegalCalendar.DateOf(now);
        Dictionary<string, RuleRun> runs = new(StringComparer.Ordinal);
        int failed = 0;
        foreach (IDecisionRule rule in rules)
        {
            try
            {
                IReadOnlyList<DecisionProposal> proposals = await rule.ProposeAsync(today, cancellationToken).ConfigureAwait(false);
                runs[rule.Kind] = new RuleRun(rule.Version, [.. proposals.Where(p => string.Equals(p.Kind, rule.Kind, StringComparison.Ordinal))]);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                runs[rule.Kind] = RuleRun.Failed;
                LogRuleFailed(rule.Kind, exception);
            }
        }

        IReadOnlyList<Decision> kept = await store.ListForRefreshAsync(cancellationToken).ConfigureAwait(false);
        // Sans index unique (deux actualisations simultanées ne doivent pas échouer) : un doublon éventuel est retiré ici.
        Dictionary<(string Kind, string Subject), Decision> byKey = [];
        foreach (Decision decision in kept.OrderBy(d => d.ProposedAt))
        {
            if (!byKey.TryAdd((decision.Kind, decision.Subject), decision))
            {
                await store.RemoveAsync(decision, cancellationToken).ConfigureAwait(false);
            }
        }

        kept = [.. byKey.Values];
        int added = 0;
        int resolved = 0;
        HashSet<(string Kind, string Subject)> proposed = [];
        foreach ((string kind, RuleRun run) in runs.Where(r => !r.Value.HasFailed))
        {
            foreach (DecisionProposal proposal in run.Proposals)
            {
                if (!proposed.Add((kind, proposal.Subject)))
                {
                    continue;
                }

                if (byKey.TryGetValue((kind, proposal.Subject), out Decision? decision))
                {
                    decision.Refresh(proposal, run.Version, today, now);
                }
                else
                {
                    await store.AddAsync(Decision.Propose(proposal, run.Version, now), cancellationToken).ConfigureAwait(false);
                    added++;
                }
            }
        }

        foreach (Decision decision in kept)
        {
            // Sa règle a tourné sans la proposer, ou n'existe plus sur cette installation : elle s'est réglée.
            bool ruleAnswered = !runs.TryGetValue(decision.Kind, out RuleRun? run) || !run.HasFailed;
            if (decision.IsOpen && ruleAnswered && !proposed.Contains((decision.Kind, decision.Subject)))
            {
                decision.Resolve(now);
                resolved++;
            }
            else if (decision.IsExpired(now))
            {
                await store.RemoveAsync(decision, cancellationToken).ConfigureAwait(false);
            }
        }

        DecisionPolicy? policy = await store.FindPolicyAsync(cancellationToken).ConfigureAwait(false);
        if (policy is null)
        {
            policy = DecisionPolicy.Default();
            await store.AddPolicyAsync(policy, cancellationToken).ConfigureAwait(false);
        }

        policy.MarkRefreshed(now);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ConcurrencyConflictException)
        {
            // Une autre actualisation (la passe quotidienne, un autre onglet) vient d'écrire les mêmes décisions : la sienne vaut.
            LogConcurrentRefresh();
            return new DecisionRefreshOutcome(0, 0, 0, failed);
        }

        int open = kept.Count(d => d.IsOpen) + added;
        return new DecisionRefreshOutcome(open, added, resolved, failed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Décisions à prendre : actualisation concurrente, la précédente est gardée")]
    private partial void LogConcurrentRefresh();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Décisions à prendre : la règle {Kind} est en échec, ses décisions restent telles quelles")]
    private partial void LogRuleFailed(string kind, Exception exception);

    private sealed record RuleRun(int Version, IReadOnlyList<DecisionProposal> Proposals, bool HasFailed = false)
    {
        public static RuleRun Failed { get; } = new(0, [], HasFailed: true);
    }
}

/// <summary>
/// Les valeurs des phrases d'une décision. L'écran les met en forme dans la langue de l'utilisateur : une valeur qui
/// commence par <c>@</c> est une clé de libellé (« @Tax.Obligation.VatReturnCa3 »), une date est écrite <c>aaaa-mm-jj</c>,
/// un fait dont le nom finit par <c>Amount</c> est un montant en euros.
/// </summary>
public static class DecisionFacts
{
    public const string LabelPrefix = "@";
    public const string AmountSuffix = "Amount";

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Label(string resourceKey) => LabelPrefix + resourceKey;

    public static string Money(decimal amount) => decimal.Round(amount, 2).ToString("0.00", CultureInfo.InvariantCulture);

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
