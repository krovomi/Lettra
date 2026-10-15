using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Exports;
using Lettra.Exports.Application.EInvoicing;
using Lettra.Exports.Application.EInvoicing.Platform;

namespace Lettra.Exports.Tests.Platform;

/// <summary>Une facture fictive à une ligne (300 € HT, TVA 20 %) et ses fichiers CII et UBL, écrits par les vrais sérialiseurs.</summary>
internal static class PlatformSamples
{
    public const string SellerSiren = "552100554";
    public const string BuyerSiren = "732829320";

    private static readonly PartySnapshotResponse Seller = new(
        "Conseil Fictif", "10 avenue de l'Opéra", null, "75001", "Paris", "FR", SellerSiren, "55210055400013", "FR96552100554", "SAS", 10_000m, "RCS Paris 552 100 554");

    private static readonly PartySnapshotResponse Buyer = new(
        "Atelier Durand", "3 rue des Lilas", null, "69001", "Lyon", "FR", BuyerSiren, "73282932000074", "FR44732829320", "SARL", null, null);

    public static InvoiceResponse Invoice(string number, InvoiceKindDto kind = InvoiceKindDto.Invoice) => new(
        Guid.CreateVersion7(), 2, kind, InvoiceStatusDto.Validated, number, "FAC", Guid.CreateVersion7(), Buyer, Seller,
        new DateOnly(2026, 10, 15), null, kind == InvoiceKindDto.CreditNote ? null : new DateOnly(2026, 11, 14), "Virement à 30 jours", null,
        VatRegimeDto.Standard, OperationCategoryDto.Services, null, "Mission de conseil", null, null, null, null,
        kind == InvoiceKindDto.CreditNote ? "FAC-000001" : null, null,
        [new DocumentLineResponse(1, null, "CONS-H", "Conseil", null, CatalogItemKindDto.Service, "HUR", 3m, 100m, 0m, "FR-20", 20m, VatCategoryDto.Standard, null, 300m)],
        new DocumentTotalsResponse([new VatBreakdownResponse(VatCategoryDto.Standard, 20m, 300m, 60m, null)], 300m, 60m, 360m),
        [], 0m, null, Guid.Empty, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null);

    public static PlatformInvoiceSubmission Submission(string number, EInvoiceFormatDto format = EInvoiceFormatDto.Cii, string buyerSiren = BuyerSiren) => new(
        SellerSiren, buyerSiren, number, format, EInvoiceSerializer.FileNameOf(number, format), EInvoiceSerializer.Write(Invoice(number), null, format));

    public static PlatformTransactionReport Transaction(string number) => new(
        SellerSiren, number, new DateOnly(2026, 10, 15), false, PlatformTransactionKind.Consumer, "FR", "EUR",
        [new PlatformVatAmount("S", 20m, 300m, 60m)], 300m, 60m, 360m);

    /// <summary>Un numéro neuf à chaque appel : une vraie plateforme rejette les doublons.</summary>
    public static string NewNumber() => $"FAC-T{Guid.NewGuid():N}"[..20].ToUpperInvariant();
}
