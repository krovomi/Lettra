using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Reminders;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.Reminders;

public sealed class ReminderSettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Le_scenario_par_defaut_relance_a_j3_j15_j30_j45_sans_envoi_automatique()
    {
        ReminderSettings settings = ReminderSettings.Default();

        settings.AutoSend.Should().BeFalse();
        Enum.GetValues<ReminderLevel>().Select(settings.DelayOf).Should().Equal(3, 15, 30, 45);
        settings.MinimumGapDays.Should().Be(7);
        settings.Version.Should().Be(0);
    }

    [Fact]
    public void Un_reglage_valide_change_les_delais_et_la_version()
    {
        ReminderSettings settings = ReminderSettings.Default();
        Guid user = Guid.NewGuid();

        settings.Update(true, [5, 20, 40, 60], 10, user, Now).IsSuccess.Should().BeTrue();

        settings.AutoSend.Should().BeTrue();
        Enum.GetValues<ReminderLevel>().Select(settings.DelayOf).Should().Equal(5, 20, 40, 60);
        settings.MinimumGapDays.Should().Be(10);
        (settings.Version, settings.UpdatedBy, settings.UpdatedAt).Should().Be((1L, user, Now));
    }

    [Theory]
    [InlineData(new[] { 3, 15, 30 })]
    [InlineData(new[] { 0, 15, 30, 45 })]
    [InlineData(new[] { 3, 15, 15, 45 })]
    [InlineData(new[] { 3, 30, 15, 45 })]
    [InlineData(new[] { 3, 15, 30, 366 })]
    public void Des_delais_qui_ne_montent_pas_strictement_de_1_a_365_sont_refuses(int[] delays)
    {
        ReminderSettings settings = ReminderSettings.Default();

        settings.Update(false, delays, 7, Guid.NewGuid(), Now).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ReminderErrors.DelaysInvalidCode);
        settings.Version.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public void Un_ecart_hors_de_1_a_90_jours_est_refuse(int gap) =>
        ReminderSettings.Default().Update(false, [3, 15, 30, 45], gap, Guid.NewGuid(), Now).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ReminderErrors.GapInvalidCode);

    [Fact]
    public void Un_niveau_inconnu_n_a_pas_de_delai()
    {
        Action act = () => ReminderSettings.Default().DelayOf((ReminderLevel)9);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

public sealed class ReminderPlannerTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    private static readonly ReminderSettings Settings = ReminderSettings.Default();

    [Theory]
    [InlineData(2, null)]
    [InlineData(3, ReminderLevel.Friendly)]
    [InlineData(80, ReminderLevel.Friendly)]
    public void Une_facture_jamais_relancee_commence_par_le_rappel_amical(int daysOverdue, ReminderLevel? expected) =>
        ReminderPlanner.Due(new ReminderState(daysOverdue, null, null), Settings, Today).Should().Be(expected);

    [Fact]
    public void Le_niveau_suivant_attend_son_delai()
    {
        ReminderPlanner.Due(new ReminderState(14, ReminderLevel.Friendly, Today.AddDays(-11)), Settings, Today).Should().BeNull();
        ReminderPlanner.Due(new ReminderState(15, ReminderLevel.Friendly, Today.AddDays(-12)), Settings, Today).Should().Be(ReminderLevel.Reminder);
    }

    [Fact]
    public void Deux_relances_d_une_meme_facture_sont_espacees_de_l_ecart_minimal()
    {
        ReminderPlanner.Due(new ReminderState(60, ReminderLevel.Reminder, Today.AddDays(-6)), Settings, Today).Should().BeNull();
        ReminderPlanner.Due(new ReminderState(60, ReminderLevel.Reminder, Today.AddDays(-7)), Settings, Today).Should().Be(ReminderLevel.Final);
    }

    [Fact]
    public void Rien_ne_suit_la_mise_en_demeure()
    {
        ReminderPlanner.Due(new ReminderState(400, ReminderLevel.FormalNotice, Today.AddDays(-100)), Settings, Today).Should().BeNull();
        ReminderPlanner.Next(null).Should().Be(ReminderLevel.Friendly);
        ReminderPlanner.Next(ReminderLevel.Final).Should().Be(ReminderLevel.FormalNotice);
        ReminderPlanner.Next(ReminderLevel.FormalNotice).Should().BeNull();
    }

    [Fact]
    public void Le_planificateur_exige_un_scenario()
    {
        Action act = () => ReminderPlanner.Due(default, null!, Today);

        act.Should().Throw<ArgumentNullException>();
    }
}

public sealed class PaymentReminderTests
{
    private static readonly ReminderDraft Draft = new(
        new InvoiceId(Guid.NewGuid()), new CustomerId(Guid.NewGuid()), "F-2026-0001", ReminderLevel.Final, "fr-FR",
        new string('s', 400), new string('b', 9000), 1200m, 31);

    [Fact]
    public void Une_relance_partie_garde_la_trace_de_tout_ce_qui_a_ete_envoye()
    {
        DateTimeOffset now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
        Guid user = Guid.NewGuid();

        PaymentReminder reminder = PaymentReminder.Record(Draft, ReminderChannel.Email, "compta@client.fr", null, ReminderTrigger.Schedule, user, now, new DateOnly(2026, 10, 19));

        reminder.Outcome.Should().Be(ReminderOutcome.Sent);
        reminder.ErrorCode.Should().BeNull();
        (reminder.InvoiceId, reminder.CustomerId, reminder.InvoiceNumber, reminder.Level).Should().Be((Draft.InvoiceId, Draft.CustomerId, "F-2026-0001", ReminderLevel.Final));
        (reminder.Recipient, reminder.Language, reminder.Outstanding, reminder.DaysOverdue).Should().Be(("compta@client.fr", "fr-FR", 1200m, 31));
        (reminder.Trigger, reminder.UserId, reminder.OccurredAt, reminder.OccurredOn).Should().Be((ReminderTrigger.Schedule, user, now, new DateOnly(2026, 10, 19)));
        reminder.Subject.Should().HaveLength(PaymentReminder.MaxSubjectLength);
        reminder.Body.Should().HaveLength(PaymentReminder.MaxBodyLength);
    }

    [Fact]
    public void Un_envoi_en_echec_est_trace_avec_son_code()
    {
        PaymentReminder reminder = PaymentReminder.Record(
            Draft with { Subject = "Objet", Body = "Corps" }, ReminderChannel.Email, "x@y.fr", "Email.SendFailed", ReminderTrigger.User, Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateOnly.MinValue);

        (reminder.Outcome, reminder.ErrorCode, reminder.Subject, reminder.Body).Should().Be((ReminderOutcome.Failed, "Email.SendFailed", "Objet", "Corps"));
    }

    [Fact]
    public void Une_relance_sans_brouillon_est_refusee()
    {
        Action act = () => PaymentReminder.Record(null!, ReminderChannel.Manual, null, null, ReminderTrigger.User, Guid.Empty, default, default);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Les_erreurs_des_relances_ont_un_code_stable() =>
        new[] { ReminderErrors.InvoiceNotOverdue, ReminderErrors.CustomerEmailMissing, ReminderErrors.NoLevelLeft, ReminderErrors.CompanyProfileMissing }
            .Select(e => (e.Code, e.Kind)).Should().Equal(
                (ReminderErrors.InvoiceNotOverdueCode, ErrorKind.Conflict),
                (ReminderErrors.CustomerEmailMissingCode, ErrorKind.Conflict),
                (ReminderErrors.NoLevelLeftCode, ErrorKind.Conflict),
                (ReminderErrors.CompanyProfileMissingCode, ErrorKind.Conflict));
}
