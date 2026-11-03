using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Reminders;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.Reminders;

/// <summary>Avant le retard : les réglages du rappel avant échéance et les bornes de l'échéancier.</summary>
public sealed class ReminderPreventionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Par_defaut_le_rappel_avant_echeance_est_propose_a_j3_et_l_echeancier_est_coupe()
    {
        ReminderSettings settings = ReminderSettings.Default();

        settings.Prevention.Should().Be(new ReminderPrevention(true, 3, false, 3, 300m));
        ReminderPrevention.Default.Validate().IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void Un_rappel_hors_de_1_a_30_jours_avant_l_echeance_est_refuse(int days) =>
        (ReminderPrevention.Default with { BeforeDueDays = days }).Validate().Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ReminderErrors.BeforeDueDaysInvalidCode);

    [Theory]
    [InlineData(1, 300)]
    [InlineData(4, 300)]
    [InlineData(3, -1)]
    [InlineData(3, 1000001)]
    [InlineData(3, 10.005)]
    public void Des_bornes_d_echeancier_hors_limites_sont_refusees(int installments, decimal minimum) =>
        (ReminderPrevention.Default with { PaymentPlanMaxInstallments = installments, PaymentPlanMinimumAmount = minimum }).Validate().Errors
            .Should().ContainSingle().Which.Code.Should().Be(ReminderErrors.PaymentPlanBoundsInvalidCode);

    [Fact]
    public void Le_reglage_change_le_rappel_et_les_bornes_d_un_bloc()
    {
        ReminderSettings settings = ReminderSettings.Default();
        ReminderPrevention prevention = new(false, 5, true, 2, 150.5m);

        settings.Update(false, [3, 15, 30, 45], 7, prevention, Guid.NewGuid(), Now).IsSuccess.Should().BeTrue();

        settings.Prevention.Should().Be(prevention);
        settings.Version.Should().Be(1);
    }

    [Fact]
    public void Des_bornes_refusees_ne_changent_rien()
    {
        ReminderSettings settings = ReminderSettings.Default();

        settings.Update(true, [3, 15, 30, 45], 7, ReminderPrevention.Default with { BeforeDueDays = 0 }, Guid.NewGuid(), Now).IsFailure.Should().BeTrue();

        (settings.AutoSend, settings.Version, settings.Prevention).Should().Be((false, 0L, ReminderPrevention.Default));
    }
}

/// <summary>Les règles de l'échéancier en libre-service : mêmes bornes pour tous, plafond légal des délais de paiement.</summary>
public sealed class PaymentPlanRulesTests
{
    /// <summary>Lundi 19 octobre 2026.</summary>
    internal static readonly DateOnly Today = new(2026, 10, 19);

    /// <summary>Facture du 1er octobre : plafond légal au 15 décembre (fin de mois + 45 jours).</summary>
    internal static readonly PaymentPlanSubject Subject = new(
        new InvoiceId(Guid.NewGuid()), new CustomerId(Guid.NewGuid()), "F2026-0042", InvoiceKind.Invoice, new DateOnly(2026, 10, 1), 1000m, true, false, false);

    internal static readonly ReminderPrevention Open = ReminderPrevention.Default with { PaymentPlansEnabled = true };

