using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lettra.Poc.Sync.Domain;

/// <summary>Numéro de facture : F-{année}-{série}-{séquence}, par exemple F-2026-P01-000123 (ADR-0007).</summary>
public static class InvoiceNumber
{
    public const string ServerSeries = "SRV";

    public static string Format(int year, string seriesCode, long sequence)
        => string.Create(CultureInfo.InvariantCulture, $"F-{year}-{seriesCode}-{sequence:D6}");
}

/// <summary>
/// Chaîne de hash par série : chaque facture validée scelle la précédente.
/// Le serveur recalcule chaque maillon reçu d'un poste.
/// </summary>
public static class HashChain
{
    public static string Link(string? previousHash, Invoice invoice)
    {
        var sb = new StringBuilder()
            .Append(previousHash ?? "GENESIS").Append('|')
            .Append(invoice.Id).Append('|')
            .Append(invoice.Number).Append('|')
            .Append(invoice.IssueDate?.ToString("O", CultureInfo.InvariantCulture)).Append('|')
            .Append(invoice.CustomerName).Append('|')
            .Append(invoice.TotalExclTax.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(invoice.TotalInclTax.ToString(CultureInfo.InvariantCulture));
        foreach (var l in invoice.Lines)
            sb.Append('|').Append(l.Label).Append(';')
              .Append(l.Quantity.ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append(l.UnitPriceExclTax.ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append(l.VatRate.ToString(CultureInfo.InvariantCulture));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
