using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Mail;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Reminders;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.Reminders;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;
using Lettra.Exports.Application.Insights;
using Lettra.Exports.Application.Reminders;

namespace Lettra.Exports.Tests.Reminders;

/// <summary>
/// Avant le retard sur des ports en mémoire : le rappel avant échéance ciblé par la météo des paiements, l'échéancier
/// qui remplace l'escalade, les signaux pour « Décisions à prendre » et les cas d'usage de l'écran.
/// </summary>
public sealed partial class AvantLeRetardServiceTests
{
    // Lundi 19 octobre 2026, 10 h à Paris.
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = new(2026, 10, 19);

    private static readonly CustomerId Durand = new(Guid.NewGuid());

    private static readonly CustomerId Berlin = new(Guid.NewGuid());

    private static readonly CustomerId Dupont = new(Guid.NewGuid());

    private readonly ReminderFakes _f = new(Now);

    private readonly DossierId _dossier = new(Guid.NewGuid());

    public AvantLeRetardServiceTests()
    {
        _f.Context.Customers[Durand] = new ReminderCustomer(Durand, "Durand SARL", "compta@durand.fr", "fr-FR", IsProfessional: true);
        _f.Context.Customers[Berlin] = new ReminderCustomer(Berlin, "Berlin GmbH", "ap@berlin.de", "en-GB", IsProfessional: true);
        _f.Context.Customers[Dupont] = new ReminderCustomer(Dupont, "Marie Dupont", null, null, IsProfessional: false);
        _f.Settings.Open();
        _f.History.Habit(Durand, "Durand SARL", daysLate: 20, Today);
        _f.History.Habit(Berlin, "Berlin GmbH", daysLate: 0, Today);
    }

    private static ReminderPrevention Bounds => new(true, 3, true, 3, 300m);

    /// <summary>Une facture qui arrive à échéance dans <paramref name="dueIn"/> jours (négatif : échue), émise 30 jours avant.</summary>
    private ReceivableDocument Add(string number, CustomerId customer, int dueIn, decimal total = 1200m, decimal paid = 0m)
    {
        ReceivableDocument document = new(
            new InvoiceId(Guid.NewGuid()), InvoiceKind.Invoice, number, customer, $"Nom de {number}", Today.AddDays(dueIn - 30), Today.AddDays(dueIn), total, 0m, paid, null);
        _f.Receivables.Documents.Add(document);
        return document;
    }

    private void Pay(ReceivableDocument document, decimal paid) =>
        _f.Receivables.Documents[_f.Receivables.Documents.IndexOf(document)] = document with { PaidInclTax = paid };

    /// <summary>Échéancier en 2 fois demandé le 30 septembre pour une facture du 30 août : échéances le 15 et le 30 octobre.</summary>
    private PaymentPlan Plan(ReceivableDocument d, DateOnly? on = null, int count = 2)
    {
        DateOnly requested = on ?? Today.AddDays(-19);
        PaymentPlanSubject subject = new(d.InvoiceId, d.CustomerId, d.Number, d.Kind, d.IssueDate, d.Outstanding, true, false, false);
        PaymentPlan plan = PaymentPlan.Request(subject, Bounds, count, "Jean Durand", true, null, Guid.Empty, requested, new DateTimeOffset(requested, TimeOnly.MinValue, TimeSpan.Zero)).Value;
        _f.Plans.All.Add(plan);
        return plan;
    }

