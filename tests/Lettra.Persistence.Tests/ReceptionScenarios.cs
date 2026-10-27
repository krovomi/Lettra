using System.Text;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.EInvoicing;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Application.EInvoicing.Reception;
using Lettra.Exports.Domain.Platform;
using Lettra.Exports.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Réception des factures fournisseurs par la plateforme agréée simulée (réforme de la facturation électronique) :
/// relevé sans doublon, fichiers illisibles ou adressés à un autre SIREN laissés de côté, rapprochement du fournisseur
/// par son SIREN, réponses de l'acheteur enregistrées et transmises, refus définitif, annuaire. Données fictives.
/// </summary>
internal static class ReceptionScenarios
{
    private const string CompanySiren = "552100554";

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Fictif", "SAS", 10_000m, CompanySiren, "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: false, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: "FR7630006000011234567890189", Bic: "AGRIFRPP", Size: Lettra.Calculations.Compliance.CompanySize.SmallOrMedium);

    private static readonly Supplier.Draft Moreau = new(
        "Papeterie Moreau", "842157307", null, "FR39842157307", "compta@moreau.example", null,
        "4 quai de la Fosse", null, "44000", "Nantes", "FR");

    public static async Task ReceptionAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        SimulatedAccreditedPlatform platform = harness.Services.GetRequiredService<SimulatedAccreditedPlatform>();
        Guid moreau;

        // Sans SIREN au profil, rien à relever : la plateforme ne sait pas à qui rendre les factures.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            (await commands.SendAsync(new FetchReceivedInvoicesCommand(dossier), default)).Errors.Select(e => e.Code)
                .Should().Equal(ErrorCodes.Reception.CompanySirenMissing);
            (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
            moreau = (await commands.SendAsync(new CreateSupplierCommand(dossier, Moreau), default)).Value.Value;
        }

        platform.DeliverIncoming(CompanySiren, new PlatformIncomingInvoice(
            "PAS-R-ILLISIBLE", EInvoiceFormatDto.Cii, "illisible.xml", Encoding.UTF8.GetBytes("<pas-une-facture/>"), DateTimeOffset.UnixEpoch));

        // Premier relevé : les quatre factures fictives, le fichier illisible compté à part ; le second n'ajoute rien.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            FetchReceivedInvoicesResponse first = Succeeded(await commands.SendAsync(new FetchReceivedInvoicesCommand(dossier), default));
            first.Should().Be(new FetchReceivedInvoicesResponse(4, 1));
            Succeeded(await commands.SendAsync(new FetchReceivedInvoicesCommand(dossier), default)).Should().Be(new FetchReceivedInvoicesResponse(0, 1));
        }

        Guid stationery, transport, creditNote;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            ReceivedInvoicesResponse list = Succeeded(await queries.QueryAsync(new GetReceivedInvoicesQuery(dossier), default));
            list.PlatformConfigured.Should().BeTrue();
            list.CompanySiren.Should().Be(CompanySiren);
            list.Directory!.ElectronicAddress.Should().Be($"0225:{CompanySiren}");
            list.ReceivingFrom.Should().Be(new DateOnly(2026, 9, 1));
            list.IssuingFrom.Should().Be(new DateOnly(2027, 9, 1), "une PME émet à partir de la seconde vague");
            list.Invoices.Should().HaveCount(4).And.OnlyContain(i => i.Status == PlatformStatusDto.MadeAvailable);
            list.Invoices.Should().BeInDescendingOrder(i => i.ReceivedAt);

            ReceivedInvoiceSummary[] fromMoreau = [.. list.Invoices.Where(i => i.SupplierSiren == "842157307")];
            fromMoreau.Should().HaveCount(2).And.OnlyContain(i => i.SupplierId == moreau);
            list.Invoices.Where(i => i.SupplierSiren != "842157307").Should().OnlyContain(i => i.SupplierId == null);

            stationery = list.Invoices.Single(i => i.Number == "PM-2026-0412").Id;
            transport = list.Invoices.Single(i => i.Number == "TL-26-1187").Id;
            creditNote = list.Invoices.Single(i => i.Number == "PM-2026-0430").Id;
            list.Invoices.Single(i => i.Id == creditNote).Kind.Should().Be(ReceivedInvoiceKindDto.CreditNote);

            ReceivedInvoiceResponse detail = Succeeded(await queries.QueryAsync(new GetReceivedInvoiceQuery(dossier, stationery), default));
            detail.Format.Should().Be(EInvoiceFormatDto.Cii);
            detail.BusinessProcess.Should().Be("B1");
            detail.Lines.Should().HaveCount(2);
            detail.Vat.Should().ContainSingle().Which.Rate.Should().Be(20m);
            detail.Lines.Sum(l => l.NetAmount).Should().Be(detail.Summary.TotalExclTax);
            detail.AvailableResponses.Should().Contain([PlatformStatusDto.InProgress, PlatformStatusDto.Approved, PlatformStatusDto.Refused]);

            Result<EInvoiceFile> file = await queries.QueryAsync(new DownloadReceivedInvoiceQuery(dossier, stationery), default);
            file.Value.FileName.Should().EndWith(".xml");
            Encoding.UTF8.GetString(file.Value.Content).Should().Contain("PM-2026-0412");

            (await queries.QueryAsync(new GetReceivedInvoiceQuery(dossier, Guid.NewGuid()), default)).Errors.Select(e => e.Code)
                .Should().Equal(ErrorCodes.Reception.NotFound);
            Succeeded(await queries.QueryAsync(new GetReceivedInvoiceQuery(dossier, creditNote), default))
                .AvailableResponses.Should().NotContain(PlatformStatusDto.PaymentSent, "un avoir ne se paie pas");
        }

        // Réponses : prise en charge, approbation, paiement transmis ; le litige et le refus se motivent ; le refus clôt.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            Succeeded(await RespondAsync(commands, dossier, stationery, PlatformStatusDto.InProgress)).Summary.Status.Should().Be(PlatformStatusDto.InProgress);
            Succeeded(await RespondAsync(commands, dossier, stationery, PlatformStatusDto.Approved)).Summary.Status.Should().Be(PlatformStatusDto.Approved);
            Codes(await RespondAsync(commands, dossier, stationery, PlatformStatusDto.PaymentSent)).Should().Equal(ErrorCodes.Reception.PaymentAmountInvalid);
            ReceivedInvoiceResponse paid = Succeeded(await RespondAsync(commands, dossier, stationery, PlatformStatusDto.PaymentSent, amount: 164.40m));
            paid.History.Select(h => h.Status).Should().Equal(
                PlatformStatusDto.MadeAvailable, PlatformStatusDto.InProgress, PlatformStatusDto.Approved, PlatformStatusDto.PaymentSent);
            paid.History[^1].Amount.Should().Be(164.40m);

            Codes(await RespondAsync(commands, dossier, transport, PlatformStatusDto.Disputed, reason: "  ")).Should().Equal(ErrorCodes.Reception.ReasonRequired);
            Codes(await RespondAsync(commands, dossier, transport, PlatformStatusDto.Disputed, reason: new string('x', 501))).Should().Equal(ErrorCodes.Reception.ReasonTooLong);
            Succeeded(await RespondAsync(commands, dossier, transport, PlatformStatusDto.Disputed, reason: "Manutention non commandée"))
                .Summary.Status.Should().Be(PlatformStatusDto.Disputed);
            ReceivedInvoiceResponse refused = Succeeded(await RespondAsync(commands, dossier, transport, PlatformStatusDto.Refused, reason: "Livraison jamais reçue"));
            refused.AvailableResponses.Should().BeEmpty();
            Codes(await RespondAsync(commands, dossier, transport, PlatformStatusDto.Approved)).Should().Equal(ErrorCodes.Reception.ResponseNotAllowed);
            Codes(await RespondAsync(commands, dossier, Guid.NewGuid(), PlatformStatusDto.Approved)).Should().Equal(ErrorCodes.Reception.NotFound);
        }

        // Relu dans un autre scope : l'historique enregistré, motifs compris, et transmis tel quel à la plateforme.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            ReceivedInvoiceResponse stored = Succeeded(await queries.QueryAsync(new GetReceivedInvoiceQuery(dossier, transport), default));
            stored.History.Select(h => h.Reason).Should().Equal(null, "Manutention non commandée", "Livraison jamais reçue");
            platform.IncomingHistory(stored.Summary.Reference).Select(h => h.Status).Should().Equal(
                PlatformStatus.MadeAvailable, PlatformStatus.Disputed, PlatformStatus.Refused);

            // Annuaire : un SIREN inscrit, un SIREN mal formé.
            DirectoryLookupResponse found = Succeeded(await queries.QueryAsync(new LookupDirectoryQuery(dossier, "732 829 320"), default));
            found.Consulted.Should().BeTrue();
            found.Entry!.ElectronicAddress.Should().Be("0225:732829320");
            (await queries.QueryAsync(new LookupDirectoryQuery(dossier, "12345"), default)).Errors.Select(e => e.Code)
                .Should().Equal(ErrorCodes.Reception.SirenInvalid);
        }

        // Plateforme en panne : le relevé échoue proprement, les factures déjà reçues restent lisibles.
        platform.IsUnavailable = true;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            (await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new FetchReceivedInvoicesCommand(dossier), default))
                .IsFailure.Should().BeTrue();
            ReceivedInvoicesResponse list = Succeeded(await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
                .QueryAsync(new GetReceivedInvoicesQuery(dossier), default));
            list.Invoices.Should().HaveCount(4);
            list.Directory.Should().BeNull();
        }

        platform.IsUnavailable = false;
    }

    /// <summary>Sans plateforme configurée (le défaut) : liste vide, relevé refusé, annuaire non consulté.</summary>
    public static async Task NotConfiguredAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
        (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();

        ReceivedInvoicesResponse list = Succeeded(await queries.QueryAsync(new GetReceivedInvoicesQuery(dossier), default));
        list.PlatformConfigured.Should().BeFalse();
        list.Directory.Should().BeNull();
        list.Invoices.Should().BeEmpty();
        (await commands.SendAsync(new FetchReceivedInvoicesCommand(dossier), default)).Errors.Select(e => e.Code)
            .Should().Equal(ErrorCodes.Platform.NotConfigured);
        Succeeded(await queries.QueryAsync(new LookupDirectoryQuery(dossier, "732829320"), default))
            .Should().Be(new DirectoryLookupResponse("732829320", null, Consulted: false));
    }

    private static Task<Result<ReceivedInvoiceResponse>> RespondAsync(
        ICommandDispatcher commands, DossierId dossier, Guid id, PlatformStatusDto status, string? reason = null, decimal? amount = null) =>
        commands.SendAsync(new RespondToReceivedInvoiceCommand(dossier, id, new RespondToReceivedInvoiceRequest { Status = status, Reason = reason, Amount = amount }), default);

    private static T Succeeded<T>(Result<T> result)
    {
        result.IsSuccess.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Code)));
        return result.Value;
    }

    private static IEnumerable<string> Codes<T>(Result<T> result) => result.Errors.Select(e => e.Code);
}
