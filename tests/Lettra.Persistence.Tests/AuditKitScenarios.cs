using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Serialization;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.AuditKit;
using Lettra.Exports.Application.Banking;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Kit contrôle fiscal sur les trois bases, à partir de pièces passées par le vrai parcours : deux factures validées,
/// l'une transmise à la plateforme simulée, l'autre encaissée. Le kit doit contenir un FEC valide, une chaîne intacte,
/// le XML de chaque pièce, la piste d'audit et des empreintes justes. Données fictives.
/// </summary>
internal static class AuditKitScenarios
{
    private static readonly Customer.Draft Durand = new(
        CustomerKind.Company, "Atelier Durand", "732829320", "73282932000074", "FR44732829320", "compta@durand.example",
        "3 rue des Lilas", null, "69001", "Lyon", "FR");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Fictif", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: true, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: "FR7630006000011234567890189", Bic: "AGRIFRPP", VatDeclarationRegime: VatDeclarationRegime.RealNormal);

    public static async Task KitAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly from = new(today.Year, 1, 1), to = new(today.Year, 12, 31);

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
            Guid durand = (await commands.SendAsync(new CreateCustomerCommand(dossier, Durand), default)).Value.Value;
            InvoiceId transmitted = await ValidatedAsync(commands, dossier, durand, 300m);
            InvoiceId paid = await ValidatedAsync(commands, dossier, durand, 120m);

            Result<PlatformTransmissionResponse> sent = await commands.SendAsync(new TransmitInvoiceCommand(dossier, transmitted), default);
            sent.IsSuccess.Should().BeTrue(string.Join(", ", sent.Errors.Select(e => e.Code)));
            Result<PaymentResponse> payment = await commands.SendAsync(new RecordPaymentCommand(dossier, new RecordPaymentRequest
            {
                CustomerId = durand,
                Date = today,
                Amount = 144m,
                Allocations = [new PaymentAllocationRequest(paid.Value, 144m)],
            }), default);
            payment.IsSuccess.Should().BeTrue(string.Join(", ", payment.Errors.Select(e => e.Code)));
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            (await queries.QueryAsync(new GetAuditKitQuery(dossier, to, from), default)).Errors.Select(e => e.Code)
                .Should().Equal(ErrorCodes.Fec.PeriodInvalid);

            Result<AuditKitFile> kit = await queries.QueryAsync(new GetAuditKitQuery(dossier, from, to), default);
            kit.IsSuccess.Should().BeTrue(string.Join(", ", kit.Errors.Select(e => e.Code)));
            kit.Value.FileName.Should().Be($"kit-controle-fiscal-552100554-{today.Year}0101-{today.Year}1231.zip");

            AuditKitManifestResponse manifest = kit.Value.Manifest;
            manifest.Fec.IsValid.Should().BeTrue();
            manifest.Fec.TotalDebit.Should().Be(manifest.Fec.TotalCredit);
            manifest.Integrity.Intact.Should().BeTrue();
            manifest.Integrity.KeyIds.Should().NotBeEmpty();
            manifest.Pieces.Should().HaveCount(2);
            manifest.PaymentCount.Should().Be(1);
            manifest.TransmissionCount.Should().Be(1);

            Dictionary<string, byte[]> files = Unzip(kit.Value.Content);
            files.Keys.First().Should().Be(AuditKitLayout.Manifest, "le manifeste est en tête de l'archive");
            JsonSerializer.Deserialize(files[AuditKitLayout.Manifest], ContractsJsonContext.Default.AuditKitManifestResponse)!.Pieces
                .Should().BeEquivalentTo(manifest.Pieces);
            manifest.Files.Select(f => f.Path).Should().BeEquivalentTo(files.Keys.Where(k => k != AuditKitLayout.Manifest));
            foreach (AuditKitFileResponse file in manifest.Files)
            {
                Convert.ToHexStringLower(SHA256.HashData(files[file.Path])).Should().Be(file.Sha256, file.Path);
            }

            files.Should().ContainKey("fec/552100554FEC" + to.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + ".txt");
            foreach (AuditKitPieceResponse piece in manifest.Pieces)
            {
                Encoding.UTF8.GetString(files[piece.XmlFile]).Should().Contain(piece.Number);
            }

            string pieces = Encoding.UTF8.GetString(files[AuditKitLayout.TrailPieces]);
            pieces.Should().StartWith("﻿numero;nature;date;client");
            pieces.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(3);
            pieces.Should().Contain("Atelier Durand").And.Contain("Déposée").And.Contain("Non transmise");
            Encoding.UTF8.GetString(files[AuditKitLayout.Transmissions]).Should().Contain(SimulatedAccreditedPlatform.PlatformName);
            Encoding.UTF8.GetString(files[AuditKitLayout.TrailPayments]).Should().Contain("144,00").And.Contain("Comptabilisé");
            Encoding.UTF8.GetString(files[AuditKitLayout.TrailJournal]).Should()
                .Contain("Facture validée").And.Contain("Dépôt à la plateforme").And.Contain("Encaissement enregistré");
            Encoding.UTF8.GetString(files[AuditKitLayout.ReadMe]).Should().Contain("Conseil Fictif (SIREN 552100554)").And.Contain("intacte");
            Encoding.UTF8.GetString(files[AuditKitLayout.Attestation]).Should().Contain("\"intact\": true");
        }
    }

    private static Dictionary<string, byte[]> Unzip(byte[] archive)
    {
        using ZipArchive zip = new(new MemoryStream(archive), ZipArchiveMode.Read);
        Dictionary<string, byte[]> files = [];
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            using Stream stream = entry.Open();
            using MemoryStream copy = new();
            stream.CopyTo(copy);
            files[entry.FullName] = copy.ToArray();
        }

        return files;
    }

    private static async Task<InvoiceId> ValidatedAsync(ICommandDispatcher commands, DossierId dossier, Guid customer, decimal price)
    {
        SaveInvoiceRequest request = new()
        {
            CustomerId = customer,
            Lines = [new DocumentLineRequest { Designation = "Conseil", Quantity = 1, UnitPriceExclTax = price, VatRateCode = "FR-20", Kind = CatalogItemKindDto.Service, UnitCode = "HUR" }],
        };
        Result<InvoiceId> created = await commands.SendAsync(new CreateInvoiceCommand(dossier, request), default);
        created.IsSuccess.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Code)));
        Result validated = await commands.SendAsync(new ValidateInvoiceCommand(dossier, created.Value, null), default);
        validated.IsSuccess.Should().BeTrue(string.Join(", ", validated.Errors.Select(e => e.Code)));
        return created.Value;
    }
}