    public static TheoryData<PaymentPlanSubject, ReminderPrevention, string> Refusals => new()
    {
        { Subject with { CustomerIsProfessional = false, Disputed = true }, ReminderPrevention.Default, PaymentPlanErrors.DisabledCode },
        { Subject with { CustomerIsProfessional = false, Disputed = true }, Open, PaymentPlanErrors.NotProfessionalCode },
        { Subject with { Kind = InvoiceKind.CreditNote, Disputed = true }, Open, PaymentPlanErrors.NothingDueCode },
        { Subject with { Outstanding = 0m, Disputed = true }, Open, PaymentPlanErrors.NothingDueCode },
        { Subject with { Disputed = true, HasActivePlan = true }, Open, PaymentPlanErrors.DisputedCode },
        { Subject with { HasActivePlan = true, Outstanding = 10m }, Open, PaymentPlanErrors.AlreadyPlannedCode },
        { Subject with { Outstanding = 299.99m }, Open, PaymentPlanErrors.BelowMinimumCode },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void La_premiere_raison_qui_empeche_l_echeancier_est_donnee(PaymentPlanSubject subject, ReminderPrevention bounds, string code)
    {
        PaymentPlanRules.Eligibility(subject, bounds).Errors.Should().ContainSingle().Which.Code.Should().Be(code);
        PaymentPlanRules.Schedule(subject, bounds, 2, Today).Errors.Should().ContainSingle().Which.Code.Should().Be(code);
        PaymentPlanRules.Options(subject, bounds, Today).Should().BeEmpty();
    }

    [Fact]
    public void Un_acompte_d_un_client_professionnel_au_dessus_du_minimum_peut_etre_etale() =>
        PaymentPlanRules.Eligibility(Subject with { Kind = InvoiceKind.DownPayment, Outstanding = 300m }, Open).IsSuccess.Should().BeTrue();

    [Fact]
    public void Trois_echeances_egales_finissent_au_plafond_legal_la_derniere_prenant_l_arrondi()
    {
        PaymentDelays.LegalCeiling(Subject.IssueDate).Should().Be(new DateOnly(2026, 12, 15));

        PaymentPlanRules.Schedule(Subject, Open, 3, Today).Value.Should().Equal(
            new PaymentPlanInstallment(1, new DateOnly(2026, 11, 7), 333.33m),
            new PaymentPlanInstallment(2, new DateOnly(2026, 11, 26), 333.33m),
            new PaymentPlanInstallment(3, new DateOnly(2026, 12, 15), 333.34m));
    }

    [Fact]
    public void Les_echeances_sont_espacees_d_un_mois_au_plus()
    {
        PaymentPlanSubject fresh = Subject with { IssueDate = Today };

        PaymentPlanRules.Schedule(fresh, Open, 2, Today).Value.Select(i => i.DueDate).Should().Equal(new DateOnly(2026, 11, 18), new DateOnly(2026, 12, 18));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(4, 3)]
    [InlineData(3, 2)]
    public void Un_nombre_d_echeances_hors_des_bornes_est_refuse(int count, int max) =>
        PaymentPlanRules.Schedule(Subject, Open with { PaymentPlanMaxInstallments = max }, count, Today).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(PaymentPlanErrors.InstallmentsInvalidCode);

    [Fact]
    public void Pres_du_plafond_legal_seules_les_options_qui_tiennent_une_semaine_d_ecart_sont_proposees()
    {
        DateOnly december = new(2026, 12, 1);

        PaymentPlanRules.Schedule(Subject, Open, 3, december).Errors.Should().ContainSingle().Which.Code.Should().Be(PaymentPlanErrors.NoRoomCode);
        PaymentPlanRules.Options(Subject, Open, december).Should().ContainSingle()
            .Which.Select(i => i.DueDate).Should().Equal(new DateOnly(2026, 12, 8), new DateOnly(2026, 12, 15));
        PaymentPlanRules.Options(Subject, Open, new DateOnly(2026, 12, 5)).Should().BeEmpty();
    }

    [Fact]
    public void Les_options_vont_de_deux_echeances_au_maximum_permis()
    {
        PaymentPlanRules.Options(Subject, Open, Today).Select(o => o.Count).Should().Equal(2, 3);
        PaymentPlanRules.Options(Subject, Open with { PaymentPlanMaxInstallments = 2 }, Today).Select(o => o.Count).Should().Equal(2);
    }

    [Fact]
    public void Les_regles_exigent_une_facture_et_des_bornes()
    {
        Action noSubject = () => PaymentPlanRules.Eligibility(null!, Open);
        Action noBounds = () => PaymentPlanRules.Options(Subject, null!, Today);

        noSubject.Should().Throw<ArgumentNullException>();
        noBounds.Should().Throw<ArgumentNullException>();
    }
}

/// <summary>L'échéancier : demande, prise en compte, arrêt et suivi lu sur le reste dû.</summary>
public sealed class PaymentPlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private static PaymentPlan Plan(int count = 3) =>
        PaymentPlan.Request(PaymentPlanRulesTests.Subject, PaymentPlanRulesTests.Open, count, "  Jean   Durand ", true, Guid.Empty, Guid.Empty, PaymentPlanRulesTests.Today, Now).Value;

