using Lettra.Calculations;
using Lettra.Domain.Billing;

namespace Lettra.Exports.Application.Tax;

/// <summary>
/// Découpe une pièce validée en opérations déclarables, sans rien recalculer de ce qui est imprimé : la TVA de chaque
/// catégorie et taux est celle du récapitulatif de la pièce. Quand un même taux mêle biens et services, la TVA des
/// services est leur base au taux, arrondie au centime, et le reste va aux biens : la somme redonne toujours la pièce.
/// </summary>
public static class VatOperations
{
    /// <summary>Débours (art. 267 II-2° du CGI) : hors base d'imposition, ils ne vont dans aucune case.</summary>
    public const string DisbursementExemption = "VATEX-EU-79-C";

    public static IReadOnlyList<VatOperation> Of(TaxDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int sign = document.Kind == InvoiceKind.CreditNote ? -1 : 1;
        List<VatOperation> operations = [];
        foreach (IGrouping<(string Category, decimal Rate), TaxLine> group in document.Lines
            .GroupBy(l => (l.Category, Rate: VatCategoryCodes.IsTaxed(l.Category) ? l.RatePercent : 0m))
            .OrderByDescending(g => g.Key.Rate)
            .ThenBy(g => g.Key.Category, StringComparer.Ordinal))
        {
            decimal groupVat = document.Summaries
                .Where(s => s.Category == group.Key.Category && s.RatePercent == group.Key.Rate)
                .Sum(s => s.VatAmount);
            var parts = group
                .GroupBy(l => (l.Nature, l.ExemptionCode))
                .OrderBy(g => g.Key.Nature == RevenueNature.Goods ? 1 : 0)
                .Select(g => (g.Key.Nature, g.Key.ExemptionCode, Base: g.Sum(l => l.TotalExclTax)))
                .ToList();

            decimal allocated = 0m;
            for (int i = 0; i < parts.Count; i++)
            {
                (RevenueNature nature, string? exemption, decimal baseExclTax) = parts[i];
                decimal vat = i == parts.Count - 1 ? groupVat - allocated : Rounding.ToCents(baseExclTax * group.Key.Rate / 100m);
                allocated += vat;
                operations.Add(new VatOperation(
                    document.InvoiceId,
                    document.Kind,
                    document.Number,
                    document.Date,
                    document.CustomerId,
                    document.Customer.Name,
                    document.Customer.CountryCode,
                    document.Customer.VatNumber,
                    document.Regime,
                    group.Key.Category,
                    group.Key.Rate,
                    exemption,
                    nature,
                    sign * baseExclTax,
                    sign * vat,
                    DueOnReceipt: !document.VatOnDebits && nature == RevenueNature.Services && VatCategoryCodes.IsTaxed(group.Key.Category)));
            }
        }

        return operations;
    }

    /// <summary>
    /// TVA sur les encaissements : retire des opérations de la période la TVA des services due à l'encaissement et la
    /// remplace par les encaissements de la période. Sans suivi des encaissements, les opérations restent telles quelles.
    /// <paramref name="deferred"/> limite le retrait aux pièces dont la TVA est réellement passée en attente (44574) ;
    /// sans lui, toutes les opérations dues à l'encaissement sont retirées.
    /// </summary>
    public static IReadOnlyList<VatOperation> WithReceipts(
        IReadOnlyList<VatOperation> operations,
        IReadOnlyList<VatReceiptAllocation> receipts,
        IReadOnlyDictionary<Guid, TaxDocument> documents,
        IReadOnlySet<Guid>? deferred = null)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(receipts);
        ArgumentNullException.ThrowIfNull(documents);
        List<VatOperation> kept = [.. operations.Where(o => !o.DueOnReceipt || (deferred is not null && !deferred.Contains(o.InvoiceId)))];
        foreach (VatReceiptAllocation receipt in receipts)
        {
            PartySnapshot? customer = documents.TryGetValue(receipt.InvoiceId, out TaxDocument? document) ? document.Customer : null;
            kept.Add(new VatOperation(
                receipt.InvoiceId,
                receipt.Kind,
                receipt.PieceNumber,
                receipt.ReceiptDate,
                receipt.CustomerId,
                receipt.CustomerName,
                customer?.CountryCode ?? "FR",
                customer?.VatNumber,
                document?.Regime ?? VatRegime.Standard,
                receipt.VatRatePercent == 0m ? VatCategoryCodes.ZeroRated : VatCategoryCodes.Standard,
                receipt.VatRatePercent,
                null,
                RevenueNature.Services,
                receipt.BaseExclTax,
                receipt.VatAmount,
                DueOnReceipt: true));
        }

        return kept;
    }
}
