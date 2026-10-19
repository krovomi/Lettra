using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Calculations;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application;
using Lettra.Exports.Application.Fec;
using Lettra.Exports.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lettra.Persistence.Tests;

/// <summary>FEC (module Exports) depuis les écritures enregistrées à la validation : mêmes scénarios sur les trois bases.</summary>
internal static class ExportScenarios
{
    private static readonly Customer.Draft Durand = new(
        CustomerKind.Company, "Atelier Durand", "732829320", "73282932000074", "FR44732829320", "compta@durand.example",
        "3 rue des Lilas", null, "69001", "Lyon", "FR");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Fictif", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: false, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: "FR7630006000011234567890189", Bic: "AGRIFRPP");

    /// <summary>
    /// Une facture à deux taux puis son avoir total : le FEC porte deux écritures équilibrées, numérotées 1 et 2 dans
    /// l'ordre de validation, passe le validateur et reprend exactement les écritures enregistrées.
    /// </summary>
    public static async Task FecOfValidatedInvoicesAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        await SeedAsync(harness, dossier, withProfile: true);

        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
        DateOnly today = LegalCalendar.DateOf(DateTimeOffset.UtcNow);
        DateOnly from = new(today.Year, 1, 1);
        DateOnly to = new(today.Year, 12, 31);

        Result<FecFile> exported = await queries.QueryAsync(new ExportFecQuery(dossier, from, to), default);
        exported.IsSuccess.Should().BeTrue(string.Join(", ", exported.Errors.Select(e => e.Code)));
        FecFile fec = exported.Value;
        fec.FileName.Should().Be($"552100554FEC{to:yyyyMMdd}.txt");
        fec.Validation.IsValid.Should().BeTrue();
        fec.Validation.EntryCount.Should().Be(2);
        fec.Validation.TotalDebit.Should().Be(fec.Validation.TotalCredit);

        string[] lines = FecFormat.Encoding.GetString(fec.Content).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be(string.Join('\t', FecFormat.Columns));
        string[][] rows = [.. lines.Skip(1).Select(l => l.Split('\t'))];
        rows.Should().OnlyContain(r => r.Length == 18 && r[0] == "VE");
        rows.Where(r => r[2] == "1").Should().OnlyContain(r => r[8] == "FAC-000001" && r[10].StartsWith("Facture FAC-000001", StringComparison.Ordinal));
        rows.Where(r => r[2] == "2").Should().OnlyContain(r => r[8] == "FAC-000002" && r[10].StartsWith("Avoir FAC-000002", StringComparison.Ordinal));
        string[] customer = rows.Single(r => r[2] == "1" && r[4] == SalesAccounts.Customers);
        customer[6].Should().StartWith("C").And.HaveLength(11);
        customer[7].Should().Be("Atelier Durand");
        customer[11].Should().Be("381,10");
        // TVA sur encaissements (vague 6) : celle de la prestation attend l'encaissement en 44574, celle des marchandises est due.
        rows.Where(r => r[4] == SalesAccounts.VatCollected && r[2] == "1").Select(r => r[12]).Should().BeEquivalentTo(["1,10"]);
        rows.Where(r => r[4] == SalesAccounts.VatPending && r[2] == "1").Select(r => r[12]).Should().BeEquivalentTo(["60,00"]);
        rows.Single(r => r[2] == "2" && r[4] == SalesAccounts.Customers)[12].Should().Be("381,10");

        // Le validateur intégré relit le même fichier soumis par l'utilisateur.
        Result<Contracts.Exports.FecValidationResponse> revalidated = await queries.QueryAsync(
            new ValidateFecQuery(dossier, fec.FileName, fec.Content, from, to), default);
        revalidated.Value.IsValid.Should().BeTrue();
        revalidated.Value.Findings.Should().BeEmpty();

        // Une seule ligne modifiée (le débit client de la facture) déséquilibre l'écriture et le fichier.
        string original = FecFormat.Encoding.GetString(fec.Content);
        int at = original.IndexOf("381,10", StringComparison.Ordinal);
        byte[] tampered = FecFormat.Encoding.GetBytes(string.Concat(original.AsSpan(0, at), "381,20", original.AsSpan(at + 6)));
        Result<Contracts.Exports.FecValidationResponse> refused = await queries.QueryAsync(new ValidateFecQuery(dossier, fec.FileName, tampered), default);
        refused.Value.IsValid.Should().BeFalse();
        refused.Value.Findings.Select(f => (f.Code, f.EntryNumber)).Should().Equal((ErrorCodes.Fec.EntryUnbalanced, "1"), (ErrorCodes.Fec.FileUnbalanced, null));

