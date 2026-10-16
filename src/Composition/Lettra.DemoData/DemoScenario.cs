using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Suppliers;

namespace Lettra.DemoData;

/// <summary>
/// Le jeu de données d'un dossier, tiré avant d'être chargé : fiches, catalogue, tarifs et l'histoire de chaque pièce,
/// datée. Pur et déterministe (même graine, même scénario) : il ne touche à aucune base, le chargement le rejoue.
/// </summary>
internal sealed record DemoScenario(
    DemoBusiness Business,
    DateTimeOffset StartsAt,
    SaveCompanyProfileRequest Profile,
    IReadOnlyList<DemoCatalogItem> Items,
    IReadOnlyList<DemoCustomer> Customers,
    IReadOnlyList<DemoSupplier> Suppliers,
    IReadOnlyList<DemoPriceList> PriceLists,
    IReadOnlyList<DemoQuoteStory> Quotes,
    IReadOnlyList<DemoInvoiceStory> Invoices,
    IReadOnlyList<int> ArchivedItems,
    DateTimeOffset ArchivedAt);

internal sealed record DemoCatalogItem(SaveCatalogItemRequest Request, DateTimeOffset CreatedAt);

/// <summary>Un client, son poids commercial (certains reviennent souvent) et ses articles habituels.</summary>
internal sealed record DemoCustomer(
    CreateCustomerRequest Request,
    DateTimeOffset CreatedAt,
    DemoPayer Payer,
    IReadOnlyList<int> HabitualItems);

internal sealed record DemoSupplier(CreateSupplierRequest Request, DateTimeOffset CreatedAt);

/// <summary>Une grille tarifaire : une remise sur quelques articles, pour quelques clients.</summary>
internal sealed record DemoPriceList(
    string Name,
    decimal DiscountPercent,
    IReadOnlyList<int> Customers,
    IReadOnlyList<int> Items,
    DateTimeOffset CreatedAt);

/// <summary>Comment un client paie : à l'heure, en retard, en deux fois, ou pas encore.</summary>
public enum DemoPayer
{
    OnTime = 1,
    Late = 2,
    InTwoInstalments = 3,
    Defaulting = 4,
}

internal sealed record DemoLine(int Item, decimal Quantity, decimal DiscountPercent);

/// <summary>
/// L'histoire d'un devis : création, validation, réponse du client, puis facturation (acompte éventuel et facture
/// finale). Chaque étape absente (null) n'a pas encore eu lieu à la date du jour : le devis reste au statut atteint.
/// </summary>
internal sealed record DemoQuoteStory(
    int Customer,
    string Subject,
    IReadOnlyList<DemoLine> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ValidatedAt,
    DemoOutcome? Outcome,
    DemoDownPayment? DownPayment,
    DemoSettledInvoice? FinalInvoice);

internal sealed record DemoOutcome(bool Accepted, DateTimeOffset At);

internal sealed record DemoDownPayment(decimal Percent, DemoSettledInvoice Invoice);

/// <summary>Une facture directe (sans devis) : abonnements et ventes courantes.</summary>
internal sealed record DemoInvoiceStory(
    int Customer,
    string Subject,
    IReadOnlyList<DemoLine> Lines,
    DemoSettledInvoice Invoice);

/// <summary>
/// La vie d'une facture après sa création : validation (numéro FAC), transmission à la plateforme agréée,
/// encaissements, avoir éventuel. Une étape à null n'a pas encore eu lieu.
/// </summary>
internal sealed record DemoSettledInvoice(
    DateTimeOffset CreatedAt,
    DateTimeOffset? ValidatedAt,
    DateTimeOffset? TransmittedAt,
    DemoCredit? Credit,
    IReadOnlyList<DemoPayment> Payments);

/// <summary>Un encaissement : une part du reste dû (1 = tout le reste).</summary>
internal sealed record DemoPayment(DateTimeOffset At, decimal ShareOfRemaining);

/// <summary>Un avoir : total (motif seul) ou partiel (une quantité rendue sur une ligne).</summary>
internal sealed record DemoCredit(DateTimeOffset At, string Reason, int? Line, decimal? Quantity);

internal static class DemoLineMapping
{
    public static DocumentLineRequest ToRequest(this DemoLine line, IReadOnlyList<DemoCatalogItem> items, Guid? catalogItemId)
    {
        SaveCatalogItemRequest item = items[line.Item].Request;
        return new DocumentLineRequest
        {
            CatalogItemId = catalogItemId,
            Reference = item.Reference,
            Designation = item.Designation,
            Description = item.Description,
            Kind = item.Kind,
            UnitCode = item.UnitCode,
            Quantity = line.Quantity,
            UnitPriceExclTax = item.UnitPriceExclTax,
            DiscountPercent = line.DiscountPercent,
            VatRateCode = item.VatRateCode,
        };
    }

    public static decimal TotalExclTax(this DemoLine line, IReadOnlyList<DemoCatalogItem> items) =>
        decimal.Round(line.Quantity * items[line.Item].Request.UnitPriceExclTax * (1m - (line.DiscountPercent / 100m)), 2);

    public static bool IsService(this SaveCatalogItemRequest item) => item.Kind == CatalogItemKindDto.Service;
}

internal static class DemoCustomerKinds
{
    public static bool IsCompany(this DemoCustomer customer) => customer.Request.Kind == CustomerKindDto.Company;
}
