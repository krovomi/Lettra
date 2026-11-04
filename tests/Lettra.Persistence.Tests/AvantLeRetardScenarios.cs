using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Mail;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Portal;
using Lettra.Contracts.Reminders;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Application.Insights;
using Lettra.Exports.Application.Portal;
using Lettra.Exports.Application.Reminders;
using Lettra.Exports.Domain.Platform;
using Lettra.Exports.Infrastructure;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Avant le retard sur les trois bases : échéancier demandé dans l'espace client (bornes, refus, facture contestée),
/// relance d'échéance manquée, décisions de l'entreprise, opposition au ciblage, rappel avant échéance envoyé au client
/// dont la météo des paiements s'est gâtée, scénario relu dans une nouvelle session. Données fictives.
/// </summary>
internal static class AvantLeRetardScenarios
{
    private static readonly Customer.Draft Durand = new(
        CustomerKind.Company, "Atelier Durand", "732829320", "73282932000074", "FR44732829320", "compta@durand.example",
        "3 rue des Lilas", null, "69001", "Lyon", "FR");

    private static readonly Customer.Draft Martin = new(
        CustomerKind.Individual, "Claire Martin", null, null, null, "claire@martin.example", "8 rue Neuve", null, "75011", "Paris", "FR");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Fictif", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: true, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: "FR7630006000011234567890189", Bic: "AGRIFRPP", VatDeclarationRegime: VatDeclarationRegime.RealNormal);

    private static readonly ReminderPreventionDto Prevention = new(true, 3, true, 3, 300m);

    /// <summary>L'espace client ouvert, comme le fait l'API quand un compte de service lui est déclaré.</summary>
    private sealed class OpenPortal : ICustomerPortalSettings
    {
        public bool IsEnabled => true;
    }

    public static TenantHarness Harness(IDbProviderConfigurator configurator, ManualClock clock, CapturedEmails emails) =>
        new(configurator, clock, simulatedPlatform: true, configure: services =>
        {
            services.AddSingleton<ICustomerPortalSettings, OpenPortal>();
            services.AddSingleton<IEmailSender>(emails);
            services.Replace(ServiceDescriptor.Singleton(new LettraPublicAddress(new Uri("https://lettra.example/"))));
        });