    [Fact]
    public void La_demande_cree_l_echeancier_dans_les_bornes_avec_le_nom_du_demandeur()
    {
        Guid link = Guid.NewGuid();
        Guid user = Guid.NewGuid();

        PaymentPlan plan = PaymentPlan.Request(PaymentPlanRulesTests.Subject, PaymentPlanRulesTests.Open, 2, " Jean\tDurand ", true, link, user, PaymentPlanRulesTests.Today, Now).Value;

        (plan.InvoiceId, plan.CustomerId, plan.InvoiceNumber).Should().Be((PaymentPlanRulesTests.Subject.InvoiceId, PaymentPlanRulesTests.Subject.CustomerId, "F2026-0042"));
        (plan.Status, plan.Total, plan.RequestedBy, plan.RequestedAt, plan.PortalLinkId, plan.CreatedBy).Should().Be(
            (PaymentPlanStatus.Active, 1000m, "Jean Durand", Now, (Guid?)link, user));
        plan.Installments.Select(i => i.Amount).Should().Equal(500m, 500m);
        plan.FinalDueDate.Should().Be(new DateOnly(2026, 12, 14));
        (plan.IsAcknowledged, plan.Version).Should().Be((false, 0L));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" J ")]
    [InlineData("J'accepte les conditions de règlement et confirme agir pour le compte de la société cliente, en tant que représentant légal dûment habilité")]
    public void Un_nom_de_demandeur_absent_ou_hors_longueur_est_refuse(string? name) =>
        PaymentPlan.Request(PaymentPlanRulesTests.Subject, PaymentPlanRulesTests.Open, 2, name, true, null, Guid.Empty, PaymentPlanRulesTests.Today, Now).Errors
            .Should().ContainSingle().Which.Code.Should().Be(PaymentPlanErrors.RequesterInvalidCode);

    [Fact]
    public void Sans_accord_l_echeancier_n_est_pas_cree() =>
        PaymentPlan.Request(PaymentPlanRulesTests.Subject, PaymentPlanRulesTests.Open, 2, "Jean Durand", false, null, Guid.Empty, PaymentPlanRulesTests.Today, Now).Errors
            .Should().ContainSingle().Which.Code.Should().Be(PaymentPlanErrors.ConsentRequiredCode);

    [Fact]
    public void Une_facture_qui_ne_peut_pas_etre_etalee_donne_sa_raison() =>
        PaymentPlan.Request(PaymentPlanRulesTests.Subject, ReminderPrevention.Default, 2, "Jean Durand", true, null, Guid.Empty, PaymentPlanRulesTests.Today, Now).Errors
            .Should().ContainSingle().Which.Code.Should().Be(PaymentPlanErrors.DisabledCode);

    [Fact]
    public void La_demande_exige_une_facture()
    {
        Action act = () => PaymentPlan.Request(null!, PaymentPlanRulesTests.Open, 2, "Jean Durand", true, null, Guid.Empty, PaymentPlanRulesTests.Today, Now);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Prendre_en_compte_deux_fois_ne_change_que_la_premiere_fois()
    {
        PaymentPlan plan = Plan();
        Guid first = Guid.NewGuid();

        plan.Acknowledge(first, Now.AddHours(1));
        plan.Acknowledge(Guid.NewGuid(), Now.AddHours(2));

        (plan.IsAcknowledged, plan.AcknowledgedBy, plan.AcknowledgedAt, plan.Version).Should().Be((true, (Guid?)first, (DateTimeOffset?)Now.AddHours(1), 1L));
    }

    [Fact]
    public void Arreter_l_echeancier_le_marque_vu_et_ne_se_fait_qu_une_fois()
    {
        PaymentPlan plan = Plan();
        Guid user = Guid.NewGuid();

        plan.Cancel(user, Now.AddDays(1)).IsSuccess.Should().BeTrue();
        Result again = plan.Cancel(user, Now.AddDays(2));

        (plan.Status, plan.CancelledBy, plan.CancelledAt, plan.AcknowledgedBy, plan.Version).Should().Be(
            (PaymentPlanStatus.Cancelled, (Guid?)user, (DateTimeOffset?)Now.AddDays(1), (Guid?)user, 1L));
        again.Errors.Should().ContainSingle().Which.Code.Should().Be(PaymentPlanErrors.AlreadyCancelledCode);
    }

    [Fact]
    public void Arreter_un_echeancier_deja_vu_garde_qui_l_a_vu()
    {
        PaymentPlan plan = Plan();
        Guid seer = Guid.NewGuid();
        plan.Acknowledge(seer, Now);

        plan.Cancel(Guid.NewGuid(), Now.AddDays(1));

        (plan.AcknowledgedBy, plan.AcknowledgedAt, plan.Version).Should().Be(((Guid?)seer, (DateTimeOffset?)Now, 2L));
        plan.Progress(500m, new DateOnly(2027, 1, 1)).Lapsed.Should().BeFalse("un échéancier arrêté n'est plus suivi");
    }

    [Fact]
    public void Une_echeance_non_reglee_a_sa_date_est_manquee()
    {
        PaymentPlanProgress progress = Plan().Progress(1000m, new DateOnly(2026, 11, 8));

        progress.Installments.Select(i => (i.Paid, i.Missed, i.Remaining)).Should().Equal((false, true, 333.33m), (false, false, 333.33m), (false, false, 333.34m));
        (progress.Next!.Installment.Number, progress.LastMissed!.Installment.Number, progress.AmountLate, progress.PaidCount).Should().Be((1, 1, 333.33m, 0));
        (progress.Completed, progress.Lapsed, progress.Covered).Should().Be((false, false, 0m));
    }

    [Fact]
    public void Les_reglements_couvrent_les_echeances_dans_l_ordre()
    {
        PaymentPlan plan = Plan();

        plan.Progress(666.67m, new DateOnly(2026, 11, 8)).Installments.Select(i => (i.Paid, i.Missed)).Should().Equal((true, false), (false, false), (false, false));

        PaymentPlanProgress partial = plan.Progress(500m, new DateOnly(2026, 11, 27));
        partial.Installments.Select(i => (i.Paid, i.Missed, i.Remaining)).Should().Equal((true, false, 0m), (false, true, 166.66m), (false, false, 333.34m));
        (partial.AmountLate, partial.PaidCount, partial.Next!.Installment.Number).Should().Be((166.66m, 1, 2));
    }

    [Fact]
    public void Une_facture_soldee_termine_l_echeancier()
    {
        PaymentPlanProgress progress = Plan().Progress(0m, new DateOnly(2027, 1, 1));

        (progress.Completed, progress.Lapsed, progress.PaidCount, progress.AmountLate).Should().Be((true, false, 3, 0m));
        progress.Next.Should().BeNull();
    }

    [Fact]
    public void Apres_la_derniere_echeance_un_reste_du_dit_l_echeancier_non_tenu()
    {
        PaymentPlanProgress progress = Plan().Progress(100m, new DateOnly(2026, 12, 16));

        (progress.Lapsed, progress.Completed, progress.LastMissed!.Installment.Number, progress.AmountLate).Should().Be((true, false, 3, 100m));
    }

    [Fact]
    public void Un_reste_du_qui_a_augmente_ne_couvre_rien() =>
        Plan().Progress(1200m, PaymentPlanRulesTests.Today).Covered.Should().Be(0m);

    [Fact]
    public void La_facture_est_attendue_a_la_prochaine_echeance_non_couverte()
    {
        PaymentPlan plan = Plan();

        plan.NextDueDate(1000m).Should().Be(new DateOnly(2026, 11, 7));
        plan.NextDueDate(600m).Should().Be(new DateOnly(2026, 11, 26));
        plan.NextDueDate(0m).Should().Be(plan.FinalDueDate);
    }

    [Fact]
    public void L_opposition_au_ciblage_garde_qui_l_a_notee_et_quand()
    {
        CustomerId customer = new(Guid.NewGuid());
        Guid user = Guid.NewGuid();

        PaymentTargetingObjection objection = PaymentTargetingObjection.Record(customer, user, Now);

        (objection.CustomerId, objection.RecordedBy, objection.RecordedAt).Should().Be((customer, user, Now));
    }

    [Fact]
    public void Les_erreurs_de_l_echeancier_ont_un_code_et_une_nature_stables() =>
        new[]
        {
            PaymentPlanErrors.Disabled, PaymentPlanErrors.NotProfessional, PaymentPlanErrors.NothingDue, PaymentPlanErrors.Disputed,
            PaymentPlanErrors.AlreadyPlanned, PaymentPlanErrors.BelowMinimum, PaymentPlanErrors.NoRoom, PaymentPlanErrors.InstallmentsInvalid,
            PaymentPlanErrors.RequesterInvalid, PaymentPlanErrors.ConsentRequired, PaymentPlanErrors.NotFound, PaymentPlanErrors.AlreadyCancelled,
        }.Select(e => e.Kind).Should().Equal(
            ErrorKind.Conflict, ErrorKind.Conflict, ErrorKind.Conflict, ErrorKind.Conflict, ErrorKind.Conflict, ErrorKind.Conflict, ErrorKind.Conflict,
            ErrorKind.Validation, ErrorKind.Validation, ErrorKind.Validation, ErrorKind.NotFound, ErrorKind.Conflict);
}

