using Lettra.Supervision.Domain;

namespace Lettra.Supervision.Tests;

public sealed class AlertTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly Actor Personne = Actor.Human(Guid.CreateVersion7());

    private static Alert CellAlert(Severity severity = Severity.S1) =>
        Alert.Raise("api-indisponible", "API indisponible", severity, AlertCategory.Availability, AlertScope.ForCell("mut-fr-01").Value, T0).Value;

    [Fact]
    public void Une_alerte_declenchee_est_nouvelle_et_tracee_par_sa_regle()
    {
        Alert alert = CellAlert();

        alert.State.Should().Be(AlertState.New);
        alert.Fingerprint.Should().Be("api-indisponible|cell:mut-fr-01");
        alert.Timeline.Should().ContainSingle().Which.Actor.Kind.Should().Be(ActorKind.Rule);
        alert.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<AlertRaised>();
    }

    [Fact]
    public void Vigie_ne_peut_ni_prendre_en_charge_ni_resoudre_ni_changer_la_gravite()
    {
        Alert alert = CellAlert();

        alert.Acknowledge(Actor.Vigie, T0).Errors.Should().ContainSingle(e => e.Code == SupervisionErrors.AgentNotAllowedCode);
        alert.Resolve(Actor.Vigie, T0).Errors.Should().ContainSingle(e => e.Code == SupervisionErrors.AgentNotAllowedCode);
        alert.ChangeSeverity(Severity.S3, "bruit", Actor.Vigie, T0).Errors.Should().ContainSingle(e => e.Code == SupervisionErrors.AgentNotAllowedCode);
        alert.State.Should().Be(AlertState.New);
        alert.Severity.Should().Be(Severity.S1);
    }

    [Fact]
    public void Changer_la_gravite_exige_une_personne_et_un_motif_trace()
    {
        Alert alert = CellAlert(Severity.S3);

        alert.ChangeSeverity(Severity.S1, " ", Personne, T0).Errors.Should().ContainSingle(e => e.Code == SupervisionErrors.ReasonRequiredCode);
        alert.ChangeSeverity(Severity.S1, "30 cabinets touchés", Actor.Rule("x"), T0).Errors.Should().ContainSingle(e => e.Code == SupervisionErrors.HumanRequiredCode);

        alert.ChangeSeverity(Severity.S1, "30 cabinets touchés", Personne, T0).IsSuccess.Should().BeTrue();
        alert.Severity.Should().Be(Severity.S1);
        alert.InitialSeverity.Should().Be(Severity.S3);
        alert.Timeline[^1].Reason.Should().Be("30 cabinets touchés");
    }

    [Fact]
    public void Une_recidive_apres_resolution_rouvre_l_alerte()
    {
        Alert alert = CellAlert();
        alert.Acknowledge(Personne, T0.AddMinutes(2));
        alert.Resolve(Personne, T0.AddMinutes(10));
        alert.ClearDomainEvents();

        alert.RecordOccurrence(T0.AddHours(1));

        alert.State.Should().Be(AlertState.New);
        alert.AcknowledgedBy.Should().BeNull();
        alert.OpenedAt.Should().Be(T0.AddHours(1));
        alert.Occurrences.Should().Be(2);
        alert.DomainEvents.Should().ContainSingle();
    }

    [Fact]
    public void Une_alerte_resolue_est_cloturee_apres_sept_jours_calmes()
    {
        Alert alert = CellAlert();
        alert.Resolve(Personne, T0);

        alert.CloseIfQuiet(T0.AddDays(6)).Should().BeFalse();
        alert.CloseIfQuiet(T0.AddDays(7)).Should().BeTrue();
        alert.State.Should().Be(AlertState.Closed);
    }

    [Fact]
    public void Une_portee_de_dossier_sans_cabinet_est_refusee() =>
        AlertScope.ForDossier("mut-fr-01", Guid.Empty, Guid.CreateVersion7()).Errors
            .Should().ContainSingle(e => e.Code == SupervisionErrors.ScopeIncompleteCode);
}
