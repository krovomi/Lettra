using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Mail;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Portal;
using Lettra.Domain.Reminders;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;
using Lettra.Exports.Application.Insights;
using Lettra.Exports.Application.Portal;
using Lettra.Exports.Application.Reminders;

namespace Lettra.Exports.Tests.Reminders;

/// <summary>Les ports des relances en mémoire, pour le service et ses cas d'usage, sans base.</summary>
internal sealed class ReminderFakes
{
    public ReminderFakes(DateTimeOffset now)
    {
        Clock = new FixedClock(now);
        Service = new PaymentReminderService(
            Receivables, Context, Reminders, Settings, Plans, Objections, new ReminderTargeting(History, Objections),
            new ReminderPaymentAids(Portal, Links, Tenant, Address, Qr, Clock), Email, UnitOfWork, Clock);
        Alerts = new PaymentPlanAlerts(Plans, Tenant, AlertPublisher, Clock);
    }

    public FixedClock Clock { get; }

    public FakeReceivables Receivables { get; } = new();

    public FakeContext Context { get; } = new();

    public FakeReminders Reminders { get; } = new();

    public FakeSettings Settings { get; } = new();

    public FakePlans Plans { get; } = new();

    public FakeObjections Objections { get; } = new();

    public FakeHistory History { get; } = new();

    public FakePortal Portal { get; } = new();

    public FakeLinks Links { get; } = new();

    public FakeTenant Tenant { get; } = new();

    public LettraPublicAddress Address { get; set; } = new(new Uri("https://lettra.example/"));

    public FakeQr Qr { get; } = new();

    public FakeEmail Email { get; } = new();

    public FakeUnitOfWork UnitOfWork { get; } = new();

    public FakeUser User { get; } = new();

    public FakeAlertPublisher AlertPublisher { get; } = new();

    public PaymentPlanAlerts Alerts { get; }

