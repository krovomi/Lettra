using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Outbox;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Application.Features.Integrity;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Domain.Platform;
using Lettra.Exports.Infrastructure;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.EInvoicing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Transmission à la plateforme agréée simulée de pièces validées par le vrai parcours (phase 5.3) : dépôt et émission,
/// statuts relus et enregistrés, réponses de l'acheteur, encaissements partiels, rejet puis retransmission, e-reporting
/// d'une vente hors de France, panne de la plateforme. Données fictives.
/// </summary>
internal static class PlatformScenarios
{
    private static readonly Customer.Draft Durand = new(
        CustomerKind.Company, "Atelier Durand", "732829320", "73282932000074", "FR44732829320", "compta@durand.example",
        "3 rue des Lilas", null, "69001", "Lyon", "FR");

    private static readonly Customer.Draft Geneve = new(
        CustomerKind.Company, "Conseil Lac SA", null, null, null, "compta@lac.example",
        "1 quai du Mont-Blanc", null, "1201", "Genève", "CH");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Fictif", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: false, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: "FR7630006000011234567890189", Bic: "AGRIFRPP");

    public static async Task TransmissionAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        SimulatedAccreditedPlatform platform = harness.Services.GetRequiredService<SimulatedAccreditedPlatform>();
        InvoiceId approved, rejected, refused, abroad, outage;
        Guid durand;

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
            durand = (await commands.SendAsync(new CreateCustomerCommand(dossier, Durand), default)).Value.Value;
            Guid geneve = (await commands.SendAsync(new CreateCustomerCommand(dossier, Geneve), default)).Value.Value;
            approved = await ValidatedAsync(commands, dossier, durand, 300m);
            rejected = await ValidatedAsync(commands, dossier, durand, 120m);
            refused = await ValidatedAsync(commands, dossier, durand, 80m);
            abroad = await ValidatedAsync(commands, dossier, geneve, 500m, VatRegimeDto.Export);
            outage = await ValidatedAsync(commands, dossier, durand, 50m);
        }

        // Dépôt d'une facture électronique : Déposée, et la pièce est émise. Rejouer la commande rend la même transmission.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            PlatformTransmissionResponse sent = Succeeded(await commands.SendAsync(new TransmitInvoiceCommand(dossier, approved), default));
            sent.Channel.Should().Be(PlatformChannelDto.EInvoice);
            sent.Format.Should().Be(EInvoiceFormatDto.Cii);
            sent.Platform.Should().Be(SimulatedAccreditedPlatform.PlatformName);
            sent.Status.Should().Be(PlatformStatusDto.Deposited);
            sent.PlatformReference.Should().NotBeNullOrWhiteSpace();
            (await StatusOfAsync(scope, dossier, approved)).Should().Be(InvoiceStatusDto.Issued);

            PlatformTransmissionResponse replayed = Succeeded(await commands.SendAsync(new TransmitInvoiceCommand(dossier, approved, EInvoiceFormatDto.Ubl), default));
            replayed.Id.Should().Be(sent.Id);
            replayed.Format.Should().Be(EInvoiceFormatDto.Cii);
        }

        // Statuts relus à la plateforme, puis réponse de l'acheteur ; une relecture n'ajoute rien de connu.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            PlatformTransmissionResponse refreshed = Succeeded(await commands.SendAsync(new RefreshPlatformStatusCommand(dossier, approved), default));
            refreshed.History.Select(h => h.Status).Should().Equal(
                PlatformStatusDto.Deposited, PlatformStatusDto.Issued, PlatformStatusDto.Received, PlatformStatusDto.MadeAvailable);
            platform.SimulateBuyerStatus(refreshed.PlatformReference!, PlatformStatus.Approved);
            Succeeded(await commands.SendAsync(new RefreshPlatformStatusCommand(dossier, approved), default)).Status.Should().Be(PlatformStatusDto.Approved);
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            Succeeded(await commands.SendAsync(new RefreshPlatformStatusCommand(dossier, approved), default)).History.Should().HaveCount(5);

            // Encaissements : partiel, puis le solde ; au-delà du TTC, avant la pièce ou après le solde, rien ne part.
            decimal total = (await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>().QueryAsync(new GetInvoiceQuery(dossier, approved), default)).Value.Totals.TotalInclTax;
            PlatformTransmissionResponse partial = Succeeded(await commands.SendAsync(new ReportPaymentCommand(dossier, approved, new DateOnly(2026, 11, 2), 100m), default));
            partial.Status.Should().Be(PlatformStatusDto.Collected);
            partial.IsFinal.Should().BeFalse();
            partial.CollectedAmount.Should().Be(100m);
            Codes(await commands.SendAsync(new ReportPaymentCommand(dossier, approved, new DateOnly(2026, 11, 3), total), default))
                .Should().Equal(ErrorCodes.Platform.PaymentAmountInvalid);
            Codes(await commands.SendAsync(new ReportPaymentCommand(dossier, approved, new DateOnly(2000, 1, 1), 1m), default))
                .Should().Equal(ErrorCodes.Platform.PaymentDateInvalid);
            PlatformTransmissionResponse paid = Succeeded(await commands.SendAsync(new ReportPaymentCommand(dossier, approved, new DateOnly(2026, 11, 14), total - 100m), default));
            paid.IsFinal.Should().BeTrue();
            paid.CollectedAmount.Should().Be(total);
            Codes(await commands.SendAsync(new ReportPaymentCommand(dossier, approved, new DateOnly(2026, 11, 15), 1m), default))
                .Should().Equal(ErrorCodes.Platform.AlreadyFinal);
        }

        // Relue dans un autre scope : l'historique enregistré dans le dossier, dans l'ordre, montants compris.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            PlatformTransmissionResponse stored = Succeeded(await queries.QueryAsync(new GetPlatformTransmissionQuery(dossier, approved), default));
            stored.History.Select(h => h.Status).Should().Equal(
                PlatformStatusDto.Deposited, PlatformStatusDto.Issued, PlatformStatusDto.Received, PlatformStatusDto.MadeAvailable,
                PlatformStatusDto.Approved, PlatformStatusDto.Collected, PlatformStatusDto.Collected);
            stored.History[^2].Amount.Should().Be(100m);
            stored.IsFinal.Should().BeTrue();
            Succeeded(await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                .SendAsync(new RefreshPlatformStatusCommand(dossier, approved), default)).History.Should().HaveCount(7);
        }

        // Destinataire absent de l'annuaire : Rejetée, la pièce reste validée ; retransmise une fois l'annuaire à jour.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            platform.Unregister(Durand.Siren!);
            PlatformTransmissionResponse first = Succeeded(await commands.SendAsync(new TransmitInvoiceCommand(dossier, rejected, EInvoiceFormatDto.Ubl), default));
            first.Status.Should().Be(PlatformStatusDto.Rejected);
            first.IsFinal.Should().BeTrue();
            first.History[0].Reason.Should().Contain("annuaire");
            (await StatusOfAsync(scope, dossier, rejected)).Should().Be(InvoiceStatusDto.Validated);
            Codes(await commands.SendAsync(new ReportPaymentCommand(dossier, rejected, new DateOnly(2026, 11, 2), 10m), default))
                .Should().Equal(ErrorCodes.Platform.NotTransmitted);

            platform.Register(Durand.Siren!);
            PlatformTransmissionResponse second = Succeeded(await commands.SendAsync(new TransmitInvoiceCommand(dossier, rejected, EInvoiceFormatDto.Ubl), default));
            second.Id.Should().NotBe(first.Id);
            second.Format.Should().Be(EInvoiceFormatDto.Ubl);
            second.Status.Should().Be(PlatformStatusDto.Deposited);
            (await StatusOfAsync(scope, dossier, rejected)).Should().Be(InvoiceStatusDto.Issued);
        }

        // Refus de l'acheteur : définitif, plus d'encaissement possible.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            string reference = Succeeded(await commands.SendAsync(new TransmitInvoiceCommand(dossier, refused), default)).PlatformReference!;
            Succeeded(await commands.SendAsync(new RefreshPlatformStatusCommand(dossier, refused), default));
            platform.SimulateBuyerStatus(reference, PlatformStatus.Refused, "Prestation non commandée");
            PlatformTransmissionResponse closed = Succeeded(await commands.SendAsync(new RefreshPlatformStatusCommand(dossier, refused), default));
            closed.Status.Should().Be(PlatformStatusDto.Refused);
            closed.IsFinal.Should().BeTrue();
            closed.History[^1].Reason.Should().Be("Prestation non commandée");
            Codes(await commands.SendAsync(new ReportPaymentCommand(dossier, refused, new DateOnly(2026, 11, 2), 10m), default))
                .Should().Equal(ErrorCodes.Platform.AlreadyFinal);
        }

        // Vente hors de France : e-reporting de la transaction, sans facture électronique ni circuit d'acheminement.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            PlatformTransmissionResponse reported = Succeeded(await commands.SendAsync(new TransmitInvoiceCommand(dossier, abroad), default));
            reported.Channel.Should().Be(PlatformChannelDto.EReporting);
            reported.Format.Should().BeNull();
            reported.Status.Should().Be(PlatformStatusDto.Deposited);
            Succeeded(await commands.SendAsync(new RefreshPlatformStatusCommand(dossier, abroad), default)).History.Should().ContainSingle();
            (await StatusOfAsync(scope, dossier, abroad)).Should().Be(InvoiceStatusDto.Issued);
        }

        // Plateforme injoignable : rien n'est enregistré, la pièce reste validée, le dépôt attend dans l'outbox (5.4).
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            platform.IsUnavailable = true;
            Codes(await commands.SendAsync(new TransmitInvoiceCommand(dossier, outage), default)).Should().Equal(ErrorCodes.Platform.Queued);
            platform.IsUnavailable = false;
            (await StatusOfAsync(scope, dossier, outage)).Should().Be(InvoiceStatusDto.Validated);
            Codes(await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>().QueryAsync(new GetPlatformTransmissionQuery(dossier, outage), default))
                .Should().Equal(ErrorCodes.Platform.NotTransmitted);
        }

        // Plateforme revenue : redemander la transmission renvoie le dépôt en attente tout de suite, sans en créer un second.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            Succeeded(await commands.SendAsync(new TransmitInvoiceCommand(dossier, outage), default)).Status.Should().Be(PlatformStatusDto.Deposited);
            (await StatusOfAsync(scope, dossier, outage)).Should().Be(InvoiceStatusDto.Issued);
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

            // Un brouillon ne se transmet pas.
            InvoiceId draft = (await commands.SendAsync(new CreateInvoiceCommand(dossier, Request(durand, 10m, VatRegimeDto.Standard)), default)).Value;
            Codes(await commands.SendAsync(new TransmitInvoiceCommand(dossier, draft), default)).Should().Equal(ErrorCodes.EInvoice.NotValidated);
        }

        // Chaque statut est une entrée chaînée et scellée du journal ; le contrôle d'intégrité les compare à la base.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            LettraDbContext db = await scope.ServiceProvider.GetRequiredService<TenantDbSession>().GetAsync(default);
            List<JournalEntry> entries = await db.Set<JournalEntry>().AsNoTracking().Where(e => e.DocumentId == approved.Value).ToListAsync();
            entries.Where(e => e.IsPlatformEvent).OrderBy(e => e.Sequence).Select(e => e.Kind).Should().Equal(
                JournalEntryKind.PlatformTransmitted,
                JournalEntryKind.PlatformStatusReceived, JournalEntryKind.PlatformStatusReceived, JournalEntryKind.PlatformStatusReceived,
                JournalEntryKind.PlatformStatusReceived,
                JournalEntryKind.PlatformPaymentReported, JournalEntryKind.PlatformPaymentReported);
            entries.Single(e => e.Kind == JournalEntryKind.PlatformTransmitted).Number.Should().NotBeNullOrWhiteSpace();
            entries.Where(e => e.Kind == JournalEntryKind.PlatformPaymentReported).Sum(e => e.TotalInclTax).Should().BeGreaterThan(0m);
            (await db.Set<JournalEntry>().AsNoTracking().CountAsync(e => e.Kind == JournalEntryKind.PlatformTransmitted)).Should().Be(6, "deux envois de la pièce rejetée, quatre autres pièces");

            IntegrityReport intact = (await scope.ServiceProvider.GetRequiredService<DossierIntegrityService>().VerifyAsync(default)).Value;
            intact.Findings.Should().BeEmpty();
        }

        // Un motif retouché en base, puis un statut ajouté sans entrée au journal : l'historique ne correspond plus.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            LettraDbContext db = await scope.ServiceProvider.GetRequiredService<TenantDbSession>().GetAsync(default);
            Guid refusal = (await db.Set<PlatformTransmissionRecord>().AsNoTracking().SingleAsync(t => t.InvoiceId == refused.Value)).Id;
            await db.Set<PlatformStatusEventRecord>()
                .Where(e => e.TransmissionId == refusal && e.Status == (int)PlatformStatusDto.Refused)
                .ExecuteUpdateAsync(u => u.SetProperty(e => e.Reason, "Accepté"));
            IntegrityReport altered = (await scope.ServiceProvider.GetRequiredService<DossierIntegrityService>().VerifyAsync(default)).Value;
            altered.Findings.Select(f => f.Kind).Should().Equal(IntegrityFindingKind.PlatformHistoryAltered);
            altered.ChainCompromised.Should().BeTrue();
            await db.Set<PlatformStatusEventRecord>()
                .Where(e => e.TransmissionId == refusal && e.Status == (int)PlatformStatusDto.Refused)
                .ExecuteUpdateAsync(u => u.SetProperty(e => e.Reason, "Prestation non commandée"));
            (await scope.ServiceProvider.GetRequiredService<DossierIntegrityService>().VerifyAsync(default)).Value.Findings.Should().BeEmpty();

            Guid abroadTransmission = (await db.Set<PlatformTransmissionRecord>().AsNoTracking().SingleAsync(t => t.InvoiceId == abroad.Value)).Id;
            db.Add(new PlatformStatusEventRecord
            {
                TransmissionId = abroadTransmission,
                Sequence = 1,
                Status = (int)PlatformStatusDto.Collected,
                AtMs = 1,
                Amount = 1m,
            });
            await db.SaveChangesAsync();
            (await scope.ServiceProvider.GetRequiredService<DossierIntegrityService>().VerifyAsync(default)).Value
                .Findings.Select(f => f.Kind).Should().Equal(IntegrityFindingKind.PlatformHistoryAltered);
        }
    }

    /// <summary>Sans plateforme configurée (le défaut), la transmission échoue et la pièce reste validée.</summary>
    public static async Task NotConfiguredAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
        Guid durand = (await commands.SendAsync(new CreateCustomerCommand(dossier, Durand), default)).Value.Value;
        InvoiceId invoice = await ValidatedAsync(commands, dossier, durand, 100m);

        Codes(await commands.SendAsync(new TransmitInvoiceCommand(dossier, invoice), default)).Should().Equal(ErrorCodes.Platform.NotConfigured);
        (await StatusOfAsync(scope, dossier, invoice)).Should().Be(InvoiceStatusDto.Validated);
    }

    /// <summary>
    /// Envois par l'outbox (étape 5.4). Plateforme injoignable : le dépôt attend et le Worker l'envoie au retour de la
    /// plateforme, puis la pièce s'émet à sa prochaine transmission demandée, sans second dépôt. Réponse perdue après
    /// l'acceptation (arrêt, coupure) : la reprise, sous la même clé, rend le même accusé ; dépôt et encaissement ne sont
    /// enregistrés et journalisés qu'une fois, et le contrôle d'intégrité reste sans constat.
    /// </summary>
    public static async Task OutboxAsync(TenantHarness harness, ManualClock clock, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        SimulatedAccreditedPlatform platform = harness.Services.GetRequiredService<SimulatedAccreditedPlatform>();
        LosingPlatform network = harness.Services.GetRequiredService<LosingPlatform>();
        InvoiceId waiting, lost;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
            Guid durand = (await commands.SendAsync(new CreateCustomerCommand(dossier, Durand), default)).Value.Value;
            waiting = await ValidatedAsync(commands, dossier, durand, 200m);
            lost = await ValidatedAsync(commands, dossier, durand, 100m);

            platform.IsUnavailable = true;
            Codes(await commands.SendAsync(new TransmitInvoiceCommand(dossier, waiting), default)).Should().Equal(ErrorCodes.Platform.Queued);
            platform.IsUnavailable = false;

            network.LoseNextResponse = true;
            Codes(await commands.SendAsync(new TransmitInvoiceCommand(dossier, lost), default)).Should().Equal(ErrorCodes.Platform.Queued);
            platform.DepositCount.Should().Be(1, "la plateforme a reçu le dépôt, sa réponse s'est perdue");
        }

        clock.Now = clock.Now.AddSeconds(30);
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            (await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchDueAsync(default)).Should().Be(new OutboxPass(2, 0, 0));
            platform.DepositCount.Should().Be(2, "la reprise sous la même clé rend le premier accusé");
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            Succeeded(await queries.QueryAsync(new GetPlatformTransmissionQuery(dossier, waiting), default)).Status.Should().Be(PlatformStatusDto.Deposited);
            Succeeded(await queries.QueryAsync(new GetPlatformTransmissionQuery(dossier, lost), default)).Status.Should().Be(PlatformStatusDto.Deposited);
            (await StatusOfAsync(scope, dossier, waiting)).Should().Be(InvoiceStatusDto.Validated, "l'émission reste à la commande de l'utilisateur");
        }

        Guid transmission;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            transmission = Succeeded(await commands.SendAsync(new TransmitInvoiceCommand(dossier, waiting), default)).Id;
            (await StatusOfAsync(scope, dossier, waiting)).Should().Be(InvoiceStatusDto.Issued);
            platform.DepositCount.Should().Be(2);
            Succeeded(await commands.SendAsync(new TransmitInvoiceCommand(dossier, lost), default));

            network.LoseNextResponse = true;
            Codes(await commands.SendAsync(new ReportPaymentCommand(dossier, waiting, new DateOnly(2026, 11, 2), 50m), default))
                .Should().Equal(ErrorCodes.Platform.Queued);
        }

        clock.Now = clock.Now.AddMinutes(1);
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            (await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchDueAsync(default)).Should().Be(new OutboxPass(1, 0, 0));
            PlatformTransmissionResponse paid = Succeeded(await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
                .QueryAsync(new GetPlatformTransmissionQuery(dossier, waiting), default));
            paid.Id.Should().Be(transmission);
            paid.CollectedAmount.Should().Be(50m, "un seul encaissement enregistré");

            LettraDbContext db = await scope.ServiceProvider.GetRequiredService<TenantDbSession>().GetAsync(default);
            List<JournalEntry> entries = await db.Set<JournalEntry>().AsNoTracking().Where(e => e.DocumentId == waiting.Value || e.DocumentId == lost.Value).ToListAsync();
            entries.Count(e => e.Kind == JournalEntryKind.PlatformTransmitted).Should().Be(2);
            entries.Count(e => e.Kind == JournalEntryKind.PlatformPaymentReported).Should().Be(1);
            (await scope.ServiceProvider.GetRequiredService<DossierIntegrityService>().VerifyAsync(default)).Value.Findings.Should().BeEmpty();
        }
    }

    private static async Task<InvoiceId> ValidatedAsync(ICommandDispatcher commands, DossierId dossier, Guid customer, decimal price, VatRegimeDto regime = VatRegimeDto.Standard)
    {
        Result<InvoiceId> created = await commands.SendAsync(new CreateInvoiceCommand(dossier, Request(customer, price, regime)), default);
        created.IsSuccess.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Code)));
        Result validated = await commands.SendAsync(new ValidateInvoiceCommand(dossier, created.Value, null), default);
        validated.IsSuccess.Should().BeTrue(string.Join(", ", validated.Errors.Select(e => e.Code)));
        return created.Value;
    }

    private static SaveInvoiceRequest Request(Guid customer, decimal price, VatRegimeDto regime) => new()
    {
        CustomerId = customer,
        VatRegime = regime,
        Lines = [new DocumentLineRequest { Designation = "Conseil", Quantity = 1, UnitPriceExclTax = price, VatRateCode = "FR-20", Kind = CatalogItemKindDto.Service, UnitCode = "HUR" }],
    };

    private static async Task<InvoiceStatusDto> StatusOfAsync(AsyncServiceScope scope, DossierId dossier, InvoiceId invoice) =>
        (await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>().QueryAsync(new GetInvoiceQuery(dossier, invoice), default)).Value.Status;

    private static PlatformTransmissionResponse Succeeded(Result<PlatformTransmissionResponse> result)
    {
        result.IsSuccess.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Code)));
        return result.Value;
    }

    private static IEnumerable<string> Codes(Result<PlatformTransmissionResponse> result) => result.Errors.Select(e => e.Code);
}