    [Fact]
    public async Task Le_rappel_avant_echeance_vise_les_clients_sous_la_pluie_et_respecte_l_opposition()
    {
        Add("F-10", Durand, 3);
        Add("F-11", Berlin, 3);
        Add("F-12", Dupont, 3);
        Add("F-13", Durand, 20);

        ReminderOverviewResponse overview = await _f.Service.OverviewAsync(default);

        overview.Upcoming.Should().ContainSingle().Which.Should().Match<UpcomingReminderResponse>(u =>
            u.Number == "F-10" && u.Weather == PaymentWeatherDto.Rainy && u.PlannedOn == Today && u.DueToday && u.BestDay == null && u.SentAt == null
            && u.DueDate == Today.AddDays(3) && u.CustomerName == "Durand SARL" && u.Language == "fr-FR" && u.Outstanding == 1200m);
        overview.DueToday.Should().Be(1, "le rappel avant échéance compte dans les relances du jour");
        overview.Invoices.Should().BeEmpty();

        _f.Objections.All.Add(PaymentTargetingObjection.Record(Durand, Guid.Empty, Now));
        (await _f.Service.OverviewAsync(default)).Upcoming.Should().BeEmpty("le client s'est opposé au ciblage");
    }

    [Fact]
    public async Task Sans_rappel_avant_echeance_la_meteo_n_est_pas_lue()
    {
        _f.Settings.Open(beforeDue: false);
        Add("F-10", Durand, 3);

        ReminderOverviewResponse overview = await _f.Service.OverviewAsync(default);

        overview.Upcoming.Should().BeEmpty();
        _f.History.Reads.Should().Be(0);
    }

    [Fact]
    public async Task Le_rappel_part_au_meilleur_jour_du_client()
    {
        CustomerId client = new(Guid.NewGuid());
        _f.Context.Customers[client] = new ReminderCustomer(client, "Hôtel Lefèvre", "compta@lefevre.fr", "fr-FR", IsProfessional: true);
        DateOnly[] tuesdays = [new(2026, 6, 2), new(2026, 6, 16)];
        DateOnly[] thursdays = [new(2026, 6, 4), new(2026, 6, 18)];
        foreach ((DateOnly issued, int days) in tuesdays.Select(d => (d, 40)).Concat(thursdays.Select(d => (d, 60))))
        {
            _f.History.Invoices.Add(new PaymentHistoryInvoice(Guid.NewGuid(), client.Value, "Hôtel Lefèvre", $"H-{issued}", issued, issued.AddDays(30), 500m, 0m,
                [new PaymentHistoryEntry(issued.AddDays(days), 500m)]));
        }

        Add("F-30", client, 10);

        UpcomingReminderResponse row = (await _f.Service.OverviewAsync(default)).Upcoming.Single();

        (row.Weather, row.BestDay, row.PlannedOn, row.DueToday).Should().Be((PaymentWeatherDto.Rainy, (DayOfWeek?)DayOfWeek.Tuesday, new DateOnly(2026, 10, 20), false));
    }

    [Fact]
    public async Task Envoyer_le_rappel_joint_le_qr_code_et_cree_le_lien_de_l_espace_client()
    {
        ReceivableDocument invoice = Add("F-10", Durand, 3);
        Guid user = Guid.NewGuid();

        Result<PaymentReminder> sent = await _f.Service.SendAsync(invoice.InvoiceId, ReminderChannel.Email, ReminderTrigger.User, user, default);

        sent.Value.Should().Match<PaymentReminder>(r => r.Level == ReminderLevel.BeforeDue && r.Installment == null && r.DaysOverdue == 0 && r.Recipient == "compta@durand.fr");
        EmailMessage mail = _f.Email.Sent.Single();
        mail.Subject.Should().Be("Votre facture F-10 arrive à échéance le 22 octobre 2026");
        mail.TextBody.Should().Contain("https://lettra.example/espace-client/").And.Contain("vous pouvez y demander un échéancier").And.Contain("Le QR code joint");
        mail.Attachments.Should().ContainSingle().Which.Should().Match<EmailAttachment>(a =>
            a.FileName == "virement-F-10.png" && a.ContentType == "image/png" && a.Content.Length == 3);
        _f.Links.All.Should().ContainSingle().Which.CustomerId.Should().Be(Durand);

        UpcomingReminderResponse row = (await _f.Service.OverviewAsync(default)).Upcoming.Single();
        (row.SentAt, row.DueToday).Should().Be(((DateTimeOffset?)Now, false));
        (await _f.Service.DraftAsync(invoice.InvoiceId, default)).Errors.Single().Code.Should().Be(ReminderErrors.InvoiceNotOverdueCode, "un seul rappel avant échéance");
    }

