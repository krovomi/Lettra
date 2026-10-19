using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Mail;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Common;
using Lettra.Contracts.Reminders;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Reminders;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;
using Lettra.Exports.Application.Reminders;

namespace Lettra.Exports.Tests.Reminders;

/// <summary>Les relances sur des ports en mémoire : chaque règle du service et des cas d'usage, sans base.</summary>
public sealed class PaymentReminderServiceTests
{
    // 19 octobre 2026, 10 h à Paris.
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = new(2026, 10, 19);

    private readonly Fakes _f = new(Now);

    private static readonly CustomerId Durand = new(Guid.NewGuid());

    private static readonly CustomerId Berlin = new(Guid.NewGuid());

    private static readonly CustomerId Dupont = new(Guid.NewGuid());

    public PaymentReminderServiceTests()
    {
        _f.Context.Customers[Durand] = new ReminderCustomer(Durand, "Durand SARL", "compta@durand.fr", "fr-FR", IsProfessional: true);
        _f.Context.Customers[Berlin] = new ReminderCustomer(Berlin, "Berlin GmbH", "ap@berlin.de", "en-GB", IsProfessional: true);
        _f.Context.Customers[Dupont] = new ReminderCustomer(Dupont, "Marie Dupont", null, null, IsProfessional: false);
    }

    private ReceivableDocument Add(string number, CustomerId customer, int daysOverdue, decimal total = 1200m, decimal paid = 0m, InvoiceKind kind = InvoiceKind.Invoice)
    {
        ReceivableDocument document = new(
            new InvoiceId(Guid.NewGuid()), kind, number, customer, $"Nom de {number}", Today.AddDays(-daysOverdue - 30), Today.AddDays(-daysOverdue),
            total, 0m, paid, null);
        _f.Receivables.Documents.Add(document);
        return document;
    }

    [Fact]
    public async Task Seules_les_factures_echues_et_non_soldees_sont_a_relancer_les_plus_en_retard_d_abord()
    {
        Add("F-1", Durand, 20);
        Add("F-2", Berlin, 50);
        Add("F-3", Durand, 60, paid: 1200m);
        Add("F-4", Durand, 0);
        Add("AV-1", Durand, 40, kind: InvoiceKind.CreditNote);
        ReceivableDocument noDueDate = new(new InvoiceId(Guid.NewGuid()), InvoiceKind.DownPayment, "AC-1", Durand, "Durand", Today.AddDays(-90), null, 500m, 0m, 0m, null);
        _f.Receivables.Documents.Add(noDueDate);
        _f.Context.Rates[_f.Receivables.Documents[1].InvoiceId] = 12m;

        ReminderOverviewResponse overview = await _f.Service.OverviewAsync(default);

        overview.Invoices.Select(i => (i.Number, i.DaysOverdue, i.NextLevel, i.DueToday)).Should().Equal(
            ("F-2", 50, ReminderLevelDto.Friendly, true),
            ("F-1", 20, ReminderLevelDto.Friendly, true));
        overview.Invoices[0].Should().Match<ReminderInvoiceResponse>(i =>
            i.Language == "en-US" && i.CustomerEmail == "ap@berlin.de" && i.PenaltyInterest == 19.73m && i.RecoveryIndemnity == 40m);
        overview.DueToday.Should().Be(2);
        overview.Settings.Should().BeEquivalentTo(new ReminderSettingsResponse(false, [3, 15, 30, 45], 7, true, 0));
    }

    [Fact]
    public async Task Sans_facture_due_la_vue_est_vide_sans_rien_lire_d_autre()
    {
        Add("F-1", Durand, 0);

        ReminderOverviewResponse overview = await _f.Service.OverviewAsync(default);

        overview.Invoices.Should().BeEmpty();
        _f.Context.CustomerReads.Should().Be(0);
    }

    [Fact]
    public async Task Un_client_inconnu_garde_le_nom_de_la_piece_et_est_traite_en_professionnel()
    {
        Add("F-9", new CustomerId(Guid.NewGuid()), 10);

        ReminderInvoiceResponse row = (await _f.Service.OverviewAsync(default)).Invoices.Single();

        (row.CustomerName, row.CustomerEmail, row.Language, row.RecoveryIndemnity).Should().Be(("Nom de F-9", null, "fr-FR", 40m));
    }