    public PaymentReminderService Service { get; }
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeReceivables : IReceivableDocumentReader
{
    public List<ReceivableDocument> Documents { get; } = [];

    public Task<IReadOnlyList<ReceivableDocument>> ListAsync(CustomerId? customer, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ReceivableDocument>>([.. Documents.Where(d => customer is null || d.CustomerId == customer)]);

    public Task<IReadOnlyDictionary<InvoiceId, IReadOnlyDictionary<decimal, decimal>>> PendingVatAsync(IReadOnlyCollection<InvoiceId> invoices, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class FakeContext : IReminderContextReader
{
    public Dictionary<CustomerId, ReminderCustomer> Customers { get; } = [];

    public Dictionary<InvoiceId, decimal> Rates { get; } = [];

    public HashSet<InvoiceId> Disputed { get; } = [];

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

    public Task<IReadOnlySet<InvoiceId>> DisputedAsync(IReadOnlyCollection<InvoiceId> invoices, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<InvoiceId>>(Disputed.Where(invoices.Contains).ToHashSet());
}

internal sealed class FakeReminders : IPaymentReminderRepository
{
    public List<PaymentReminder> All { get; } = [];

    public ReminderHistoryFilter? LastFilter { get; private set; }

    public PaymentReminder Seed(ReceivableDocument document, ReminderLevel level, DateOnly on, int? installment = null)
    {
        PaymentReminder reminder = PaymentReminder.Record(
            new ReminderDraft(document.InvoiceId, document.CustomerId, document.Number, level, "fr-FR", "Objet", "Corps", document.Outstanding, 1, installment),
            ReminderChannel.Email, "x@y.fr", null, ReminderTrigger.User, Guid.Empty, new DateTimeOffset(on, TimeOnly.MinValue, TimeSpan.Zero), on);
        All.Add(reminder);
        return reminder;
    }

    public Task AddAsync(PaymentReminder reminder, CancellationToken cancellationToken)
    {
        All.Add(reminder);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<InvoiceId, ReminderTrack>> TracksAsync(IReadOnlyCollection<InvoiceId> invoices, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<InvoiceId, ReminderTrack>>(All
            .Where(r => invoices.Contains(r.InvoiceId))
            .GroupBy(r => r.InvoiceId)
            .ToDictionary(g => g.Key, ReminderTrack.Of));

    public Task<(IReadOnlyList<ReminderHistoryItem> Items, int Total)> ListAsync(ReminderHistoryFilter filter, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        return Task.FromResult<(IReadOnlyList<ReminderHistoryItem>, int)>(([.. All.Select(r => new ReminderHistoryItem(r, "Durand SARL"))], All.Count));
    }
}

internal sealed class FakeSettings : IReminderSettingsRepository
{
    public ReminderSettings? Stored { get; set; }

    public int Adds { get; private set; }

    public Task<ReminderSettings?> FindAsync(CancellationToken cancellationToken) => Task.FromResult(Stored);

    public Task AddAsync(ReminderSettings settings, CancellationToken cancellationToken)
    {
        Stored = settings;
        Adds++;
        return Task.CompletedTask;
    }

    /// <summary>Le scénario par défaut, avec l'échéancier ouvert et l'envoi automatique au choix.</summary>
    public ReminderSettings Open(bool autoSend = false, bool beforeDue = true, int maxInstallments = 3, decimal minimum = 300m)
    {
        ReminderSettings settings = ReminderSettings.Default();
        settings.Update(autoSend, [3, 15, 30, 45], 7, new ReminderPrevention(beforeDue, 3, true, maxInstallments, minimum), Guid.Empty, DateTimeOffset.UnixEpoch)
            .IsSuccess.Should().BeTrue();
        Stored = settings;
        return settings;
    }
}

internal sealed class FakePlans : IPaymentPlanRepository
{
    public List<PaymentPlan> All { get; } = [];

    public Task AddAsync(PaymentPlan plan, CancellationToken cancellationToken)
    {
        All.Add(plan);
        return Task.CompletedTask;
    }

    public Task<PaymentPlan?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(All.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyDictionary<InvoiceId, PaymentPlan>> ActiveAsync(IReadOnlyCollection<InvoiceId>? invoices, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<InvoiceId, PaymentPlan>>(All
            .Where(p => p.Status == PaymentPlanStatus.Active && (invoices is null || invoices.Contains(p.InvoiceId)))
            .ToDictionary(p => p.InvoiceId));

    public Task<PaymentPlan?> LatestForInvoiceAsync(InvoiceId invoice, CancellationToken cancellationToken) =>
        Task.FromResult(All.Where(p => p.InvoiceId == invoice).OrderByDescending(p => p.RequestedAt).FirstOrDefault());

    public Task<IReadOnlyList<PaymentPlan>> ListAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PaymentPlan>>([.. All.Where(p => p.Status == PaymentPlanStatus.Active || p.RequestedAt >= since)]);

    public Task<int> CountUnacknowledgedAsync(CancellationToken cancellationToken) =>
        Task.FromResult(All.Count(p => p.Status == PaymentPlanStatus.Active && !p.IsAcknowledged));
}

internal sealed class FakeObjections : IPaymentTargetingObjectionRepository
{
    public List<PaymentTargetingObjection> All { get; } = [];

    public Task<IReadOnlyList<PaymentTargetingObjection>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PaymentTargetingObjection>>([.. All]);

    public Task<PaymentTargetingObjection?> FindAsync(CustomerId customer, CancellationToken cancellationToken) =>
        Task.FromResult(All.FirstOrDefault(o => o.CustomerId == customer));

    public Task AddAsync(PaymentTargetingObjection objection, CancellationToken cancellationToken)
    {
        All.Add(objection);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(PaymentTargetingObjection objection, CancellationToken cancellationToken)
    {
        All.Remove(objection);
        return Task.CompletedTask;
    }
}

internal sealed class FakeHistory : IPaymentHistoryReader
{
    public List<PaymentHistoryInvoice> Invoices { get; } = [];

    public int Reads { get; private set; }

    public Task<IReadOnlyList<PaymentHistoryInvoice>> ReadAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult<IReadOnlyList<PaymentHistoryInvoice>>([.. Invoices]);
    }

    /// <summary>
    /// Une habitude de paiement : <paramref name="count"/> factures soldées <paramref name="daysLate"/> jours après leur
    /// échéance, un <paramref name="paidOn"/> donné (le meilleur jour du client).
    /// </summary>
    public void Habit(CustomerId customer, string name, int daysLate, DateOnly today, int count = 3, DayOfWeek? paidOn = null)
    {
        for (int i = 0; i < count; i++)
        {
            DateOnly due = today.AddDays(-30 - (14 * i) - daysLate);
            DateOnly settled = due.AddDays(daysLate);
            if (paidOn is DayOfWeek day)
            {
                settled = settled.AddDays(((int)day - (int)settled.DayOfWeek + 7) % 7);
            }

            Invoices.Add(new PaymentHistoryInvoice(Guid.NewGuid(), customer.Value, name, $"H-{name}-{i}", due.AddDays(-30), due, 500m, 0m,
                [new PaymentHistoryEntry(settled, 500m)]));
        }
    }
}

internal sealed class FakePortal : ICustomerPortalSettings
{
    public bool IsEnabled { get; set; } = true;
}

internal sealed class FakeLinks : IPortalLinkRepository
{
    public List<PortalLink> All { get; } = [];

    public Task AddAsync(PortalLink link, CancellationToken cancellationToken)
    {
        All.Add(link);
        return Task.CompletedTask;
    }

    public Task<PortalLink?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(All.FirstOrDefault(l => l.Id == id));

    public Task<PortalLink?> FindBySecretHashAsync(string secretHash, CancellationToken cancellationToken) =>
        Task.FromResult(All.FirstOrDefault(l => l.SecretHash == secretHash));

    public Task<IReadOnlyList<PortalLink>> ListByCustomerAsync(CustomerId customer, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PortalLink>>([.. All.Where(l => l.CustomerId == customer)]);
}

internal sealed class FakeTenant : ITenantContext
{
    public TenantDescriptor Current { get; } = new(new DossierId(Guid.NewGuid()), new OrganisationId(Guid.NewGuid()), DatabaseProvider.Sqlite, "test:dossier");

    public bool IsResolved { get; set; } = true;
}

internal sealed class FakeQr : IPaymentQrCodeImage
{
    public bool Draws { get; set; } = true;

    public byte[]? Png(string beneficiary, string? iban, string? bic, decimal amount, string remittance) => Draws && iban is not null ? [1, 2, 3] : null;
}

internal sealed class FakeEmail : IEmailSender
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

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.FromResult(1);
    }
}

internal sealed class FakeUser : ICurrentUser
{
    public bool IsAuthenticated => true;

    public Guid UserId { get; } = Guid.NewGuid();
}

internal sealed class FakeAlertPublisher : IDossierAlertPublisher
{
    public List<DossierAlertSignal> Signals { get; } = [];

    public Task PublishAsync(DossierAlertSignal signal, CancellationToken cancellationToken)
    {
        Signals.Add(signal);
        return Task.CompletedTask;
    }
}
