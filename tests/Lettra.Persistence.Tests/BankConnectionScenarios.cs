using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Banque connectée sur les trois bases : consentement refusé puis donné, 90 jours d'opérations importés comme un relevé,
/// relecture sans doublon (même en parallèle), relecture des comptes dus, renouvellement, échéance, déconnexion.
/// </summary>
internal static class BankConnectionScenarios
{
    public static readonly DateTimeOffset Start = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Customer.Draft Martin = new(
        CustomerKind.Company, "Boulangerie Martin", "732829320", "73282932000074", "FR44732829320", "compta@martin.fr",
        "1 rue de la Paix", null, "75002", "Paris", "FR");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Lettra", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: false, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: "FR7630006000011234567890189", Bic: "AGRIFRPP", VatDeclarationRegime: null);

    private static async Task<T> Send<T>(TenantHarness harness, DossierId dossier, ICommand<T> command)
    {
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(command, default);
    }

    private static async Task<T> Query<T>(TenantHarness harness, DossierId dossier, IQuery<T> query)
    {
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        return await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>().QueryAsync(query, default);
    }

    private static DocumentLineRequest Line(decimal price) =>
        new() { Designation = "Conseil", Quantity = 1, UnitPriceExclTax = price, VatRateCode = "FR-20", Kind = CatalogItemKindDto.Service, UnitCode = "HUR" };

    private static Task<Result<BankConsentRedirectResponse>> StartAsync(TenantHarness harness, DossierId dossier, Guid? replaces = null) =>
        Send(harness, dossier, new StartBankConnectionCommand(dossier, new StartBankConnectionRequest { InstitutionId = "sim-lumiere", ReplacesId = replaces }));

    private static Task<Result<BankConnectionCompletedResponse>> CompleteAsync(TenantHarness harness, DossierId dossier, Guid id, string? code) =>
        Send(harness, dossier, new CompleteBankConnectionCommand(dossier, id, new CompleteBankConnectionRequest { Code = code }));

    private static Task<Result<SyncBankConnectionsResponse>> SyncAsync(TenantHarness harness, DossierId dossier, Guid? id = null, bool onlyDue = false) =>
        Send(harness, dossier, new SyncBankConnectionsCommand(dossier, new SyncBankConnectionsRequest { ConnectionId = id, OnlyDue = onlyDue }));

