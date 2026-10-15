using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Infrastructure;
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
            platform.SimulateBuyerStatus(refreshed.PlatformReference!, PlatformStatusDto.Approved);
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
            platform.SimulateBuyerStatus(reference, PlatformStatusDto.Refused, "Prestation non commandée");
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

        // Plateforme injoignable : rien n'est enregistré, la pièce reste validée.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            platform.IsUnavailable = true;
            Codes(await commands.SendAsync(new TransmitInvoiceCommand(dossier, outage), default)).Should().Equal(ErrorCodes.Platform.Unavailable);
            platform.IsUnavailable = false;
            (await StatusOfAsync(scope, dossier, outage)).Should().Be(InvoiceStatusDto.Validated);
            Codes(await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>().QueryAsync(new GetPlatformTransmissionQuery(dossier, outage), default))
                .Should().Equal(ErrorCodes.Platform.NotTransmitted);

            // Un brouillon ne se transmet pas.
            InvoiceId draft = (await commands.SendAsync(new CreateInvoiceCommand(dossier, Request(durand, 10m, VatRegimeDto.Standard)), default)).Value;
            Codes(await commands.SendAsync(new TransmitInvoiceCommand(dossier, draft), default)).Should().Equal(ErrorCodes.EInvoice.NotValidated);
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