/// <summary>Réseau qui perd la réponse : la plateforme a traité l'appel, l'appelant ne le sait pas.</summary>
internal sealed class LosingPlatform(SimulatedAccreditedPlatform inner) : IAccreditedPlatformClient
{
    public bool LoseNextResponse { get; set; }

    public string Name => inner.Name;

    public Task<Result<PlatformReceipt>> SubmitInvoiceAsync(PlatformInvoiceSubmission submission, CancellationToken cancellationToken) =>
        LoseAsync(inner.SubmitInvoiceAsync(submission, cancellationToken));

    public Task<Result<PlatformReceipt>> ReportTransactionAsync(PlatformTransactionReport report, CancellationToken cancellationToken) =>
        LoseAsync(inner.ReportTransactionAsync(report, cancellationToken));

    public Task<Result<IReadOnlyList<PlatformStatusEvent>>> GetStatusEventsAsync(string reference, CancellationToken cancellationToken) =>
        inner.GetStatusEventsAsync(reference, cancellationToken);

    public Task<Result<PlatformStatusEvent>> ReportPaymentAsync(PlatformPaymentReport payment, CancellationToken cancellationToken) =>
        LoseAsync(inner.ReportPaymentAsync(payment, cancellationToken));

    private async Task<T> LoseAsync<T>(Task<T> call)
    {
        T result = await call.ConfigureAwait(false);
        if (LoseNextResponse)
        {
            LoseNextResponse = false;
            throw new HttpRequestException("Connexion coupée avant la réponse.");
        }

        return result;
    }
}