    [Fact]
    public async Task Une_facture_non_echue_n_a_pas_de_relance()
    {
        ReceivableDocument notDue = Add("F-1", Durand, 0);

        (await _f.Service.DraftAsync(notDue.InvoiceId, default)).Errors.Single().Code.Should().Be(ReminderErrors.InvoiceNotOverdueCode);
    }

    [Fact]
    public async Task Sans_fiche_entreprise_la_relance_ne_peut_pas_etre_signee()
    {
        ReceivableDocument due = Add("F-1", Durand, 10);
        _f.Context.Sender = null;

        (await _f.Service.DraftAsync(due.InvoiceId, default)).Errors.Single().Code.Should().Be(ReminderErrors.CompanyProfileMissingCode);
        (await _f.Service.RunDueAsync(ReminderTrigger.User, Guid.Empty, default)).Errors.Single().Code.Should().Be(ReminderErrors.CompanyProfileMissingCode);
    }

    [Fact]
    public async Task Apres_la_mise_en_demeure_il_n_y_a_plus_de_niveau()
    {
        ReceivableDocument due = Add("F-1", Durand, 200);
        _f.Reminders.Seed(due, ReminderLevel.FormalNotice, Today.AddDays(-30));

        (await _f.Service.DraftAsync(due.InvoiceId, default)).Errors.Single().Code.Should().Be(ReminderErrors.NoLevelLeftCode);
        (await _f.Service.OverviewAsync(default)).Invoices.Single().Should().Match<ReminderInvoiceResponse>(i =>
            i.LastLevel == ReminderLevelDto.FormalNotice && i.NextLevel == null && !i.DueToday);
    }

    [Fact]
    public async Task Un_envoi_par_e_mail_part_au_client_et_reste_dans_l_historique()
    {
        ReceivableDocument due = Add("F-1", Durand, 20);
        _f.Reminders.Seed(due, ReminderLevel.Friendly, Today.AddDays(-10));

        Result<PaymentReminder> sent = await _f.Service.SendAsync(due.InvoiceId, ReminderChannel.Email, ReminderTrigger.User, Guid.Empty, default);

        sent.IsSuccess.Should().BeTrue();
        sent.Value.Should().Match<PaymentReminder>(r => r.Level == ReminderLevel.Reminder && r.Recipient == "compta@durand.fr" && r.Outcome == ReminderOutcome.Sent);
        _f.Email.Sent.Single().Should().Match<EmailMessage>(m =>
            m.To.Value == "compta@durand.fr" && m.Subject == "Relance : facture F-1 impayée" && m.SenderName == "Atelier Martin");
        _f.Reminders.All.Should().HaveCount(2);
        _f.UnitOfWork.Saves.Should().Be(1);
    }

    [Fact]
    public async Task Un_client_sans_adresse_ou_une_installation_sans_e_mail_ne_laisse_aucune_trace()
    {
        ReceivableDocument noAddress = Add("F-1", Dupont, 20);
        ReceivableDocument due = Add("F-2", Durand, 20);

        (await _f.Service.SendAsync(noAddress.InvoiceId, ReminderChannel.Email, ReminderTrigger.User, Guid.Empty, default))
            .Errors.Single().Code.Should().Be(ReminderErrors.CustomerEmailMissingCode);
        _f.Email.IsConfigured = false;
        (await _f.Service.SendAsync(due.InvoiceId, ReminderChannel.Email, ReminderTrigger.User, Guid.Empty, default))
            .Errors.Single().Code.Should().Be(ErrorCodes.Email.NotConfigured);

        _f.Reminders.All.Should().BeEmpty();
        _f.UnitOfWork.Saves.Should().Be(0);
    }

