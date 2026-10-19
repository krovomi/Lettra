using Lettra.Calculations;
using Lettra.Domain.Billing;
using Lettra.Exports.Application.Tax;

namespace Lettra.Exports.Tests.Tax;

/// <summary>Pièces fictives pour les brouillons de TVA et de DES : une ligne par catégorie et par taux.</summary>
internal static class TaxSamples
{
    public static readonly Guid Durand = Guid.Parse("0199a000-0000-7000-8000-000000000001");
    public static readonly Guid Berlin = Guid.Parse("0199a000-0000-7000-8000-000000000002");

    public static PartySnapshot Customer(string name, string country = "FR", string? vatNumber = null, string? siren = "552100554") =>
        new(name, "1 rue de la Paix", null, "75002", "Paris", country, siren, null, vatNumber, null, null, null, IsBusiness: true);

    public static TaxDocument Invoice(
        string number,
        DateOnly date,
        IReadOnlyList<TaxLine> lines,
        PartySnapshot? customer = null,
        VatRegime regime = VatRegime.Standard,
        InvoiceKind kind = InvoiceKind.Invoice,
        bool vatOnDebits = true,
        bool transmitted = true,
        Guid? customerId = null)
    {
        List<VatSummary> summaries = [.. lines
            .GroupBy(l => (l.Category, Rate: VatCategoryCodes.IsTaxed(l.Category) ? l.RatePercent : 0m))
            .Select(g =>
            {
                decimal baseExclTax = g.Sum(l => l.TotalExclTax);
                return new VatSummary(g.Key.Category, g.Key.Rate, baseExclTax, Rounding.ToCents(baseExclTax * g.Key.Rate / 100m), g.First().ExemptionCode);
            })];
        return new TaxDocument(
            Guid.CreateVersion7(), kind, number, date, customerId ?? Durand, customer ?? Customer("Atelier Durand"), regime, vatOnDebits, lines, summaries, transmitted);
    }

    public static TaxLine Services(decimal amount, decimal rate = 20m, string category = VatCategoryCodes.Standard, string? exemption = null) =>
        new(RevenueNature.Services, category, rate, exemption, amount);

    public static TaxLine Goods(decimal amount, decimal rate = 20m, string category = VatCategoryCodes.Standard, string? exemption = null) =>
        new(RevenueNature.Goods, category, rate, exemption, amount);
}