    [Fact]
    public async Task L_apercu_annonce_le_lien_sans_le_creer_et_ne_propose_pas_d_echeancier_a_un_particulier()
    {
        _f.History.Habit(Dupont, "Marie Dupont", daysLate: 25, Today);
        ReceivableDocument invoice = Add("F-12", Dupont, 2);

        ReminderPreviewResponse preview = (await new GetReminderPreviewHandler(_f.Service).HandleAsync(new GetReminderPreviewQuery(_dossier, invoice.InvoiceId.Value), default)).Value;

        (preview.Level, preview.Recipient).Should().Be((ReminderLevelDto.BeforeDue, null));
        preview.Body.Should().Contain("(lien créé à l'envoi, valable 30 jours)").And.NotContain("échéancier");
        _f.Links.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Sans_espace_client_ni_qr_code_le_rappel_part_sans_eux()
    {
        _f.Portal.IsEnabled = false;
        _f.Qr.Draws = false;
        ReceivableDocument invoice = Add("F-11", Durand, 1);

        (await _f.Service.SendAsync(invoice.InvoiceId, ReminderChannel.Email, ReminderTrigger.User, Guid.NewGuid(), default)).IsSuccess.Should().BeTrue();

        EmailMessage mail = _f.Email.Sent.Single();
        mail.TextBody.Should().NotContain("espace client").And.NotContain("QR code").And.Contain("IBAN FR76");
        mail.Attachments.Should().BeNull();
        _f.Links.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Le_rappel_note_comme_parti_autrement_ne_cree_pas_de_lien()
    {
        ReceivableDocument invoice = Add("F-10", Durand, 3);

        (await _f.Service.SendAsync(invoice.InvoiceId, ReminderChannel.Manual, ReminderTrigger.User, Guid.NewGuid(), default)).Value.Level.Should().Be(ReminderLevel.BeforeDue);

        _f.Links.All.Should().BeEmpty();
        _f.Email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task La_passe_du_jour_envoie_les_rappels_avant_echeance_dus()
    {
        _f.History.Habit(Dupont, "Marie Dupont", daysLate: 25, Today);
        Add("F-10", Durand, 3);
        Add("F-12", Dupont, 3);
        Add("F-13", Durand, 9);

        RunRemindersResponse run = (await _f.Service.RunDueAsync(ReminderTrigger.Schedule, Guid.Empty, default)).Value;

        run.Should().Be(new RunRemindersResponse(2, 1, 0, 1));
        _f.Reminders.All.Should().ContainSingle().Which.Level.Should().Be(ReminderLevel.BeforeDue);
        _f.Email.Sent.Single().Attachments.Should().ContainSingle();
        _f.Links.All.Should().ContainSingle("la passe automatique crée aussi le lien, au nom du compte de service");
    }

    [Fact]
    public async Task Sous_echeancier_l_escalade_s_arrete_et_l_echeance_manquee_a_sa_relance()
    {
        ReceivableDocument invoice = Add("F-20", Durand, -20);
        PaymentPlan plan = Plan(invoice);
        plan.Installments.Select(i => (i.DueDate, i.Amount)).Should().Equal((new DateOnly(2026, 10, 15), 600m), (new DateOnly(2026, 10, 30), 600m));

        ReminderInvoiceResponse row = (await _f.Service.OverviewAsync(default)).Invoices.Single();

        row.Should().Match<ReminderInvoiceResponse>(r => r.DueDate == new DateOnly(2026, 10, 15) && r.DaysOverdue == 4 && r.Outstanding == 600m
            && r.NextLevel == ReminderLevelDto.MissedInstallment && r.DueToday && r.PaymentPlanId == plan.Id && r.Installment == 1);

        Result<PaymentReminder> sent = await _f.Service.SendAsync(invoice.InvoiceId, ReminderChannel.Email, ReminderTrigger.User, Guid.NewGuid(), default);

        (sent.Value.Level, sent.Value.Installment, sent.Value.Outstanding).Should().Be((ReminderLevel.MissedInstallment, 1, 600m));
        EmailMessage mail = _f.Email.Sent.Single();
        mail.Subject.Should().Be("Échéance 1 de votre échéancier : facture F-20");
        mail.TextBody.Should().Contain("Rappel du calendrier :").And.Contain("https://lettra.example/espace-client/").And.NotContain("demander un échéancier");
        mail.Attachments!.Single().FileName.Should().Be("virement-F-20.png");

        ReminderInvoiceResponse after = (await _f.Service.OverviewAsync(default)).Invoices.Single();
        (after.NextLevel, after.DueToday).Should().Be((null, false));
        (await _f.Service.RunDueAsync(ReminderTrigger.Schedule, Guid.Empty, default)).Value.Due.Should().Be(0, "une seule relance par échéance manquée");
    }

    [Fact]
    public async Task Une_echeance_manquee_attend_le_delai_du_rappel_amical()
    {
        ReceivableDocument invoice = Add("F-20", Durand, -20);
        Plan(invoice);
        _f.Clock.Now = new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);

        ReminderInvoiceResponse row = (await _f.Service.OverviewAsync(default)).Invoices.Single();

        (row.DaysOverdue, row.NextLevel, row.DueToday).Should().Be((2, (ReminderLevelDto?)ReminderLevelDto.MissedInstallment, false));
    }

    [Fact]
    public async Task Un_echeancier_tenu_ne_relance_pas_et_ecarte_le_rappel_avant_echeance()
    {
        ReceivableDocument late = Add("F-20", Durand, -20);
        Plan(late);
        Pay(late, 600m);
        ReceivableDocument coming = Add("F-21", Durand, 3);
        Plan(coming, Today);

        ReminderOverviewResponse overview = await _f.Service.OverviewAsync(default);

        overview.Invoices.Should().BeEmpty();
        overview.Upcoming.Should().BeEmpty("une facture sous échéancier n'a pas de rappel avant échéance");
        overview.DueToday.Should().Be(0);
    }

    [Fact]
    public async Task Un_echeancier_non_tenu_rend_la_facture_a_l_escalade_depuis_sa_derniere_echeance()
    {
        ReceivableDocument invoice = Add("F-20", Durand, -20);
        Plan(invoice);
        _f.Reminders.Seed(invoice, ReminderLevel.MissedInstallment, Today, installment: 1);

        _f.Clock.Now = new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);
        ReminderInvoiceResponse early = (await _f.Service.OverviewAsync(default)).Invoices.Single();
        _f.Clock.Now = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
        ReminderInvoiceResponse due = (await _f.Service.OverviewAsync(default)).Invoices.Single();

        early.Should().Match<ReminderInvoiceResponse>(r => r.DueDate == new DateOnly(2026, 10, 30) && r.DaysOverdue == 4 && r.Outstanding == 1200m
            && r.NextLevel == ReminderLevelDto.Reminder && !r.DueToday && r.Installment == null);
        (due.DaysOverdue, due.NextLevel, due.DueToday).Should().Be((21, (ReminderLevelDto?)ReminderLevelDto.Reminder, true));
        (await _f.Service.DraftAsync(invoice.InvoiceId, default)).Value.Draft.Level.Should().Be(ReminderLevel.Reminder);
    }

    [Fact]
    public async Task Un_echeancier_arrete_rend_la_facture_a_ses_relances_habituelles()
    {
        ReceivableDocument invoice = Add("F-20", Durand, -20);
        Plan(invoice).Cancel(Guid.Empty, Now);

        ReminderInvoiceResponse row = (await _f.Service.OverviewAsync(default)).Invoices.Single();

        (row.DueDate, row.DaysOverdue, row.NextLevel, row.PaymentPlanId).Should().Be((Today.AddDays(-20), 20, (ReminderLevelDto?)ReminderLevelDto.Friendly, null));

        _f.Reminders.Seed(invoice, ReminderLevel.MissedInstallment, Today, installment: 1);
        ReminderInvoiceResponse reminded = (await _f.Service.OverviewAsync(default)).Invoices.Single();
        (reminded.NextLevel, reminded.DueToday).Should().Be((ReminderLevelDto.Reminder, false), "la relance d'échéance a tenu lieu de rappel amical");
    }

    [Fact]
    public async Task La_vue_montre_les_echeanciers_en_cours_et_recents_et_les_oppositions_par_nom()
    {
        ReceivableDocument late = Add("F-20", Durand, -20);
        PaymentPlan running = Plan(late);
        ReceivableDocument paid = Add("F-22", Berlin, -20, paid: 1200m);
        Plan(paid with { PaidInclTax = 0m });
        PaymentPlan old = Plan(Add("F-23", Berlin, -20), Today.AddDays(-100));
        old.Cancel(Guid.Empty, Now.AddDays(-99));
        _f.Objections.All.Add(PaymentTargetingObjection.Record(Durand, Guid.Empty, Now));
        _f.Objections.All.Add(PaymentTargetingObjection.Record(Berlin, Guid.Empty, Now));
        _f.Objections.All.Add(PaymentTargetingObjection.Record(new CustomerId(Guid.NewGuid()), Guid.Empty, Now));

        ReminderOverviewResponse overview = await _f.Service.OverviewAsync(default);

        overview.Plans.Select(p => (p.InvoiceNumber, p.Status)).Should().BeEquivalentTo(
            [("F-20", PaymentPlanStatusDto.Late), ("F-22", PaymentPlanStatusDto.Completed)]);
        PaymentPlanResponse row = overview.Plans.Single(p => p.Id == running.Id);
        row.Should().Match<PaymentPlanResponse>(p => p.CustomerName == "Nom de F-20" && p.Total == 1200m && p.Outstanding == 1200m && !p.Acknowledged
            && p.RequestedBy == "Jean Durand" && p.Version == 0);
        row.Installments.Should().Equal(
            new PaymentPlanInstallmentResponse(1, new DateOnly(2026, 10, 15), 600m, false, true, 600m),
            new PaymentPlanInstallmentResponse(2, new DateOnly(2026, 10, 30), 600m, false, false, 600m));
        overview.Objections.Select(o => o.CustomerName).Should().Equal("", "Berlin GmbH", "Durand SARL");
    }

    [Fact]
    public async Task Les_signaux_disent_les_rappels_du_jour_les_echeances_manquees_et_les_echeanciers_a_voir()
    {
        ReceivableDocument coming = Add("F-10", Durand, 3);
        ReceivableDocument late = Add("F-20", Durand, -20);
        PaymentPlan plan = Plan(late);

        IReadOnlyList<PaymentPreventionSignal> signals =
            (await new GetPaymentPreventionSignalsHandler(_f.Service).HandleAsync(new GetPaymentPreventionSignalsQuery(_dossier), default)).Value;

        signals.Select(s => (s.Kind, s.Subject, s.Impact, s.DueOn, s.Link)).Should().Equal(
            (PaymentPreventionSignals.BeforeDueKind, coming.InvoiceId.Value, 1200m, Today.AddDays(3), "/reminders"),
            (PaymentPreventionSignals.MissedInstallmentKind, late.InvoiceId.Value, 600m, new DateOnly(2026, 10, 15), "/reminders"),
            (PaymentPreventionSignals.PlanToReviewKind, plan.Id, 1200m, new DateOnly(2026, 10, 15), "/reminders"));
        signals[0].Facts.Should().Equal(new Dictionary<string, string> { ["numero"] = "F-10", ["client"] = "Durand SARL", ["echeance"] = "2026-10-22" });
        signals[1].Facts["rang"].Should().Be("1");
        signals[2].Facts.Should().Equal(new Dictionary<string, string> { ["numero"] = "F-20", ["client"] = "Nom de F-20", ["echeances"] = "2" });

        plan.Acknowledge(Guid.Empty, Now);
        (await _f.Service.SignalsAsync(default)).Select(s => s.Kind).Should().NotContain(PaymentPreventionSignals.PlanToReviewKind);
    }

    [Fact]
    public async Task Le_reglage_change_le_rappel_et_les_bornes_et_refuse_des_bornes_hors_limites()
    {
        UpdateReminderSettingsHandler handler = new(_f.Settings, _f.Service, _f.UnitOfWork, _f.User, _f.Clock);
        ReminderPreventionDto prevention = new(false, 5, true, 2, 150m);

        Result<ReminderSettingsResponse> saved = await handler.HandleAsync(new UpdateReminderSettingsCommand(_dossier, new UpdateReminderSettingsRequest
        {
            Delays = [3, 15, 30, 45],
            MinimumGapDays = 7,
            Prevention = prevention,
        }), default);
        Result<ReminderSettingsResponse> refused = await handler.HandleAsync(new UpdateReminderSettingsCommand(_dossier, new UpdateReminderSettingsRequest
        {
            Delays = [3, 15, 30, 45],
            MinimumGapDays = 7,
            Prevention = prevention with { PaymentPlanMaxInstallments = 5 },
        }), default);

        saved.Value.Prevention.Should().Be(prevention);
        refused.Errors.Single().Code.Should().Be(ReminderErrors.PaymentPlanBoundsInvalidCode);
        _f.Settings.Stored!.Prevention.Should().Be(new ReminderPrevention(false, 5, true, 2, 150m));
    }

    [Fact]
    public async Task L_echeancier_d_une_facture_se_lit_avec_son_suivi()
    {
        ReceivableDocument invoice = Add("F-20", Durand, -20);
        GetInvoicePaymentPlanHandler handler = new(_f.Plans, _f.Receivables, _f.Clock);

        (await handler.HandleAsync(new GetInvoicePaymentPlanQuery(_dossier, invoice.InvoiceId.Value), default)).Value.Plan.Should().BeNull();

        PaymentPlan plan = Plan(invoice);
        Pay(invoice, 600m);
        PaymentPlanResponse row = (await handler.HandleAsync(new GetInvoicePaymentPlanQuery(_dossier, invoice.InvoiceId.Value), default)).Value.Plan!;

        (row.Id, row.Status, row.Outstanding, row.Installments[0].Paid).Should().Be((plan.Id, PaymentPlanStatusDto.OnTrack, 600m, true));
    }

    private PaymentPlanDecisions Decisions => new(_f.Plans, _f.Receivables, _f.Alerts, _f.UnitOfWork, _f.User, _f.Clock);

    [Fact]
    public async Task Prendre_en_compte_un_echeancier_leve_l_alerte_quand_plus_aucun_n_attend()
    {
        PaymentPlan first = Plan(Add("F-20", Durand, -20));
        PaymentPlan second = Plan(Add("F-21", Berlin, -20));
        AcknowledgePaymentPlanHandler handler = new(Decisions);

        PaymentPlanResponse seen = (await handler.HandleAsync(new AcknowledgePaymentPlanCommand(_dossier, first.Id, new PaymentPlanDecisionRequest(0)), default)).Value;
        await handler.HandleAsync(new AcknowledgePaymentPlanCommand(_dossier, second.Id, new PaymentPlanDecisionRequest(0)), default);

        (seen.Acknowledged, seen.Version, seen.Status).Should().Be((true, 1L, PaymentPlanStatusDto.Late));
        first.AcknowledgedBy.Should().Be(_f.User.UserId);
        _f.AlertPublisher.Signals.Select(s => (s.Rule, s.Firing, s.Dossier)).Should().Equal(
            (DossierAlertRule.PaymentPlanToReview, true, _f.Tenant.Current.DossierId),
            (DossierAlertRule.PaymentPlanToReview, false, _f.Tenant.Current.DossierId));
        _f.UnitOfWork.Saves.Should().Be(2);
    }

    [Fact]
    public async Task Arreter_un_echeancier_se_fait_une_fois_a_la_version_lue()
    {
        PaymentPlan plan = Plan(Add("F-20", Durand, -20));
        CancelPaymentPlanHandler handler = new(Decisions);

        Result<PaymentPlanResponse> stale = await handler.HandleAsync(new CancelPaymentPlanCommand(_dossier, plan.Id, new PaymentPlanDecisionRequest(3)), default);
        Result<PaymentPlanResponse> missing = await handler.HandleAsync(new CancelPaymentPlanCommand(_dossier, plan.Id, null!), default);
        Result<PaymentPlanResponse> cancelled = await handler.HandleAsync(new CancelPaymentPlanCommand(_dossier, plan.Id, new PaymentPlanDecisionRequest(0)), default);
        Result<PaymentPlanResponse> again = await handler.HandleAsync(new CancelPaymentPlanCommand(_dossier, plan.Id, new PaymentPlanDecisionRequest(1)), default);
        Result<PaymentPlanResponse> unknown = await handler.HandleAsync(new CancelPaymentPlanCommand(_dossier, Guid.NewGuid(), new PaymentPlanDecisionRequest(0)), default);

        stale.Errors.Single().Kind.Should().Be(ErrorKind.PreconditionFailed);
        missing.Errors.Single().Kind.Should().Be(ErrorKind.PreconditionFailed);
        (cancelled.Value.Status, cancelled.Value.Acknowledged, cancelled.Value.CancelledAt).Should().Be((PaymentPlanStatusDto.Cancelled, true, (DateTimeOffset?)Now));
        again.Errors.Single().Code.Should().Be(PaymentPlanErrors.AlreadyCancelledCode);
        unknown.Errors.Single().Code.Should().Be(PaymentPlanErrors.NotFoundCode);
        _f.UnitOfWork.Saves.Should().Be(1);
    }

    [Fact]
    public async Task Hors_d_un_dossier_l_alerte_n_est_pas_publiee()
    {
        _f.Tenant.IsResolved = false;
        Plan(Add("F-20", Durand, -20));

        await _f.Alerts.PublishAsync(default);

        _f.AlertPublisher.Signals.Should().BeEmpty();
    }

    [Fact]
    public async Task L_opposition_au_ciblage_se_note_et_se_retire_une_fois()
    {
        SetTargetingObjectionHandler handler = new(_f.Objections, _f.Context, _f.UnitOfWork, _f.User, _f.Clock);

        Result unknown = await handler.HandleAsync(new SetTargetingObjectionCommand(_dossier, Guid.NewGuid(), new SetTargetingObjectionRequest(true)), default);
        await handler.HandleAsync(new SetTargetingObjectionCommand(_dossier, Durand.Value, null!), default);
        await handler.HandleAsync(new SetTargetingObjectionCommand(_dossier, Durand.Value, new SetTargetingObjectionRequest(true)), default);

        unknown.Errors.Single().Code.Should().Be(CustomerErrors.NotFoundCode);
        _f.Objections.All.Should().ContainSingle().Which.Should().Match<PaymentTargetingObjection>(o =>
            o.CustomerId == Durand && o.RecordedBy == _f.User.UserId && o.RecordedAt == Now);
        _f.UnitOfWork.Saves.Should().Be(1);

        await handler.HandleAsync(new SetTargetingObjectionCommand(_dossier, Durand.Value, new SetTargetingObjectionRequest(false)), default);
        await handler.HandleAsync(new SetTargetingObjectionCommand(_dossier, Durand.Value, new SetTargetingObjectionRequest(false)), default);

        _f.Objections.All.Should().BeEmpty();
        _f.UnitOfWork.Saves.Should().Be(2);
    }
}