    [Fact]
    public async Task Une_relance_notee_a_la_main_ne_demande_ni_adresse_ni_serveur()
    {
        ReceivableDocument due = Add("F-1", Dupont, 20);
        _f.Email.IsConfigured = false;

        Result<PaymentReminder> noted = await _f.Service.SendAsync(due.InvoiceId, ReminderChannel.Manual, ReminderTrigger.User, Guid.Empty, default);

        noted.Value.Should().Match<PaymentReminder>(r => r.Channel == ReminderChannel.Manual && r.Recipient == null && r.Outcome == ReminderOutcome.Sent);
        _f.Email.Sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData("compta@durand.fr", true, ErrorCodes.Email.SendFailed)]
    [InlineData("pas-une-adresse", false, SharedKernelErrors.EmailInvalidCode)]
    public async Task Un_envoi_refuse_est_garde_comme_echec(string address, bool serverFails, string recordedCode)
    {
        ReceivableDocument due = Add("F-1", Durand, 20);
        _f.Context.Customers[Durand] = _f.Context.Customers[Durand] with { Email = address };
        _f.Email.Fail = serverFails;

        Result<PaymentReminder> sent = await _f.Service.SendAsync(due.InvoiceId, ReminderChannel.Email, ReminderTrigger.User, Guid.Empty, default);

        sent.Errors.Single().Code.Should().Be(ErrorCodes.Email.SendFailed);
        _f.Reminders.All.Single().Should().Match<PaymentReminder>(r => r.Outcome == ReminderOutcome.Failed && r.ErrorCode == recordedCode);
    }

    [Fact]
    public async Task La_passe_du_jour_envoie_les_relances_dues_et_compte_le_reste()
    {
        Add("F-1", Durand, 20);
        Add("F-2", Berlin, 50);
        Add("F-3", Dupont, 30);
        ReceivableDocument recent = Add("F-4", Durand, 40);
        _f.Reminders.Seed(recent, ReminderLevel.Friendly, Today.AddDays(-2));
        Add("F-5", Durand, 1);

        Result<RunRemindersResponse> run = await _f.Service.RunDueAsync(ReminderTrigger.Schedule, Guid.Empty, default);

        run.Value.Should().Be(new RunRemindersResponse(Due: 3, Sent: 2, Failed: 0, WithoutEmail: 1));
        _f.Reminders.All.Where(r => r.Trigger == ReminderTrigger.Schedule).Select(r => r.InvoiceNumber).Should().BeEquivalentTo(["F-1", "F-2"]);
    }

    [Fact]
    public async Task La_passe_du_jour_compte_les_echecs_et_exige_un_serveur_d_envoi()
    {
        Add("F-1", Durand, 20);
        _f.Email.Fail = true;

        (await _f.Service.RunDueAsync(ReminderTrigger.User, Guid.Empty, default)).Value.Should().Be(new RunRemindersResponse(1, 0, 1, 0));

        _f.Email.IsConfigured = false;
        (await _f.Service.RunDueAsync(ReminderTrigger.User, Guid.Empty, default)).Errors.Single().Code.Should().Be(ErrorCodes.Email.NotConfigured);
    }

    [Fact]
    public async Task Les_cas_d_usage_de_l_ecran_traduisent_le_service()
    {
        DossierId dossier = new(Guid.NewGuid());
        ReceivableDocument due = Add("F-1", Berlin, 20);

        Result<ReminderPreviewResponse> preview = await new GetReminderPreviewHandler(_f.Service).HandleAsync(new GetReminderPreviewQuery(dossier, due.InvoiceId.Value), default);
        preview.Value.Should().Match<ReminderPreviewResponse>(p => p.Level == ReminderLevelDto.Friendly && p.Language == "en-US" && p.Recipient == "ap@berlin.de");

        SendReminderHandler send = new(_f.Service, _f.Context, _f.User);
        Result<PaymentReminderResponse> manual = await send.HandleAsync(new SendReminderCommand(dossier, due.InvoiceId.Value, new SendReminderRequest(ReminderChannelDto.Manual)), default);
        manual.Value.Should().Match<PaymentReminderResponse>(r => r.Channel == ReminderChannelDto.Manual && r.CustomerName == "Berlin GmbH" && r.Level == ReminderLevelDto.Friendly);

        Result<PaymentReminderResponse> refused = await send.HandleAsync(new SendReminderCommand(dossier, Guid.NewGuid(), null!), default);
        refused.Errors.Single().Code.Should().Be(ReminderErrors.InvoiceNotOverdueCode);

        (await new GetReminderOverviewHandler(_f.Service).HandleAsync(new GetReminderOverviewQuery(dossier), default)).Value.Invoices.Should().ContainSingle();
        (await new RunDueRemindersHandler(_f.Service, _f.User).HandleAsync(new RunDueRemindersCommand(dossier), default)).Value.Due.Should().Be(0);
    }