    public static async Task PreventionAsync(TenantHarness harness, ManualClock clock, CapturedEmails emails, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        Scenario s = new(harness, dossier);
        Guid durand = Ok(await s.Send(new CreateCustomerCommand(dossier, Durand))).Value;
        Guid martin = Ok(await s.Send(new CreateCustomerCommand(dossier, Martin))).Value;
        Ok(await s.Send(new SaveCompanyProfileCommand(dossier, Cabinet, null)));
        InvoiceId first = await s.ValidatedAsync(durand, 100m);
        InvoiceId second = await s.ValidatedAsync(durand, 100m);
        InvoiceId big = await s.ValidatedAsync(durand, 1000m);
        InvoiceId disputed = await s.ValidatedAsync(durand, 500m);
        InvoiceId individual = await s.ValidatedAsync(martin, 500m);
        Ok(await s.Send(new UpdateReminderSettingsCommand(dossier, new UpdateReminderSettingsRequest { Delays = [3, 15, 30, 45], MinimumGapDays = 7, Prevention = Prevention })));

        // La facture contestée sur la plateforme agréée n'est pas étalée.
        SimulatedAccreditedPlatform platform = harness.Services.GetRequiredService<SimulatedAccreditedPlatform>();
        Ok(await s.Send(new TransmitInvoiceCommand(dossier, disputed)));
        string reference = Ok(await s.Send(new RefreshPlatformStatusCommand(dossier, disputed))).PlatformReference!;
        platform.SimulateBuyerStatus(reference, PlatformStatus.Disputed, "Quantités contestées");
        Ok(await s.Send(new RefreshPlatformStatusCommand(dossier, disputed))).Status.Should().Be(PlatformStatusDto.Disputed);

        // Cinq jours après l'échéance : l'espace client propose l'échéancier pour la seule facture qui s'y prête.
        clock.Now = clock.Now.AddDays(35);
        PortalLinkResponse durandSpace = Ok(await s.Send(new CreatePortalLinkCommand(dossier, new CreatePortalLinkRequest { CustomerId = durand })));
        PortalLinkResponse martinSpace = Ok(await s.Send(new CreatePortalLinkCommand(dossier, new CreatePortalLinkRequest { CustomerId = martin })));
        PortalView view = Ok(await s.Send(new OpenPortalCommand(dossier, Hash(durandSpace))));
        view.Invoices.Single(i => i.Id == big.Value).PlanOptions!.Select(o => (o.Installments, o.Schedule.Sum(i => i.Amount))).Should().Equal((2, 1200m), (3, 1200m));
        view.Invoices.Where(i => i.Id != big.Value).Should().OnlyContain(i => i.PlanOptions!.Count == 0 && i.Plan == null, "trop petites ou contestée");
        Ok(await s.Send(new OpenPortalCommand(dossier, Hash(martinSpace)))).Invoices.Single().PlanOptions.Should().BeEmpty("un particulier n'a pas d'échéancier");

        RequestPaymentPlanOnPortalCommand Ask(PortalLinkResponse link, InvoiceId invoice, int count = 2, bool consent = true) =>
            new(dossier, Hash(link), invoice.Value, new RequestPaymentPlanRequest(count, "Jeanne  Durand", consent));
        Code(await s.Send(Ask(durandSpace, big, 4))).Should().Be(ErrorCodes.PaymentPlans.InstallmentsInvalid);
        Code(await s.Send(Ask(durandSpace, big, consent: false))).Should().Be(ErrorCodes.PaymentPlans.ConsentRequired);
        Code(await s.Send(Ask(durandSpace, disputed))).Should().Be(ErrorCodes.PaymentPlans.Disputed);
        Code(await s.Send(Ask(durandSpace, first))).Should().Be(ErrorCodes.PaymentPlans.BelowMinimum);
        Code(await s.Send(Ask(martinSpace, individual))).Should().Be(ErrorCodes.PaymentPlans.NotProfessional);
        Code(await s.Send(Ask(durandSpace, individual))).Should().Be(ErrorCodes.Portal.NothingToPay, "la facture d'un autre client");
        PaymentPlanRequestResult created = Ok(await s.Send(Ask(durandSpace, big)));
        created.Installments.Select(i => i.Amount).Should().Equal(600m, 600m);
        Code(await s.Send(Ask(durandSpace, big))).Should().Be(ErrorCodes.PaymentPlans.AlreadyPlanned);
        harness.Alerts.Signals.Where(a => a.Rule == DossierAlertRule.PaymentPlanToReview).Should().ContainSingle().Which.Firing.Should().BeTrue();
        PortalInvoice planned = Ok(await s.Send(new OpenPortalCommand(dossier, Hash(durandSpace)))).Invoices.Single(i => i.Id == big.Value);
        (planned.Plan!.RequestedBy, planned.Plan.Installments.Count, planned.PlanOptions!.Count).Should().Be(("Jeanne Durand", 2, 0));

        // Côté entreprise : l'échéancier à voir, la facture sortie de l'escalade, attendue à sa première échéance.
        DateOnly installment = created.Installments[0].DueDate;
        ReminderOverviewResponse overview = Ok(await s.Query(new GetReminderOverviewQuery(dossier)));
        PaymentPlanResponse plan = overview.Plans.Should().ContainSingle().Subject;
        (plan.InvoiceId, plan.Status, plan.Acknowledged, plan.RequestedBy, plan.Outstanding).Should().Be((big.Value, PaymentPlanStatusDto.OnTrack, false, "Jeanne Durand", 1200m));
        overview.Invoices.Select(i => i.InvoiceId).Should().BeEquivalentTo([first.Value, second.Value, disputed.Value, individual.Value]);
        Ok(await s.Query(new GetInvoicePaymentPlanQuery(dossier, big.Value))).Plan!.Id.Should().Be(plan.Id);
        Ok(await s.Query(new GetPaymentPreventionSignalsQuery(dossier))).Should().ContainSingle(x => x.Kind == PaymentPreventionSignals.PlanToReviewKind && x.Subject == plan.Id);
        (await s.HistoryAsync()).Single(i => i.InvoiceId == big.Value).DueDate.Should().Be(installment, "sous échéancier, la facture est attendue à sa prochaine échéance");

        // Durand règle ses deux petites factures avec 20 jours de retard : sa météo se gâte.
        clock.Now = clock.Now.AddDays(15);
        foreach (InvoiceId paid in new[] { first, second })
        {
            Ok(await s.Send(new RecordPaymentCommand(dossier, new RecordPaymentRequest
            {
                CustomerId = durand,
                Date = LegalCalendar.DateOf(clock.Now),
                Amount = 120m,
                Allocations = [new PaymentAllocationRequest(paid.Value, 120m)],
            })));
        }

        // La première échéance est manquée depuis quatre jours : une relance, avec le lien de l'espace client.
        clock.Now = At(installment.AddDays(4));
        ReminderInvoiceResponse missed = Ok(await s.Query(new GetReminderOverviewQuery(dossier))).Invoices.Single(i => i.InvoiceId == big.Value);
        (missed.Installment, missed.NextLevel, missed.DueToday, missed.Outstanding, missed.PaymentPlanId).Should().Be(
            (1, ReminderLevelDto.MissedInstallment, true, 600m, plan.Id));
        Ok(await s.Send(new SendReminderCommand(dossier, big.Value, new SendReminderRequest(ReminderChannelDto.Email)))).Level.Should().Be(ReminderLevelDto.MissedInstallment);
        EmailMessage letter = emails.Sent[^1];
        letter.Subject.Should().StartWith("Échéance 1 de votre échéancier");
        letter.TextBody.Should().Contain("https://lettra.example/espace-client/").And.Contain("Rappel du calendrier");
        Ok(await s.Query(new GetReminderOverviewQuery(dossier))).Invoices.Single(i => i.InvoiceId == big.Value).NextLevel.Should().BeNull("une seule relance par échéance");

        // L'entreprise prend l'échéancier en compte : refusé à une version périmée, puis l'alerte se lève.
        Code(await s.Send(new AcknowledgePaymentPlanCommand(dossier, plan.Id, new PaymentPlanDecisionRequest(plan.Version + 1))))
            .Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        PaymentPlanResponse seen = Ok(await s.Send(new AcknowledgePaymentPlanCommand(dossier, plan.Id, new PaymentPlanDecisionRequest(plan.Version))));
        (seen.Acknowledged, seen.Status, seen.Installments[0].Missed).Should().Be((true, PaymentPlanStatusDto.Late, true));
        harness.Alerts.Signals.Last(a => a.Rule == DossierAlertRule.PaymentPlanToReview).Firing.Should().BeFalse();

        // Une nouvelle facture arrive à échéance dans trois jours : le rappel avant échéance, sauf opposition du client.
        InvoiceId next = await s.ValidatedAsync(durand, 1000m);
        clock.Now = clock.Now.AddDays(27);
        Ok(await s.Send(new SetTargetingObjectionCommand(dossier, durand, new SetTargetingObjectionRequest(true))));
        Code(await s.Send(new SetTargetingObjectionCommand(dossier, Guid.CreateVersion7(), new SetTargetingObjectionRequest(true)))).Should().Be(ErrorCodes.Customer.NotFound);
        ReminderOverviewResponse objected = Ok(await s.Query(new GetReminderOverviewQuery(dossier)));
        objected.Upcoming.Should().BeEmpty();
        objected.Objections.Should().ContainSingle().Which.CustomerName.Should().Be("Atelier Durand");
        Ok(await s.Send(new SetTargetingObjectionCommand(dossier, durand, new SetTargetingObjectionRequest(false))));

        UpcomingReminderResponse upcoming = Ok(await s.Query(new GetReminderOverviewQuery(dossier))).Upcoming.Should().ContainSingle().Subject;
        (upcoming.InvoiceId, upcoming.DueToday, upcoming.Weather is PaymentWeatherDto.Rainy or PaymentWeatherDto.Stormy).Should().Be((next.Value, true, true));
        Ok(await s.Query(new GetPaymentPreventionSignalsQuery(dossier))).Should().Contain(x => x.Kind == PaymentPreventionSignals.BeforeDueKind && x.Subject == next.Value);
        Ok(await s.Send(new SendReminderCommand(dossier, next.Value, new SendReminderRequest(ReminderChannelDto.Email)))).Level.Should().Be(ReminderLevelDto.BeforeDue);
        emails.Sent[^1].TextBody.Should().Contain("Petit rappel avant l'échéance").And.Contain("https://lettra.example/espace-client/").And.Contain("demander un échéancier");
        Ok(await s.Query(new GetReminderOverviewQuery(dossier))).Upcoming.Single().SentAt.Should().NotBeNull();

        // L'entreprise arrête l'échéancier : la facture reprend l'escalade, la relance d'échéance valant rappel amical.
        PaymentPlanResponse stopped = Ok(await s.Send(new CancelPaymentPlanCommand(dossier, plan.Id, new PaymentPlanDecisionRequest(seen.Version))));
        stopped.Status.Should().Be(PaymentPlanStatusDto.Cancelled);
        ReminderInvoiceResponse resumed = Ok(await s.Query(new GetReminderOverviewQuery(dossier))).Invoices.Single(i => i.InvoiceId == big.Value);
        (resumed.PaymentPlanId, resumed.NextLevel, resumed.Outstanding).Should().Be((null, ReminderLevelDto.Reminder, 1200m));

        // Une nouvelle session relit le scénario et l'historique.
        Ok(await s.Query(new GetReminderSettingsQuery(dossier))).Prevention.Should().Be(Prevention);
        Ok(await s.Query(new ListPaymentRemindersQuery(dossier, Level: ReminderLevelDto.MissedInstallment))).Items.Should().ContainSingle().Which.InvoiceId.Should().Be(big.Value);
    }

