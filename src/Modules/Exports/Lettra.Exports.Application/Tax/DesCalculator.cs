using System.Globalization;
using System.Text;
using Lettra.Calculations;
using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;

namespace Lettra.Exports.Application.Tax;

/// <summary>
/// Déclaration européenne de services (art. 289 B du CGI) : les prestations de services rendues à un assujetti établi
/// dans un autre État membre, dont la TVA est due par le preneur (art. 259-1 et 283-2 du CGI). Une ligne par numéro de
/// TVA du client, montant HT du mois arrondi à l'euro. Fonction pure.
/// <para>
/// Dans Lettra, ce sont les lignes de services d'une pièce en autoliquidation intracommunautaire (catégorie AE) et,
/// par prudence, les lignes de services classées en catégorie K, à un client dont le pays est dans l'Union hors France.
/// Le mois retenu est celui de la date de la pièce (prestation achevée ou acompte encaissé : à relire, §13).
/// </para>
/// </summary>
public static class DesCalculator
{
    public const string CheckVatNumberMissing = "DesVatNumberMissing";
    public const string CheckNotEu = "DesCustomerNotEu";
    public const string CheckNegative = "DesNegative";

    /// <summary>États membres de l'Union hors France, en codes ISO 3166 (la Grèce est « EL » dans les numéros de TVA).</summary>
    public static IReadOnlySet<string> OtherMemberStates { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "ES", "FI", "GR", "HR", "HU", "IE", "IT", "LT", "LU", "LV",
        "MT", "NL", "PL", "PT", "RO", "SE", "SI", "SK",
    };

    /// <summary>Vrai si l'opération relève de la DES quel que soit le pays (le pays est contrôlé à part).</summary>
    public static bool IsIntraEuService(VatOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        bool category = (operation.Category == VatCategoryCodes.ReverseCharge && operation.Regime == VatRegime.ReverseChargeIntraEu)
            || operation.Category == VatCategoryCodes.IntraCommunity;
        return category && operation.Nature != RevenueNature.Goods && operation.CustomerCountry != "FR";
    }

    public static DesResponse Build(int year, int month, DateOnly dueDate, IReadOnlyList<VatOperation> operations, DateTimeOffset? filedAt)
    {
        ArgumentNullException.ThrowIfNull(operations);
        List<VatOperation> services = [.. operations.Where(IsIntraEuService)];
        List<TaxCheckResponse> checks = [];

        List<VatOperation> notEu = [.. services.Where(o => !OtherMemberStates.Contains(o.CustomerCountry))];
        if (notEu.Count > 0)
        {
            checks.Add(new TaxCheckResponse(CheckNotEu, TaxCheckSeverityDto.Warning, Count: notEu.Count, Pieces: Pieces(notEu)));
        }

        List<VatOperation> eu = [.. services.Where(o => OtherMemberStates.Contains(o.CustomerCountry))];
        List<VatOperation> missing = [.. eu.Where(o => NormalizeVatNumber(o.CustomerVatNumber) is null)];
        if (missing.Count > 0)
        {
            checks.Add(new TaxCheckResponse(CheckVatNumberMissing, TaxCheckSeverityDto.Blocking, Count: missing.Count, Pieces: Pieces(missing)));
        }

        List<DesLineResponse> lines = eu
            .Where(o => NormalizeVatNumber(o.CustomerVatNumber) is not null)
            .GroupBy(o => NormalizeVatNumber(o.CustomerVatNumber)!, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select((g, index) =>
            {
                VatOperation last = g.OrderBy(o => o.Date).Last();
                return new DesLineResponse(
                    index + 1, g.Key, last.CustomerCountry, last.CustomerId, last.CustomerName,
                    Math.Round(g.Sum(o => o.BaseExclTax), 0, MidpointRounding.AwayFromZero), VatReturnCalculator.Sources(g));
            })
            .ToList();

        List<DesLineResponse> negative = [.. lines.Where(l => l.Amount < 0m)];
        if (negative.Count > 0)
        {
            checks.Add(new TaxCheckResponse(CheckNegative, TaxCheckSeverityDto.Warning, Count: negative.Count,
                Pieces: [.. negative.SelectMany(l => l.Sources).Select(s => s.PieceNumber).Distinct()]));
        }

        if (checks.Count == 0)
        {
            checks.Add(new TaxCheckResponse(CheckVatNumberMissing, TaxCheckSeverityDto.Ok, Count: 0));
        }

        return new DesResponse(year, month, dueDate, lines, lines.Sum(l => l.Amount), checks, filedAt);
    }

    /// <summary>
    /// Fichier CSV de la DES (UTF-8, point-virgule) : numéro de ligne, numéro de TVA du client, montant en euros, période
    /// AAAAMM. Gabarit d'import du service en ligne des douanes à confirmer avant tout dépôt réel.
    /// </summary>
    public static byte[] ToCsv(DesResponse des)
    {
        ArgumentNullException.ThrowIfNull(des);
        StringBuilder csv = new();
        csv.Append("ligne;numero_tva_client;montant_eur;periode\r\n");
        string period = string.Create(CultureInfo.InvariantCulture, $"{des.Year:D4}{des.Month:D2}");
        foreach (DesLineResponse line in des.Lines)
        {
            csv.Append(CultureInfo.InvariantCulture, $"{line.LineNumber};{line.CustomerVatNumber};{line.Amount:0};{period}\r\n");
        }

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    /// <summary>Numéro de TVA sans espaces ni points, en majuscules ; <c>null</c> s'il est vide.</summary>
    public static string? NormalizeVatNumber(string? vatNumber)
    {
        if (string.IsNullOrWhiteSpace(vatNumber))
        {
            return null;
        }

        string clean = new([.. vatNumber.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);
        return clean.Length < 4 ? null : clean;
    }

    private static List<string> Pieces(IEnumerable<VatOperation> operations) => [.. operations.Select(o => o.PieceNumber).Distinct()];
}
