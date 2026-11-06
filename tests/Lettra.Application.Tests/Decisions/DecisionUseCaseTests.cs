using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Decisions;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Features.Decisions;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Common;
using Lettra.Contracts.Decisions;
using Lettra.Domain.Common;
using Lettra.Domain.Decisions;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Lettra.Application.Tests.Decisions;

/// <summary>
/// « Décisions à prendre » : l'actualisation rapproche les propositions des règles des décisions gardées, la liste ne
/// montre que ce que l'utilisateur peut traiter, et accepter fait l'action préparée avec ses droits.
/// </summary>
public sealed class DecisionUseCaseTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 3);
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());

    private readonly InMemoryDecisionStore _store = new();
    private readonly FakeRule _reminders = new("relance.due", Permission.FactureEmettre);
    private readonly FakeRule _iban = new("iban.a-verifier", Permission.TiersModifier);
    private readonly FakeAction _send = new("relance.envoyer");
    private readonly FakePermissions _permissions = new();
    private readonly FakeUser _user = new();
    private readonly SettableClock _clock = new(Now);
    private readonly CapturingLoggerProvider _logs = new();
    private readonly LoggerFactory _loggers;

    public DecisionUseCaseTests() => _loggers = new LoggerFactory([_logs]);

    public void Dispose()
    {
        _loggers.Dispose();
        _logs.Dispose();
    }

    private static DecisionProposal Outside(string kind, string subject) =>
        new(kind, subject, DecisionAudience.Cabinet, DecisionUrgency.Normal, null, null, new Dictionary<string, string>(), "x");

    private DecisionRefresher Refresher(params IDecisionRule[] rules) =>
        new(rules.Length == 0 ? [_reminders, _iban] : rules, _store, _store, _clock, _loggers.CreateLogger<DecisionRefresher>());

    private DecisionHandlers Handlers() =>
        new(_store, [_reminders, _iban], [_send], _permissions, _user, Refresher(), _store, _clock);

    private async Task<DecisionsResponse> ListAsync() => (await Handlers().HandleAsync(new ListDecisionsQuery(Dossier), default)).Value;

    private Task<Result> AnswerAsync(Decision decision, DecisionChoiceDto choice, DateOnly? until = null, long? version = null) =>
        Handlers().HandleAsync(new AnswerDecisionCommand(Dossier, decision.Id, choice, until, version), default);

    private Decision Kept(string subject) => _store.Decisions.Single(d => d.Subject == subject);

    [Fact]
    public async Task L_actualisation_cree_met_a_jour_et_regle_les_decisions_selon_ce_que_les_regles_proposent()
    {
        _reminders.Propose("F1:Friendly");
        _reminders.Propose("F1:Friendly", DecisionUrgency.High);
        _iban.Propose("iban-1", DecisionUrgency.Critical);

        DecisionRefreshOutcome first = await Refresher().RefreshAsync(default);
        _reminders.Proposals.Clear();
        _iban.Proposals.Clear();
        _reminders.Version = 2;
        _reminders.Propose("F2:Reminder");
        _iban.Propose("iban-1", DecisionUrgency.High);
        _clock.Now = Now.AddDays(1);
        DecisionRefreshOutcome second = await Refresher().RefreshAsync(default);

        first.Should().Be(new DecisionRefreshOutcome(2, 2, 0, 0), "un doublon de clé ne compte qu'une fois");
        second.Should().Be(new DecisionRefreshOutcome(2, 1, 1, 0));
        Kept("F1:Friendly").Status.Should().Be(DecisionStatus.Resolved, "la règle ne la propose plus");
        Kept("F2:Reminder").RuleVersion.Should().Be(2);
        Kept("iban-1").Urgency.Should().Be(DecisionUrgency.High);
        Kept("iban-1").SeenAt.Should().Be(Now.AddDays(1));
        _store.Policy!.RefreshedAt.Should().Be(Now.AddDays(1));
        _store.Saves.Should().Be(2);
    }

    [Fact]
    public async Task Une_regle_en_echec_laisse_ses_decisions_et_une_regle_retiree_regle_les_siennes()
    {
        _reminders.Propose("F1:Friendly");
        _iban.Propose("iban-1");
        await Refresher().RefreshAsync(default);
        _reminders.Fails = true;

        DecisionRefreshOutcome outcome = await Refresher(_reminders).RefreshAsync(default);

        outcome.FailedRules.Should().Be(1);
        Kept("F1:Friendly").Status.Should().Be(DecisionStatus.Proposed, "sa règle n'a pas répondu");
        Kept("iban-1").Status.Should().Be(DecisionStatus.Resolved, "sa règle n'existe plus sur cette installation");
        _logs.Entries.Should().Contain(e => e.Contains("relance.due", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Les_doublons_et_les_decisions_closes_depuis_treize_mois_sont_effaces()
    {
        Decision old = Decision.Propose(Outside("iban.a-verifier", "iban-old"), 1, Now.AddYears(-2));
        old.Dismiss(_user.UserId, Now.AddMonths(-14));
        Decision twin = Decision.Propose(Outside("iban.a-verifier", "iban-1"), 1, Now.AddMinutes(1));
        Decision first = Decision.Propose(_iban.Propose("iban-1"), 1, Now);
        _store.Decisions.AddRange([old, twin, first]);

        await Refresher().RefreshAsync(default);

        _store.Decisions.Should().ContainSingle(d => d.Subject == "iban-1").Which.Id.Should().Be(first.Id);
        _store.Decisions.Should().NotContain(old);
    }

    [Fact]
    public async Task Une_actualisation_concurrente_est_ignoree()
    {
        _iban.Propose("iban-1");
        _store.ConflictOnSave = true;

        DecisionRefreshOutcome outcome = await Refresher().RefreshAsync(default);

        outcome.Should().Be(new DecisionRefreshOutcome(0, 0, 0, 0));
        (await Handlers().HandleAsync(new RefreshDecisionsCommand(Dossier), default)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task La_liste_ne_montre_que_ce_que_l_utilisateur_peut_traiter_les_plus_urgentes_d_abord()
    {
        _reminders.Propose("F1:Friendly", DecisionUrgency.Normal, new DecisionAction("relance.envoyer", "F1"), Today.AddDays(2));
        _reminders.Propose("F2:Final", DecisionUrgency.High, new DecisionAction("inconnue", "F2"));
        _reminders.Propose("F3:Friendly", DecisionUrgency.Normal, dueOn: Today);
        _iban.Propose("iban-1", DecisionUrgency.Critical);
        _store.Decisions.Add(Decision.Propose(Outside("regle.retiree", "x"), 1, Now));
        await Refresher().RefreshAsync(default);
        _permissions.Granted.Remove(Permission.TiersModifier);
        _permissions.Granted.Remove(Permission.DecisionsRegler);

        DecisionsResponse view = await ListAsync();

        view.Rows.Select(r => r.Facts["subject"]).Should().Equal("F2:Final", "F3:Friendly", "F1:Friendly");
        view.Rows.Select(r => r.CanAccept).Should().Equal(false, false, true);
        view.Rows[2].Should().Match<DecisionRow>(r => r.Kind == "relance.due" && r.Status == DecisionStatusDto.Proposed && r.DueOn == Today.AddDays(2) && r.Link == "somewhere");
        view.Level.Should().Be(AutonomyLevelDto.Prepare);
        view.RefreshedAt.Should().Be(Now);
        view.CanConfigure.Should().BeFalse();
    }

    [Fact]
    public async Task Accepter_fait_l_action_preparee_puis_note_la_decision_acceptee()
    {
        _reminders.Propose("F1:Friendly", action: new DecisionAction("relance.envoyer", "facture-1"));
        await Refresher().RefreshAsync(default);
        Decision decision = Kept("F1:Friendly");

        Result accepted = await AnswerAsync(decision, DecisionChoiceDto.Accept, version: decision.Version);

        accepted.IsSuccess.Should().BeTrue();
        _send.Calls.Should().Equal((Dossier, "facture-1"));
        decision.Status.Should().Be(DecisionStatus.Accepted);
        decision.DecidedBy.Should().Be(_user.UserId);
        (await AnswerAsync(decision, DecisionChoiceDto.Accept)).Errors.Should().ContainSingle().Which.Code.Should().Be(DecisionErrors.NotOpenCode);
        (await ListAsync()).Rows.Should().ContainSingle().Which.Should().Match<DecisionRow>(r => r.Status == DecisionStatusDto.Accepted && !r.CanAccept);
    }

    [Fact]
    public async Task Une_action_refusee_laisse_la_decision_ouverte_avec_l_erreur_de_la_commande()
    {
        _reminders.Propose("F1:Friendly", action: new DecisionAction("relance.envoyer", "facture-1"));
        await Refresher().RefreshAsync(default);
        _send.Refusal = Error.Unavailable("Email.SendFailed");

        Result refused = await AnswerAsync(Kept("F1:Friendly"), DecisionChoiceDto.Accept);

        refused.Errors.Should().ContainSingle().Which.Code.Should().Be("Email.SendFailed");
        Kept("F1:Friendly").Status.Should().Be(DecisionStatus.Proposed);
    }

    [Fact]
    public async Task Sans_action_avec_le_cadran_sur_montrer_ou_sans_gestionnaire_rien_ne_s_accepte()
    {
        _reminders.Propose("sans-action");
        _reminders.Propose("inconnue", action: new DecisionAction("inconnue", "x"));
        _reminders.Propose("preparee", action: new DecisionAction("relance.envoyer", "x"));
        await Refresher().RefreshAsync(default);

        (await AnswerAsync(Kept("sans-action"), DecisionChoiceDto.Accept)).Errors.Should().ContainSingle().Which.Code.Should().Be(DecisionErrors.NoActionCode);
        (await AnswerAsync(Kept("inconnue"), DecisionChoiceDto.Accept)).Errors.Should().ContainSingle().Which.Code.Should().Be(DecisionErrors.ActionUnavailableCode);
        (await Handlers().HandleAsync(new SetDecisionPolicyCommand(Dossier, AutonomyLevelDto.Show, _store.Policy!.Version), default)).IsSuccess.Should().BeTrue();
        (await AnswerAsync(Kept("preparee"), DecisionChoiceDto.Accept)).Errors.Should().ContainSingle().Which.Code.Should().Be(DecisionErrors.NoActionCode);
        (await ListAsync()).Rows.Should().OnlyContain(r => !r.CanAccept);
        _send.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Plus_tard_ignorer_et_remettre_dans_la_liste()
    {
        _reminders.Propose("F1:Friendly");
        _reminders.Propose("F2:Friendly");
        await Refresher().RefreshAsync(default);

        (await AnswerAsync(Kept("F1:Friendly"), DecisionChoiceDto.Snooze, Today.AddDays(7))).IsSuccess.Should().BeTrue();
        (await AnswerAsync(Kept("F2:Friendly"), DecisionChoiceDto.Dismiss)).IsSuccess.Should().BeTrue();
        DecisionsResponse later = await ListAsync();
        (await AnswerAsync(Kept("F1:Friendly"), DecisionChoiceDto.Snooze)).Errors.Should().ContainSingle().Which.Code.Should().Be(DecisionErrors.SnoozeDateInvalidCode);
        (await AnswerAsync(Kept("F2:Friendly"), DecisionChoiceDto.Restore)).IsSuccess.Should().BeTrue();
        (await AnswerAsync(Kept("F2:Friendly"), (DecisionChoiceDto)42)).Errors.Should().ContainSingle().Which.Code.Should().Be(DecisionErrors.ChoiceInvalidCode);

        later.Rows.Select(r => (r.Facts["subject"], r.Status, r.SnoozedUntil)).Should().Equal(
            ("F1:Friendly", DecisionStatusDto.Snoozed, Today.AddDays(7)), ("F2:Friendly", DecisionStatusDto.Dismissed, null));
        Kept("F2:Friendly").Status.Should().Be(DecisionStatus.Proposed);
    }

    [Fact]
    public async Task Une_decision_inconnue_perimee_ou_hors_de_ses_droits_est_refusee()
    {
        _iban.Propose("iban-1");
        await Refresher().RefreshAsync(default);
        Decision decision = Kept("iban-1");

        (await Handlers().HandleAsync(new AnswerDecisionCommand(Dossier, DecisionId.New(), DecisionChoiceDto.Dismiss, null, null), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(DecisionErrors.NotFoundCode);
        (await AnswerAsync(decision, DecisionChoiceDto.Dismiss, version: decision.Version + 1))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        _permissions.Granted.Remove(Permission.TiersModifier);
        (await AnswerAsync(decision, DecisionChoiceDto.Dismiss)).Errors.Should().ContainSingle().Which.Code.Should().Be(DecisionErrors.NotFoundCode);
        decision.Status.Should().Be(DecisionStatus.Proposed);
    }

    [Fact]
    public async Task Le_cadran_se_regle_a_partir_de_la_version_lue()
    {
        Result created = await Handlers().HandleAsync(new SetDecisionPolicyCommand(Dossier, AutonomyLevelDto.Show, null), default);
        Result stale = await Handlers().HandleAsync(new SetDecisionPolicyCommand(Dossier, AutonomyLevelDto.Prepare, 7), default);
        Result invalid = await Handlers().HandleAsync(new SetDecisionPolicyCommand(Dossier, (AutonomyLevelDto)9, null), default);
        DecisionsResponse view = await ListAsync();

        created.IsSuccess.Should().BeTrue();
        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        invalid.Errors.Should().ContainSingle().Which.Code.Should().Be(DecisionErrors.LevelInvalidCode);
        view.Level.Should().Be(AutonomyLevelDto.Show);
        view.CanConfigure.Should().BeTrue();
        _store.Policy!.UpdatedBy.Should().Be(_user.UserId);
    }

    [Fact]
    public async Task La_vue_cabinet_compte_les_decisions_a_decider_au_niveau_de_la_plus_urgente()
    {
        _reminders.Propose("F1:Friendly", DecisionUrgency.Normal, dueOn: Today.AddDays(3));
        _reminders.Propose("F2:Final", DecisionUrgency.High, dueOn: Today.AddDays(1));
        _iban.Propose("iban-1", DecisionUrgency.Low);
        DecisionsIndicatorSource source = new(Refresher(), _store, _clock);

        IReadOnlyList<DossierIndicator> indicators = await source.ComputeAsync(Today, default);
        Kept("F2:Final").Snooze(Today.AddDays(5), Today, _user.UserId);
        DossierIndicator? withoutSnoozed = DecisionView.Indicator(_store.Decisions, Today);

        indicators.Should().Equal(new DossierIndicator(DossierIndicatorKeys.DecisionsPending, 3, IndicatorLevel.Warning, Today.AddDays(1)));
        withoutSnoozed.Should().Be(new DossierIndicator(DossierIndicatorKeys.DecisionsPending, 2, IndicatorLevel.Info, Today.AddDays(3)));
        DecisionView.Indicator([], Today).Should().BeNull();
        _iban.Proposals.Clear();
        _iban.Propose("iban-1", DecisionUrgency.Critical);
        (await source.ComputeAsync(Today, default)).Single().Level.Should().Be(IndicatorLevel.Critical);
        FluentActions.Invoking(() => DecisionView.Indicator(null!, Today)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => DecisionView.ToRow(null!, Today, false)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void La_vue_cabinet_ne_compte_pas_les_decisions_qui_revelent_le_niveau_d_attention()
    {
        Decision meeting = Decision.Propose(Outside(DecisionKinds.PreventionMeeting, "rendez-vous:2026-11"), 1, Now);
        Decision iban = Decision.Propose(Outside("iban.a-verifier", "iban-1"), 1, Now);

        DecisionView.Indicator([meeting], Today).Should().BeNull("le radar ne sort pas du dossier, même en compteur");
        DecisionView.Indicator([meeting, iban], Today)!.Count.Should().Be(1);
    }

    [Fact]
    public void Les_faits_s_ecrivent_sans_culture()
    {
        DecisionFacts.Date(new DateOnly(2026, 11, 3)).Should().Be("2026-11-03");
        DecisionFacts.Label("Tax.Obligation.Des").Should().Be("@Tax.Obligation.Des");
        DecisionFacts.Money(1234.5m).Should().Be("1234.50");
        DecisionFacts.Number(42).Should().Be("42");
    }
}