    private static DateTimeOffset At(DateOnly day) => new(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);

    private static string Hash(PortalLinkResponse link) => PortalToken.Read(link.Token)!.Value.SecretHash;

    private static T Ok<T>(Result<T> result)
    {
        result.IsSuccess.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Code)));
        return result.Value;
    }

    private static void Ok(Result result) => result.IsSuccess.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Code)));

    private static string Code(Result result) => result.Errors.Should().ContainSingle().Subject.Code;

    private static string Code<T>(Result<T> result) => result.Errors.Should().ContainSingle().Subject.Code;

    /// <summary>Chaque étape dans sa propre session, comme une requête de l'API.</summary>
    private sealed class Scenario(TenantHarness harness, DossierId dossier)
    {
        public async Task<T> Send<T>(ICommand<T> command)
        {
            await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
            return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(command, default);
        }

        public async Task<T> Query<T>(IQuery<T> query)
        {
            await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
            return await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>().QueryAsync(query, default);
        }

        public async Task<IReadOnlyList<PaymentHistoryInvoice>> HistoryAsync()
        {
            await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
            return await scope.ServiceProvider.GetRequiredService<IPaymentHistoryReader>().ReadAsync(default);
        }

        /// <summary>Une facture validée du jour (échéance à 30 jours) : <paramref name="amount"/> HT, TVA à 20 %.</summary>
        public async Task<InvoiceId> ValidatedAsync(Guid customer, decimal amount)
        {
            DocumentLineRequest line = new() { Designation = "Conseil", Quantity = 1, UnitPriceExclTax = amount, VatRateCode = "FR-20", Kind = CatalogItemKindDto.Service, UnitCode = "HUR" };
            InvoiceId invoice = Ok(await Send(new CreateInvoiceCommand(dossier, new SaveInvoiceRequest { CustomerId = customer, Lines = [line] })));
            Ok(await Send(new ValidateInvoiceCommand(dossier, invoice, null)));
            return invoice;
        }
    }
}
