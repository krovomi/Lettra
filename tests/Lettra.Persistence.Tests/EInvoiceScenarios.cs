using System.Xml.Linq;
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
using Lettra.Exports.Application.EInvoicing;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Factures électroniques EN 16931 (CII et UBL) de pièces validées par le vrai parcours : facture à deux taux avec remise
/// et ligne exonérée, acompte puis facture finale, avoir, autoliquidation intra-UE, livraison intracommunautaire, export
/// et franchise en base. Quand <c>LETTRA_EINVOICE_SAMPLES</c> est défini, les fichiers y sont écrits pour le contrôle
/// Schematron EN 16931 officiel fait en CI (build/einvoice/validate.sh).
/// </summary>
internal static class EInvoiceScenarios
{
    private static readonly XNamespace Rsm = "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100";
    private static readonly XNamespace UblInvoice = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace UblCreditNote = "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2";

    private static readonly Customer.Draft Durand = new(
        CustomerKind.Company, "Atelier Durand", "732829320", "73282932000074", "FR44732829320", "compta@durand.example",
        "3 rue des Lilas", null, "69001", "Lyon", "FR");

    private static readonly Customer.Draft Berlin = new(
        CustomerKind.Company, "Muster Beratung GmbH", null, null, "DE123456789", "buchhaltung@muster.example",
        "Musterstraße 1", null, "10115", "Berlin", "DE");

    private static readonly Customer.Draft Geneve = new(
        CustomerKind.Company, "Conseil Lac SA", null, null, null, "compta@lac.example",
        "1 quai du Mont-Blanc", null, "1201", "Genève", "CH");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Fictif", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: false, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: "FR7630006000011234567890189", Bic: "AGRIFRPP");

    private static readonly CompanyProfile.Draft MicroEntreprise = Cabinet with
    {
        Name = "Studio Fictif",
        LegalForm = "EI",
        ShareCapital = null,
        Siren = "732829320",
        Siret = "73282932000074",
        Rcs = null,
        VatNumber = null,
        VatFranchise = true,
    };