    [Fact]
    public async Task L_historique_borne_la_page_et_passe_les_filtres()
    {
        ReceivableDocument due = Add("F-1", Durand, 20);
        _f.Reminders.Seed(due, ReminderLevel.Friendly, Today);
        ListPaymentRemindersHandler handler = new(_f.Reminders);

        Result<PagedResponse<PaymentReminderResponse>> page = await handler.HandleAsync(new ListPaymentRemindersQuery(
            new DossierId(Guid.NewGuid()), due.InvoiceId.Value, Durand.Value, ReminderLevelDto.Friendly, ReminderOutcomeDto.Sent, ReminderChannelDto.Email,
            Today, Today, "  durand ", ReminderSortDto.Customer, false, Page: 0, PageSize: 5000), default);

        (page.Value.Page, page.Value.PageSize, page.Value.TotalCount).Should().Be((1, ListPaymentRemindersHandler.MaxPageSize, 1));
        page.Value.Items.Single().CustomerName.Should().Be("Durand SARL");
        _f.Reminders.LastFilter.Should().Be(new ReminderHistoryFilter(
            due.InvoiceId, Durand, ReminderLevel.Friendly, ReminderOutcome.Sent, ReminderChannel.Email, Today, Today, "durand", ReminderSortDto.Customer, false, 1, 200));

        await handler.HandleAsync(new ListPaymentRemindersQuery(new DossierId(Guid.NewGuid()), Search: " "), default);
        _f.Reminders.LastFilter!.Search.Should().BeNull();
        _f.Reminders.LastFilter.Invoice.Should().BeNull();
    }

    [Fact]
    public async Task Le_premier_reglage_cree_le_scenario_puis_le_modifie()
    {
        UpdateReminderSettingsHandler handler = new(_f.Settings, _f.Service, _f.UnitOfWork, _f.User, _f.Clock);
        DossierId dossier = new(Guid.NewGuid());

        Result<ReminderSettingsResponse> first = await handler.HandleAsync(
            new UpdateReminderSettingsCommand(dossier, new UpdateReminderSettingsRequest { AutoSend = true, Delays = [5, 20, 40, 60], MinimumGapDays = 10 }), default);
        Result<ReminderSettingsResponse> second = await handler.HandleAsync(
            new UpdateReminderSettingsCommand(dossier, new UpdateReminderSettingsRequest { Delays = [3, 15, 30, 45] }), default);

        first.Value.Should().BeEquivalentTo(new ReminderSettingsResponse(true, [5, 20, 40, 60], 10, true, 1));
        second.Value.Should().BeEquivalentTo(new ReminderSettingsResponse(false, [3, 15, 30, 45], 7, true, 2));
        _f.Settings.Adds.Should().Be(1);
        _f.UnitOfWork.Saves.Should().Be(2);
        (await new GetReminderSettingsHandler(_f.Service).HandleAsync(new GetReminderSettingsQuery(dossier), default)).Value.Version.Should().Be(2);
    }

    [Fact]
    public async Task Un_reglage_invalide_n_enregistre_rien()
    {
        UpdateReminderSettingsHandler handler = new(_f.Settings, _f.Service, _f.UnitOfWork, _f.User, _f.Clock);

        Result<ReminderSettingsResponse> refused = await handler.HandleAsync(new UpdateReminderSettingsCommand(new DossierId(Guid.NewGuid()), null!), default);

        refused.Errors.Single().Code.Should().Be(ReminderErrors.DelaysInvalidCode);
        (_f.Settings.Adds, _f.UnitOfWork.Saves).Should().Be((0, 0));
    }

    private sealed class Fakes
    {
        public Fakes(DateTimeOffset now)
        {
            Clock = new FixedClock(now);
            Service = new PaymentReminderService(Receivables, Context, Reminders, Settings, Email, UnitOfWork, Clock);
        }

        public FixedClock Clock { get; }

        public FakeReceivables Receivables { get; } = new();

        public FakeContext Context { get; } = new();

        public FakeReminders Reminders { get; } = new();

        public FakeSettings Settings { get; } = new();