    public static async Task ConnectedBankAsync(TenantHarness harness, ManualClock clock, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            Guid customer = (await commands.SendAsync(new CreateCustomerCommand(dossier, Martin), default)).Value.Value;
            (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
            for (int i = 1; i <= 8; i++)
            {
                InvoiceId invoice = (await commands.SendAsync(new CreateInvoiceCommand(dossier, new SaveInvoiceRequest { CustomerId = customer, Lines = [Line(100m * i)] }), default)).Value;
                (await commands.SendAsync(new ValidateInvoiceCommand(dossier, invoice, null), default)).IsSuccess.Should().BeTrue();
            }
        }

        // Les virements arrivent dans les semaines qui suivent l'émission.
        clock.Now = Start.AddDays(40);

        BankConnectionsResponse empty = (await Query(harness, dossier, new GetBankConnectionsQuery(dossier))).Value;
        empty.Available.Should().BeTrue();
        empty.Institutions.Should().HaveCount(3);
        empty.Connections.Should().BeEmpty();
        (await Send(harness, dossier, new StartBankConnectionCommand(dossier, new StartBankConnectionRequest { InstitutionId = "banque-inconnue" })))
            .Errors[0].Code.Should().Be(ErrorCodes.BankConnection.InstitutionUnknown);

        // Consentement refusé sur la page de la banque, puis donné.
        BankConsentRedirectResponse consent = (await StartAsync(harness, dossier)).Value;
        consent.RedirectUrl.Should().StartWith($"{BankConnectionRoutes.SimulatedConsent}?connection={consent.ConnectionId}&bank=");
        (await Query(harness, dossier, new GetBankConnectionsQuery(dossier))).Value.Connections.Should().BeEmpty("un consentement pas encore donné n'est pas montré");
        (await CompleteAsync(harness, dossier, consent.ConnectionId, BankConnectionRoutes.SimulatedRefusedCode))
            .Errors[0].Code.Should().Be(ErrorCodes.BankConnection.ConsentRefused);
        (await CompleteAsync(harness, dossier, Guid.CreateVersion7(), BankConnectionRoutes.SimulatedGrantedCode))
            .Errors[0].Code.Should().Be(ErrorCodes.BankConnection.NotFound);

        BankConnectionCompletedResponse linked = (await CompleteAsync(harness, dossier, consent.ConnectionId, BankConnectionRoutes.SimulatedGrantedCode)).Value;
        linked.Connection.Should().BeEquivalentTo(new
        {
            Status = BankConnectionStatusDto.Active,
            InstitutionId = "sim-lumiere",
            InstitutionName = "Banque Lumière (simulée)",
            NeedsRenewal = false,
            ConsentExpiresAt = (DateTimeOffset?)clock.Now.AddDays(180),
        });
        linked.Connection.AccountDisplay.Should().MatchRegex(@"^FR\d\d •••• \d{4}$", "le compte n'est gardé que masqué");
        linked.FirstSync.Errors.Should().BeEmpty();
        linked.FirstSync.Imported.Should().BeGreaterThanOrEqualTo(2, "au moins deux frais mensuels tombent dans les 90 jours");
        (await CompleteAsync(harness, dossier, consent.ConnectionId, BankConnectionRoutes.SimulatedGrantedCode))
            .Errors[0].Code.Should().Be(ErrorCodes.BankConnection.AlreadyCompleted);

        // Les opérations sont au rapprochement comme un relevé, avec leurs propositions.
        ReconciliationResponse reconciliation = (await Query(harness, dossier, new GetReconciliationQuery(dossier))).Value;
        reconciliation.Lines.Should().HaveCount(linked.FirstSync.Imported);
        reconciliation.Lines.Where(l => l.Amount > 0m).Should().OnlyContain(l => l.Proposal != null, "chaque virement paie une facture exacte");
        reconciliation.Lines.Count(l => l.Amount > 0m).Should().Be(linked.FirstSync.Proposals);
        reconciliation.Imports.Should().ContainSingle().Which.Format.Should().Be(BankStatementFormatDto.Connection);

        // Relire tout de suite : rien de nouveau ; les comptes « dus » ne le sont pas encore.
        BankConnectionSyncOutcome again = (await SyncAsync(harness, dossier, consent.ConnectionId)).Value.Outcomes.Should().ContainSingle().Subject;
        again.Imported.Should().Be(0);
        again.Errors.Should().BeEmpty();
        (await SyncAsync(harness, dossier, onlyDue: true)).Value.Outcomes.Should().BeEmpty();

        // Deux relectures en même temps : jamais de doublon ni d'erreur inattendue.
        clock.Now = clock.Now.AddHours(7);
        Result<SyncBankConnectionsResponse>[] parallel = await Task.WhenAll(
            SyncAsync(harness, dossier, onlyDue: true), SyncAsync(harness, dossier, onlyDue: true));
        parallel.Should().OnlyContain(r => r.IsSuccess);
        parallel.SelectMany(r => r.Value.Outcomes).Sum(o => o.Imported).Should().Be(0);
        parallel.SelectMany(r => r.Value.Outcomes).SelectMany(o => o.Errors).Select(e => e.Code)
            .Should().BeSubsetOf([ErrorCodes.BankConnection.SyncInProgress, "Concurrency.VersionMismatch"]);
        (await Query(harness, dossier, new GetReconciliationQuery(dossier))).Value.Lines.Should().HaveCount(linked.FirstSync.Imported);

        // Un mois plus tard, de nouveaux frais : la relecture les ajoute.
        clock.Now = clock.Now.AddDays(31);
        (await SyncAsync(harness, dossier)).Value.Outcomes.Should().ContainSingle().Which.Imported.Should().BeGreaterThanOrEqualTo(1);

        // À 15 jours de l'échéance, l'accord est à renouveler ; le renouvellement remplace l'ancienne connexion.
        clock.Now = Start.AddDays(40 + 170);
        (await Query(harness, dossier, new GetBankConnectionsQuery(dossier))).Value.Connections.Should().ContainSingle().Which.NeedsRenewal.Should().BeTrue();
        (await StartAsync(harness, dossier, replaces: Guid.CreateVersion7())).Errors[0].Code.Should().Be(ErrorCodes.BankConnection.NotFound);
        BankConsentRedirectResponse renewal = (await StartAsync(harness, dossier, replaces: consent.ConnectionId)).Value;
        BankConnectionCompletedResponse renewed = (await CompleteAsync(harness, dossier, renewal.ConnectionId, BankConnectionRoutes.SimulatedGrantedCode)).Value;
        renewed.FirstSync.Errors.Should().BeEmpty();
        IReadOnlyList<BankConnectionResponse> both = (await Query(harness, dossier, new GetBankConnectionsQuery(dossier))).Value.Connections;
        both.Should().HaveCount(2);
        both.Single(c => c.Id == consent.ConnectionId).Status.Should().Be(BankConnectionStatusDto.Revoked);
        both.Single(c => c.Id == renewal.ConnectionId).Should().BeEquivalentTo(new { Status = BankConnectionStatusDto.Active, NeedsRenewal = false });

        // Échéance passée : le compte ne se relit plus.
        clock.Now = clock.Now.AddDays(181);
        (await Query(harness, dossier, new GetBankConnectionsQuery(dossier))).Value.Connections.Single(c => c.Id == renewal.ConnectionId)
            .Status.Should().Be(BankConnectionStatusDto.Expired);
        (await SyncAsync(harness, dossier, renewal.ConnectionId)).Value.Outcomes.Single().Errors[0].Code.Should().Be(ErrorCodes.BankConnection.ConsentExpired);
        (await SyncAsync(harness, dossier)).Value.Outcomes.Should().BeEmpty("seuls les comptes actifs se relisent");

        // Déconnexion : définitive, les opérations restent.
        int lines = (await Query(harness, dossier, new GetReconciliationQuery(dossier))).Value.Lines.Count;
        (await Send(harness, dossier, new RevokeBankConnectionCommand(dossier, renewal.ConnectionId))).Value.Status.Should().Be(BankConnectionStatusDto.Revoked);
        (await Send(harness, dossier, new RevokeBankConnectionCommand(dossier, renewal.ConnectionId))).Errors[0].Code.Should().Be(ErrorCodes.BankConnection.NotActive);
        (await Send(harness, dossier, new RevokeBankConnectionCommand(dossier, Guid.CreateVersion7()))).Errors[0].Code.Should().Be(ErrorCodes.BankConnection.NotFound);
        (await SyncAsync(harness, dossier, renewal.ConnectionId)).Value.Outcomes.Single().Errors[0].Code.Should().Be(ErrorCodes.BankConnection.NotActive);
        (await SyncAsync(harness, dossier, Guid.CreateVersion7())).Errors[0].Code.Should().Be(ErrorCodes.BankConnection.NotFound);
        (await Query(harness, dossier, new GetReconciliationQuery(dossier))).Value.Lines.Should().HaveCount(lines);
    }
}