        // Hors de l'exercice, rien n'est exporté (le fichier garde son en-tête).
        FecFile empty = (await queries.QueryAsync(new ExportFecQuery(dossier, from.AddYears(-2), to.AddYears(-2)), default)).Value;
        empty.Validation.EntryCount.Should().Be(0);
        empty.Validation.Findings.Select(f => f.Code).Should().Equal(ErrorCodes.Fec.NoEntries);
    }

    /// <summary>
    /// L'export en masse du Worker : un dossier exporté, un dossier sans fiche entreprise refusé avec son code, puis une
    /// reprise qui ne refait pas le fichier déjà déposé.
    /// </summary>
    public static async Task BulkExportAsync(TenantHarness harness, DatabaseProvider provider, string firstConnection, string secondConnection)
    {
        DossierId ready = harness.AddDossier(provider, firstConnection);
        DossierId incomplete = harness.AddDossier(provider, secondConnection);
        await SeedAsync(harness, ready, withProfile: true);
        await SeedAsync(harness, incomplete, withProfile: false);

        DateOnly today = LegalCalendar.DateOf(DateTimeOffset.UtcNow);
        DateOnly from = new(today.Year, 1, 1);
        DateOnly to = new(today.Year, 12, 31);
        MemoryExportArchive archive = new();
        TenantDescriptor[] dossiers = [harness.Descriptor(ready), harness.Descriptor(incomplete)];
        FecBulkExportJob job = new(
            harness.Services.GetRequiredService<IServiceScopeFactory>(),
            harness.Services,
            new ActiveDossierSource((_, _) => Task.FromResult<IReadOnlyList<TenantDescriptor>>(dossiers)),
            archive,
            TimeProvider.System,
            Options.Create(new FecBulkExportOptions()),
            NullLogger<FecBulkExportJob>.Instance);

        FecBulkExportReport first = (await job.RunAsync(from, to, default))!;
        first.Dossiers.Single(d => d.DossierId == ready.Value).Status.Should().Be(FecBulkExportStatus.Exported);
        first.Dossiers.Single(d => d.DossierId == ready.Value).EntryCount.Should().Be(2);
        FecBulkExportLine refused = first.Dossiers.Single(d => d.DossierId == incomplete.Value);
        refused.Status.Should().Be(FecBulkExportStatus.Refused);
        refused.Code.Should().Be(CompanyProfileErrors.NotFoundCode);
        string batch = $"FEC-{to:yyyyMMdd}";
        archive.Files.Keys.Should().Equal($"{batch}/{ready.Value:D}/552100554FEC{to:yyyyMMdd}.txt");
        archive.Reports.Should().ContainKey(batch);

        FecBulkExportReport second = (await job.RunAsync(from, to, default))!;
        second.Dossiers.Single(d => d.DossierId == ready.Value).Status.Should().Be(FecBulkExportStatus.AlreadyExported);
        archive.Writes.Should().Be(1);
    }

    private static DocumentLineRequest Line(string designation, decimal quantity, decimal price, string vat, CatalogItemKindDto kind) =>
        new() { Designation = designation, Quantity = quantity, UnitPriceExclTax = price, VatRateCode = vat, Kind = kind, UnitCode = kind == CatalogItemKindDto.Service ? "HUR" : "C62" };

    /// <summary>Facture (300 € HT de services à 20 %, 20 € HT de livres à 5,5 %) validée, puis son avoir total validé.</summary>
    private static async Task SeedAsync(TenantHarness harness, DossierId dossier, bool withProfile)
    {
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        Guid customer = (await commands.SendAsync(new CreateCustomerCommand(dossier, Durand), default)).Value.Value;
        if (!withProfile)
        {
            return;
        }

        (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
        SaveInvoiceRequest request = new()
        {
            CustomerId = customer,
            Lines = [Line("Conseil", 3, 100m, "FR-20", CatalogItemKindDto.Service), Line("Guide « TVA »", 1, 20m, "FR-5.5", CatalogItemKindDto.Good)],
        };
        InvoiceId invoice = (await commands.SendAsync(new CreateInvoiceCommand(dossier, request), default)).Value;
        Result validated = await commands.SendAsync(new ValidateInvoiceCommand(dossier, invoice, null), default);
        validated.IsSuccess.Should().BeTrue(string.Join(", ", validated.Errors.Select(e => e.Code)));
        InvoiceId credit = (await commands.SendAsync(new CreateCreditNoteCommand(dossier, invoice, new CreateCreditNoteRequest { Reason = "Annulation" }), default)).Value;
        (await commands.SendAsync(new ValidateInvoiceCommand(dossier, credit, null), default)).IsSuccess.Should().BeTrue();
    }

    private sealed class MemoryExportArchive : IExportArchive
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public Dictionary<string, byte[]> Reports { get; } = [];

        public int Writes { get; private set; }

        public Task<bool> ExistsAsync(string batch, DossierId dossier, string fileName, CancellationToken cancellationToken) =>
            Task.FromResult(Files.ContainsKey($"{batch}/{dossier.Value:D}/{fileName}"));

        public Task WriteAsync(string batch, DossierId dossier, string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            Writes++;
            Files[$"{batch}/{dossier.Value:D}/{fileName}"] = content.ToArray();
            return Task.CompletedTask;
        }

        public Task WriteReportAsync(string batch, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            Reports[batch] = content.ToArray();
            return Task.CompletedTask;
        }
    }
}
