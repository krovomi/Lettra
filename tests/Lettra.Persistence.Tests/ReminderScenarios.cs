using Lettra.Application.Abstractions.Mail;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Reminders;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;
using Lettra.Exports.Application.Reminders;
using Lettra.Exports.Infrastructure;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lettra.Persistence.Tests;

/// <summary>Les e-mails « envoyés » pendant un scénario ; <see cref="Fail"/> simule un serveur qui refuse.</summary>
internal sealed class CapturedEmails : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public bool Fail { get; set; }

    public bool IsConfigured => true;

    public Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            return Task.FromResult<Result>(EmailErrors.SendFailed);
        }

        Sent.Add(message);
        return Task.FromResult(Result.Success());
    }
}

/// <summary>
/// Relances (vague 7) sur les trois bases : factures échues lues avec leurs encaissements, envoi par e-mail et relance
/// notée, historique filtré et trié, scénario relu dans une nouvelle session, passe quotidienne du Worker.
/// </summary>
internal static class ReminderScenarios
{
    private static readonly Customer.Draft Durand = new(
        CustomerKind.Company, "Atelier Durand", "732829320", "73282932000074", "FR44732829320", "compta@durand.example",
        "3 rue des Lilas", null, "69001", "Lyon", "FR");

    private static readonly Customer.Draft Berlin = new(
        CustomerKind.Company, "Berlin Beratung GmbH", null, null, "DE123456789", "buchhaltung@berlin.example",
        "Unter den Linden 1", null, "10117", "Berlin", "DE", PreferredLanguage: "en-US");

    private static readonly Customer.Draft Martin = new(
        CustomerKind.Individual, "Claire Martin", null, null, null, null, "8 rue Neuve", null, "75011", "Paris", "FR");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Fictif", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: true, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: "FR7630006000011234567890189", Bic: "AGRIFRPP", VatDeclarationRegime: VatDeclarationRegime.RealNormal);

    /// <summary>
    /// 10 h UTC du jour légal (fin de matinée à Paris) : avancer de 50 jours garde la même heure de Paris à un jour près
    /// du changement d'heure, sans passer d'une date à l'autre comme un départ juste après minuit.
    /// </summary>
    public static DateTimeOffset Start => new(LegalCalendar.DateOf(DateTimeOffset.UtcNow).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);

    public static TenantHarness Harness(IDbProviderConfigurator configurator, ManualClock clock, CapturedEmails emails) =>
        new(configurator, clock, configure: s => s.AddSingleton<IEmailSender>(emails));

    public static async Task RemindersAsync(TenantHarness harness, ManualClock clock, CapturedEmails emails, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        (Guid durand, Guid berlin, Guid martin, Guid paid) = await SeedAsync(harness, dossier);

        // Échéance à 30 jours : 50 jours plus tard, trois factures ont 20 jours de retard ; la quatrième est payée.
        clock.Now = clock.Now.AddDays(50);
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            ReminderOverviewResponse overview = Ok(await queries.QueryAsync(new GetReminderOverviewQuery(dossier), default));
            overview.Invoices.Select(i => i.CustomerId).Should().BeEquivalentTo([durand, berlin, martin], "la facture payée n'est plus en retard");
            overview.Invoices.Should().OnlyContain(i => i.DaysOverdue == 20 && i.NextLevel == ReminderLevelDto.Friendly && i.DueToday);
            overview.Invoices.Single(i => i.CustomerId == berlin).Language.Should().Be("en-US");
            overview.Invoices.Single(i => i.CustomerId == durand).RecoveryIndemnity.Should().Be(40m);
            overview.Invoices.Single(i => i.CustomerId == martin).RecoveryIndemnity.Should().Be(0m, "un particulier ne doit pas l'indemnité de 40 €");
            overview.DueToday.Should().Be(3);

            Guid durandInvoice = overview.Invoices.Single(i => i.CustomerId == durand).InvoiceId;
            ReminderPreviewResponse preview = Ok(await queries.QueryAsync(new GetReminderPreviewQuery(dossier, durandInvoice), default));
            preview.Recipient.Should().Be("compta@durand.example");
            preview.Subject.Should().StartWith("Rappel");

            PaymentReminderResponse sent = Ok(await commands.SendAsync(new SendReminderCommand(dossier, durandInvoice, new SendReminderRequest(ReminderChannelDto.Email)), default));
            sent.Should().Match<PaymentReminderResponse>(r => r.Outcome == ReminderOutcomeDto.Sent && r.Level == ReminderLevelDto.Friendly && r.CustomerName == "Atelier Durand");
            emails.Sent.Should().ContainSingle().Which.Should().Match<EmailMessage>(m => m.To.Value == "compta@durand.example" && m.SenderName == "Conseil Fictif");

            Guid martinInvoice = overview.Invoices.Single(i => i.CustomerId == martin).InvoiceId;
            (await commands.SendAsync(new SendReminderCommand(dossier, martinInvoice, new SendReminderRequest(ReminderChannelDto.Email)), default))
                .Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Reminders.CustomerEmailMissing);
            Ok(await commands.SendAsync(new SendReminderCommand(dossier, martinInvoice, new SendReminderRequest(ReminderChannelDto.Manual)), default))
                .Channel.Should().Be(ReminderChannelDto.Manual);