/// <summary>Le rappel avant échéance et la relance d'échéance manquée dans le planificateur et la trace des relances.</summary>
public sealed class AvantLeRetardPlannerTests
{
    private static readonly DateOnly Issue = new(2026, 10, 1);

    /// <summary>Samedi 31 octobre 2026.</summary>
    private static readonly DateOnly Due = new(2026, 10, 31);

    [Fact]
    public void Un_rappel_avant_echeance_ne_compte_pas_dans_l_escalade()
    {
        ReminderPlanner.Next(ReminderLevel.BeforeDue).Should().Be(ReminderLevel.Friendly);
        ReminderPlanner.Due(new ReminderState(5, ReminderLevel.BeforeDue, Due.AddDays(-3)), ReminderSettings.Default(), Due.AddDays(5))
            .Should().Be(ReminderLevel.Friendly);
    }

    [Fact]
    public void Apres_une_echeance_manquee_l_escalade_reprend_a_la_relance() =>
        ReminderPlanner.Next(ReminderLevel.MissedInstallment).Should().Be(ReminderLevel.Reminder);

    [Theory]
    [InlineData(null, 2026, 10, 28)]
    [InlineData(DayOfWeek.Wednesday, 2026, 10, 28)]
    [InlineData(DayOfWeek.Tuesday, 2026, 10, 27)]
    [InlineData(DayOfWeek.Thursday, 2026, 10, 22)]
    public void Le_rappel_part_trois_jours_avant_ou_au_meilleur_jour_du_client_dans_la_semaine_qui_precede(DayOfWeek? best, int year, int month, int day) =>
        ReminderPlanner.BeforeDueOn(Issue, Due, 3, best).Should().Be(new DateOnly(year, month, day));