    public static async Task SamplesAsync(TenantHarness harness, DatabaseProvider provider, string connectionString, string franchiseConnectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        DossierId franchise = harness.AddDossier(provider, franchiseConnectionString);
        List<(string Name, InvoiceId Id, DossierId Dossier)> pieces = [];

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
            Guid durand = (await commands.SendAsync(new CreateCustomerCommand(dossier, Durand), default)).Value.Value;
            Guid berlin = (await commands.SendAsync(new CreateCustomerCommand(dossier, Berlin), default)).Value.Value;
            Guid geneve = (await commands.SendAsync(new CreateCustomerCommand(dossier, Geneve), default)).Value.Value;

            InvoiceId invoice = await ValidatedAsync(commands, dossier, new SaveInvoiceRequest
            {
                CustomerId = durand,
                DeliveryDate = new DateOnly(2026, 10, 1),
                DeliveryAddress = "Entrepôt Durand\n12 rue du Port\n69002 Lyon",
                PurchaseOrderReference = "BC-2026-118",
                Subject = "Mission de conseil d'octobre",
                Lines =
                [
                    Line("Conseil", 3, 100m, "FR-20", CatalogItemKindDto.Service) with { Reference = "CONS-H", Description = "Séances du 1er au 3 octobre", DiscountPercent = 10m },
                    Line("Guide « TVA »", 2, 19.99m, "FR-5.5", CatalogItemKindDto.Good),
                    Line("Formation professionnelle", 1, 450m, "FR-20", CatalogItemKindDto.Service) with { VatExemptionCode = "VATEX-EU-132-1I" },
                    Line("Débours (frais de greffe)", 1, 39.42m, "FR-20", CatalogItemKindDto.Service) with { VatExemptionCode = "VATEX-EU-79-C" },
                ],
            });
            pieces.Add(("facture", invoice, dossier));

            InvoiceId credit = (await commands.SendAsync(new CreateCreditNoteCommand(dossier, invoice, new CreateCreditNoteRequest { Reason = "Erreur de quantité" }), default)).Value;
            (await commands.SendAsync(new ValidateInvoiceCommand(dossier, credit, null), default)).IsSuccess.Should().BeTrue();
            pieces.Add(("avoir", credit, dossier));

            QuoteId quote = (await commands.SendAsync(new CreateQuoteCommand(dossier, new SaveQuoteRequest
            {
                CustomerId = durand,
                Subject = "Refonte du plan comptable",
                Lines = [Line("Audit", 10, 120m, "FR-20", CatalogItemKindDto.Service), Line("Classeurs", 4, 12.5m, "FR-10", CatalogItemKindDto.Good)],
            }), default)).Value;
            (await commands.SendAsync(new ValidateQuoteCommand(dossier, quote, null), default)).IsSuccess.Should().BeTrue();
            (await commands.SendAsync(new SetQuoteOutcomeCommand(dossier, quote, true, null), default)).IsSuccess.Should().BeTrue();
            InvoiceId downPayment = (await commands.SendAsync(new CreateDownPaymentCommand(dossier, quote, new CreateDownPaymentRequest { Percent = 30m }), default)).Value;
            (await commands.SendAsync(new ValidateInvoiceCommand(dossier, downPayment, null), default)).IsSuccess.Should().BeTrue();
            pieces.Add(("acompte", downPayment, dossier));
            InvoiceId final = (await commands.SendAsync(new CreateFinalInvoiceCommand(dossier, quote), default)).Value;
            (await commands.SendAsync(new ValidateInvoiceCommand(dossier, final, null), default)).IsSuccess.Should().BeTrue();
            pieces.Add(("facture-finale", final, dossier));

            pieces.Add(("autoliquidation-intra-ue", await ValidatedAsync(commands, dossier, new SaveInvoiceRequest
            {
                CustomerId = berlin,
                VatRegime = VatRegimeDto.ReverseChargeIntraEu,
                Lines = [Line("Conseil à distance", 5, 150m, "FR-20", CatalogItemKindDto.Service)],
            }), dossier));

            pieces.Add(("livraison-intra-ue", await ValidatedAsync(commands, dossier, new SaveInvoiceRequest
            {
                CustomerId = berlin,
                VatRegime = VatRegimeDto.IntraEuGoodsSupply,
                DeliveryDate = new DateOnly(2026, 10, 5),
                DeliveryAddress = "Muster Beratung GmbH\nMusterstraße 1\n10115 Berlin\nAllemagne",
                Lines = [Line("Classeurs", 40, 12.5m, "FR-20", CatalogItemKindDto.Good)],
            }), dossier));

            pieces.Add(("export", await ValidatedAsync(commands, dossier, new SaveInvoiceRequest
            {
                CustomerId = geneve,
                VatRegime = VatRegimeDto.Export,
                Lines = [Line("Guide « TVA »", 25, 19.99m, "FR-5.5", CatalogItemKindDto.Good)],
            }), dossier));
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(franchise))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            (await commands.SendAsync(new SaveCompanyProfileCommand(franchise, MicroEntreprise, null), default)).IsSuccess.Should().BeTrue();
            Guid durand = (await commands.SendAsync(new CreateCustomerCommand(franchise, Durand), default)).Value.Value;
            pieces.Add(("franchise", await ValidatedAsync(commands, franchise, new SaveInvoiceRequest
            {
                CustomerId = durand,
                VatRegime = VatRegimeDto.Franchise,
                Lines = [Line("Photographies", 1, 800m, "FR-20", CatalogItemKindDto.Service)],
            }), franchise));
        }

        string? directory = Environment.GetEnvironmentVariable("LETTRA_EINVOICE_SAMPLES");
        foreach ((string name, InvoiceId id, DossierId owner) in pieces)
        {
            await using AsyncServiceScope scope = await harness.OpenAsync(owner);
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            InvoiceResponse piece = (await queries.QueryAsync(new GetInvoiceQuery(owner, id), default)).Value;
            foreach (EInvoiceFormatDto format in Enum.GetValues<EInvoiceFormatDto>())
            {
                Result<EInvoiceFile> file = await queries.QueryAsync(new GetEInvoiceQuery(owner, id, format), default);
                file.IsSuccess.Should().BeTrue(string.Join(", ", file.Errors.Select(e => e.Code)));
                file.Value.FileName.Should().Be(EInvoiceSerializer.FileNameOf(piece.Number!, format));
                XDocument xml = XDocument.Load(new MemoryStream(file.Value.Content));
                xml.Root!.Name.Should().Be(format switch
                {
                    EInvoiceFormatDto.Cii => Rsm + "CrossIndustryInvoice",
                    _ => piece.Kind == InvoiceKindDto.CreditNote ? UblCreditNote + "CreditNote" : UblInvoice + "Invoice",
                });
                xml.ToString().Should().Contain(piece.Number!);
                if (directory is not null)
                {
                    Directory.CreateDirectory(Path.Combine(directory, format.ToString().ToLowerInvariant()));
                    await File.WriteAllBytesAsync(Path.Combine(directory, format.ToString().ToLowerInvariant(), $"{name}.xml"), file.Value.Content);
                }
            }
        }

        // Un brouillon n'a pas de facture électronique.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            Guid customer = (await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
                .QueryAsync(new GetInvoiceQuery(dossier, pieces[0].Id), default)).Value.CustomerId;
            InvoiceId draft = (await commands.SendAsync(new CreateInvoiceCommand(dossier, new SaveInvoiceRequest
            {
                CustomerId = customer,
                Lines = [Line("Conseil", 1, 100m, "FR-20", CatalogItemKindDto.Service)],
            }), default)).Value;
            Result<EInvoiceFile> refused = await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
                .QueryAsync(new GetEInvoiceQuery(dossier, draft, EInvoiceFormatDto.Cii), default);
            refused.Errors.Select(e => e.Code).Should().Equal(ErrorCodes.EInvoice.NotValidated);
        }
    }

    private static async Task<InvoiceId> ValidatedAsync(ICommandDispatcher commands, DossierId dossier, SaveInvoiceRequest request)
    {
        Result<InvoiceId> created = await commands.SendAsync(new CreateInvoiceCommand(dossier, request), default);
        created.IsSuccess.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Code)));
        Result validated = await commands.SendAsync(new ValidateInvoiceCommand(dossier, created.Value, null), default);
        validated.IsSuccess.Should().BeTrue(string.Join(", ", validated.Errors.Select(e => e.Code)));
        return created.Value;
    }

    private static DocumentLineRequest Line(string designation, decimal quantity, decimal price, string vat, CatalogItemKindDto kind) =>
        new() { Designation = designation, Quantity = quantity, UnitPriceExclTax = price, VatRateCode = vat, Kind = kind, UnitCode = kind == CatalogItemKindDto.Service ? "HUR" : "C62" };
}