            (await commands.SendAsync(new SendReminderCommand(dossier, paid, new SendReminderRequest(ReminderChannelDto.Email)), default))
                .Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Reminders.InvoiceNotOverdue);

            // Un serveur qui refuse : l'échec est gardé, il ne compte pas comme relance faite.
            emails.Fail = true;
            Guid berlinInvoice = overview.Invoices.Single(i => i.CustomerId == berlin).InvoiceId;
            (await commands.SendAsync(new SendReminderCommand(dossier, berlinInvoice, new SendReminderRequest(ReminderChannelDto.Email)), default))
                .Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Email.SendFailed);
            emails.Fail = false;

            ReminderOverviewResponse after = Ok(await queries.QueryAsync(new GetReminderOverviewQuery(dossier), default));
            after.Invoices.Single(i => i.CustomerId == durand).Should().Match<ReminderInvoiceResponse>(i =>
                i.LastLevel == ReminderLevelDto.Friendly && i.NextLevel == ReminderLevelDto.Reminder && !i.DueToday);
            after.Invoices.Single(i => i.CustomerId == berlin).Should().Match<ReminderInvoiceResponse>(i => i.LastLevel == null && i.DueToday);

            PagedResponse<PaymentReminderResponse> history = Ok(await queries.QueryAsync(new ListPaymentRemindersQuery(dossier), default));
            history.TotalCount.Should().Be(3);
            Ok(await queries.QueryAsync(new ListPaymentRemindersQuery(dossier, Outcome: ReminderOutcomeDto.Failed), default))
                .Items.Should().ContainSingle().Which.Should().Match<PaymentReminderResponse>(r => r.ErrorCode == ErrorCodes.Email.SendFailed && r.Language == "en-US");
            Ok(await queries.QueryAsync(new ListPaymentRemindersQuery(dossier, Search: "durand"), default)).Items.Should().ContainSingle();
            Ok(await queries.QueryAsync(new ListPaymentRemindersQuery(dossier, Sort: ReminderSortDto.Customer, Descending: false), default))
                .Items.Select(r => r.CustomerName).Should().Equal("Atelier Durand", "Berlin Beratung GmbH", "Claire Martin");

            Ok(await commands.SendAsync(new UpdateReminderSettingsCommand(dossier,
                new UpdateReminderSettingsRequest { AutoSend = true, Delays = [3, 15, 30, 45], MinimumGapDays = 7 }), default)).Version.Should().Be(1);
        }

        // Huit jours plus tard, la passe du Worker envoie la relance suivante à Durand et le rappel à Berlin ; Martin
        // n'a pas d'adresse : rien ne part pour lui.
        clock.Now = clock.Now.AddDays(8);
        emails.Sent.Clear();
        TenantDescriptor[] dossiers = [harness.Descriptor(dossier)];
        PaymentReminderJob job = new(
            harness.Services.GetRequiredService<IServiceScopeFactory>(),
            harness.Services,
            new ReminderDossierSource((_, _) => Task.FromResult<IReadOnlyList<TenantDescriptor>>(dossiers)),
            emails,
            Options.Create(new PaymentReminderJobOptions()),
            NullLogger<PaymentReminderJob>.Instance);
        (int active, int sentCount, int failed) = await job.RunPassAsync(default);
        (active, sentCount, failed).Should().Be((1, 2, 0));
        string durandNumber = await NumberAsync(harness, dossier, durand);
        string berlinNumber = await NumberAsync(harness, dossier, berlin);
        emails.Sent.Select(m => m.Subject).Should().BeEquivalentTo(
            [$"Relance : facture {durandNumber} impayée", $"Reminder: invoice {berlinNumber} is now due"]);

        // Une nouvelle session relit le scénario et l'historique ; une seconde passe le même jour n'envoie rien.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            Ok(await queries.QueryAsync(new GetReminderSettingsQuery(dossier), default)).AutoSend.Should().BeTrue();
            PagedResponse<PaymentReminderResponse> history = Ok(await queries.QueryAsync(new ListPaymentRemindersQuery(dossier, Outcome: ReminderOutcomeDto.Sent), default));
            history.TotalCount.Should().Be(4);
            history.Items.Where(r => r.Trigger == ReminderTriggerDto.Schedule).Should().HaveCount(2);
        }

        (await job.RunPassAsync(default)).Sent.Should().Be(0);
    }

    /// <summary>
    /// Les cas limites de la passe du Worker : dossier sans envoi automatique, sans fiche entreprise, inconnu, liste des
    /// dossiers en panne, et la boucle du service (coupée, sans e-mail, puis une vraie passe arrêtée proprement).
    /// </summary>
    public static async Task JobEdgesAsync(TenantHarness harness, CapturedEmails emails, DatabaseProvider provider, string manualConnection, string autoConnection)
    {
        DossierId manual = harness.AddDossier(provider, manualConnection);
        DossierId automatic = harness.AddDossier(provider, autoConnection);
        await using (AsyncServiceScope scope = await harness.OpenAsync(automatic))
        {
            Ok(await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new UpdateReminderSettingsCommand(automatic,
                new UpdateReminderSettingsRequest { AutoSend = true, Delays = [3, 15, 30, 45], MinimumGapDays = 7 }), default));
        }

        TenantDescriptor unknown = new(new DossierId(Guid.CreateVersion7()), harness.Descriptor(manual).OrganisationId, provider, "test:inconnu", null);
        TenantDescriptor[] dossiers = [harness.Descriptor(manual), harness.Descriptor(automatic), unknown];
        PaymentReminderJob Job(ReminderDossierSource source, IEmailSender sender, PaymentReminderJobOptions? options = null) => new(
            harness.Services.GetRequiredService<IServiceScopeFactory>(), harness.Services, source, sender,
            Options.Create(options ?? new PaymentReminderJobOptions()), NullLogger<PaymentReminderJob>.Instance);
        ReminderDossierSource listed = new((_, _) => Task.FromResult<IReadOnlyList<TenantDescriptor>>(dossiers));

        // Sans envoi automatique, rien ; sans fiche entreprise, la passe est sautée sans erreur ; un dossier inconnu
        // compte un échec sans arrêter les autres.
        (await Job(listed, emails).RunPassAsync(default)).Should().Be((0, 0, 1));
        (await Job(new ReminderDossierSource((_, _) => throw new InvalidOperationException("catalogue indisponible")), emails).RunPassAsync(default))
            .Should().Be((0, 0, 0));
        emails.Sent.Should().BeEmpty();

        // Le service ne démarre pas de boucle quand il est coupé ou sans serveur d'envoi.
        int listings = 0;
        TaskCompletionSource firstPass = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ReminderDossierSource counted = new((_, _) =>
        {
            Interlocked.Increment(ref listings);
            firstPass.TrySetResult();
            return Task.FromResult<IReadOnlyList<TenantDescriptor>>([]);
        });
        foreach (PaymentReminderJob idle in new[] { Job(counted, emails, new PaymentReminderJobOptions { Enabled = false }), Job(counted, new NoEmail()) })
        {
            await idle.StartAsync(default);
            await idle.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
            await idle.StopAsync(default);
        }

        listings.Should().Be(0);

        PaymentReminderJob running = Job(counted, emails, new PaymentReminderJobOptions { InitialDelay = TimeSpan.Zero, Interval = TimeSpan.FromHours(1) });
        await running.StartAsync(default);
        await firstPass.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await running.StopAsync(default);
        listings.Should().Be(1);
    }

    private sealed class NoEmail : IEmailSender
    {
        public bool IsConfigured => false;

        public Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken) => Task.FromResult<Result>(EmailErrors.NotConfigured);
    }

    private static async Task<string> NumberAsync(TenantHarness harness, DossierId dossier, Guid customer)
    {
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        IReadOnlyList<ReceivableDocument> documents = await scope.ServiceProvider.GetRequiredService<IReceivableDocumentReader>()
            .ListAsync(new CustomerId(customer), default);
        return documents.Single(d => d.Outstanding > 0m).Number;
    }

    private static T Ok<T>(Result<T> result)
    {
        result.IsSuccess.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Code)));
        return result.Value;
    }

    private static async Task<(Guid Durand, Guid Berlin, Guid Martin, Guid Paid)> SeedAsync(TenantHarness harness, DossierId dossier)
    {
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
        Guid durand = (await commands.SendAsync(new CreateCustomerCommand(dossier, Durand), default)).Value.Value;
        Guid berlin = (await commands.SendAsync(new CreateCustomerCommand(dossier, Berlin), default)).Value.Value;
        Guid martin = (await commands.SendAsync(new CreateCustomerCommand(dossier, Martin), default)).Value.Value;
        Guid paid = Guid.Empty;
        foreach ((Guid customer, VatRegimeDto regime, bool pay) in new[]
        {
            (durand, VatRegimeDto.Standard, false),
            (berlin, VatRegimeDto.ReverseChargeIntraEu, false),
            (martin, VatRegimeDto.Standard, false),
            (durand, VatRegimeDto.Standard, true),
        })
        {
            DocumentLineRequest line = new() { Designation = "Conseil", Quantity = 1, UnitPriceExclTax = 100m, VatRateCode = "FR-20", Kind = CatalogItemKindDto.Service, UnitCode = "HUR" };
            Result<InvoiceId> created = await commands.SendAsync(new CreateInvoiceCommand(dossier, new SaveInvoiceRequest { CustomerId = customer, Lines = [line], VatRegime = regime }), default);
            created.IsSuccess.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Code)));
            Result validated = await commands.SendAsync(new ValidateInvoiceCommand(dossier, created.Value, null), default);
            validated.IsSuccess.Should().BeTrue(string.Join(", ", validated.Errors.Select(e => e.Code)));
            if (pay)
            {
                paid = created.Value.Value;
                RecordPaymentRequest payment = new()
                {
                    CustomerId = customer,
                    Date = LegalCalendar.DateOf(DateTimeOffset.UtcNow),
                    Amount = 120m,
                    Allocations = [new PaymentAllocationRequest(paid, 120m)],
                };
                Ok(await commands.SendAsync(new RecordPaymentCommand(dossier, payment), default));
            }
        }

        return (durand, berlin, martin, paid);
    }
}