    [Fact]
    public void Une_facture_a_echeance_courte_n_a_pas_de_rappel_le_jour_de_son_emission()
    {
        ReminderPlanner.BeforeDueOn(new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 29), 3, null).Should().BeNull();
        ReminderPlanner.BeforeDueOn(new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 29), 3, DayOfWeek.Sunday)
            .Should().Be(new DateOnly(2026, 10, 26), "le meilleur jour tomberait le jour de l'émission : on garde J−3");
    }

    private static PaymentReminder Sent(ReminderLevel level, DateOnly on, int? installment = null, string? error = null) =>
        PaymentReminder.Record(
            new ReminderDraft(new InvoiceId(Guid.Empty), new CustomerId(Guid.Empty), "F-1", level, "fr-FR", "Objet", "Corps", 100m, 0, installment),
            ReminderChannel.Email, "x@y.fr", error, ReminderTrigger.User, Guid.Empty, new DateTimeOffset(on, TimeOnly.MinValue, TimeSpan.Zero), on);

    [Fact]
    public void La_trace_retient_la_derniere_relance_de_chaque_sorte_partie()
    {
        PaymentReminder beforeDue = Sent(ReminderLevel.BeforeDue, new DateOnly(2026, 10, 28));
        PaymentReminder friendly = Sent(ReminderLevel.Friendly, new DateOnly(2026, 11, 3));
        PaymentReminder reminder = Sent(ReminderLevel.Reminder, new DateOnly(2026, 11, 15));
        PaymentReminder second = Sent(ReminderLevel.MissedInstallment, new DateOnly(2026, 11, 28), 2);

        ReminderTrack track = ReminderTrack.Of([
            friendly, reminder, Sent(ReminderLevel.Final, new DateOnly(2026, 12, 1), error: "Email.SendFailed"), beforeDue,
            Sent(ReminderLevel.MissedInstallment, new DateOnly(2026, 11, 8), 1), second]);

        (track.LastEscalation, track.BeforeDue, track.LastInstallment, track.Last).Should().Be((reminder, beforeDue, second, second));
        new[] { 1, 2, 3 }.Select(track.InstallmentReminded).Should().Equal(true, true, false);
    }

    [Fact]
    public void Une_facture_sans_relance_a_une_trace_vide()
    {
        ReminderTrack track = ReminderTrack.Of([]);

        track.Should().Be(ReminderTrack.Empty);
        track.Last.Should().BeNull();
        track.InstallmentReminded(1).Should().BeFalse();
        track.Escalation(10).Should().Be(new ReminderState(10, null, null));
    }

    [Fact]
    public void Apres_un_echeancier_la_relance_d_echeance_tient_lieu_de_rappel_amical()
    {
        DateOnly missed = new(2026, 11, 8);

        ReminderTrack.Of([Sent(ReminderLevel.MissedInstallment, missed, 1)]).Escalation(20).Should().Be(new ReminderState(20, ReminderLevel.Friendly, missed));
        ReminderTrack.Of([Sent(ReminderLevel.Friendly, missed)]).Escalation(20).Should().Be(new ReminderState(20, ReminderLevel.Friendly, missed));
    }

    [Fact]
    public void Apres_un_echeancier_l_escalade_garde_le_plus_haut_niveau_et_la_relance_la_plus_recente()
    {
        DateOnly older = new(2026, 10, 10);
        DateOnly missed = new(2026, 11, 8);
        DateOnly newer = new(2026, 11, 20);

        ReminderTrack.Of([Sent(ReminderLevel.Reminder, older), Sent(ReminderLevel.MissedInstallment, missed, 1)]).Escalation(40)
            .Should().Be(new ReminderState(40, ReminderLevel.Reminder, missed));
        ReminderTrack.Of([Sent(ReminderLevel.Final, newer), Sent(ReminderLevel.MissedInstallment, missed, 1)]).Escalation(40)
            .Should().Be(new ReminderState(40, ReminderLevel.Final, newer));
    }

    [Fact]
    public void La_trace_exige_une_liste_de_relances()
    {
        Action act = () => ReminderTrack.Of(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