        public FakeEmail Email { get; } = new();

        public FakeUnitOfWork UnitOfWork { get; } = new();

        public FakeUser User { get; } = new();

        public PaymentReminderService Service { get; }

    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeReceivables : IReceivableDocumentReader
    {
        public List<ReceivableDocument> Documents { get; } = [];

        public Task<IReadOnlyList<ReceivableDocument>> ListAsync(CustomerId? customer, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReceivableDocument>>([.. Documents.Where(d => customer is null || d.CustomerId == customer)]);

        public Task<IReadOnlyDictionary<InvoiceId, IReadOnlyDictionary<decimal, decimal>>> PendingVatAsync(IReadOnlyCollection<InvoiceId> invoices, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeContext : IReminderContextReader
    {
        public Dictionary<CustomerId, ReminderCustomer> Customers { get; } = [];

        public Dictionary<InvoiceId, decimal> Rates { get; } = [];

        public ReminderSender? Sender { get; set; } = new("Atelier Martin", "FR76 3000 6000 0112 3456 7890 189", null);

        public int CustomerReads { get; private set; }

        public Task<IReadOnlyDictionary<CustomerId, ReminderCustomer>> CustomersAsync(IReadOnlyCollection<CustomerId> customers, CancellationToken cancellationToken)
        {
            CustomerReads++;
            return Task.FromResult<IReadOnlyDictionary<CustomerId, ReminderCustomer>>(Customers.Where(c => customers.Contains(c.Key)).ToDictionary());
        }

        public Task<IReadOnlyDictionary<InvoiceId, decimal>> PenaltyRatesAsync(IReadOnlyCollection<InvoiceId> invoices, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<InvoiceId, decimal>>(Rates);

        public Task<ReminderSender?> SenderAsync(CancellationToken cancellationToken) => Task.FromResult(Sender);
    }

    private sealed class FakeReminders : IPaymentReminderRepository
    {
        public List<PaymentReminder> All { get; } = [];

        public ReminderHistoryFilter? LastFilter { get; private set; }

        public void Seed(ReceivableDocument document, ReminderLevel level, DateOnly on) => All.Add(PaymentReminder.Record(
            new ReminderDraft(document.InvoiceId, document.CustomerId, document.Number, level, "fr-FR", "Objet", "Corps", document.Outstanding, 1),
            ReminderChannel.Email, "x@y.fr", null, ReminderTrigger.User, Guid.Empty, new DateTimeOffset(on, TimeOnly.MinValue, TimeSpan.Zero), on));

        public Task AddAsync(PaymentReminder reminder, CancellationToken cancellationToken)
        {
            All.Add(reminder);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<InvoiceId, PaymentReminder>> LastSentAsync(IReadOnlyCollection<InvoiceId> invoices, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<InvoiceId, PaymentReminder>>(All
                .Where(r => invoices.Contains(r.InvoiceId) && r.Outcome == ReminderOutcome.Sent)
                .GroupBy(r => r.InvoiceId)
                .ToDictionary(g => g.Key, g => g.MaxBy(r => r.Level)!));

        public Task<(IReadOnlyList<ReminderHistoryItem> Items, int Total)> ListAsync(ReminderHistoryFilter filter, CancellationToken cancellationToken)
        {
            LastFilter = filter;
            return Task.FromResult<(IReadOnlyList<ReminderHistoryItem>, int)>(([.. All.Select(r => new ReminderHistoryItem(r, "Durand SARL"))], All.Count));
        }
    }

    private sealed class FakeSettings : IReminderSettingsRepository
    {
        private ReminderSettings? _stored;

        public int Adds { get; private set; }

        public Task<ReminderSettings?> FindAsync(CancellationToken cancellationToken) => Task.FromResult(_stored);

        public Task AddAsync(ReminderSettings settings, CancellationToken cancellationToken)
        {
            _stored = settings;
            Adds++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEmail : IEmailSender
    {
        public bool IsConfigured { get; set; } = true;

        public bool Fail { get; set; }

        public List<EmailMessage> Sent { get; } = [];

        public Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                return Task.FromResult(Result.Failure(EmailErrors.SendFailed));
            }

            Sent.Add(message);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeUser : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public Guid UserId { get; } = Guid.NewGuid();
    }
}
