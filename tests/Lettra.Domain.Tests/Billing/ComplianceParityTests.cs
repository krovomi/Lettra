using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lettra.Calculations.Compliance;

namespace Lettra.Domain.Tests.Billing;

/// <summary>
/// Parité du contrôle des mentions obligatoires avec <c>validate-facture.js</c> (Paperasse f2e15c4), sur les 1 006
/// factures du banc du POC. Les mentions comparées doivent donner le même verdict (erreur, avertissement, présente,
/// sans objet) ; les mentions dont la règle s'écarte volontairement de la référence sont listées à part et suivies par
/// <see cref="ComplianceChecker.DeviationsFromReference"/>.
/// </summary>
public sealed partial class ComplianceParityTests
{
    private const string Reference = "romainsimon/paperasse@f2e15c4";

    /// <summary>Mentions dont la règle diffère volontairement de la référence (voir DeviationsFromReference).</summary>
    private static readonly HashSet<string> Deviating =
    [
        MentionCodes.SellerRcs,
        MentionCodes.SellerLegalForm,
        MentionCodes.DeliveryDate,
        MentionCodes.DeliveryAddress,
        MentionCodes.VatOnDebits,
        MentionCodes.RecoveryIndemnity,
        MentionCodes.VatExemptionText,
        MentionCodes.Totals,
    ];

    [Fact]
    public void Le_controle_des_mentions_donne_le_meme_verdict_que_la_reference_hors_ecarts_documentes()
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "parity", "validate-facture.json")))!;
        root["reference"]!.GetValue<string>().Should().Be(Reference);
        JsonArray cases = root["cases"]!.AsArray();
        List<string> mismatches = [];
        int compared = 0;

        foreach (JsonNode? c in cases)
        {
            DateOnly today = DateOnly.Parse(c!["today"]!.GetValue<string>(), CultureInfo.InvariantCulture);
            if (c["strict"]!.GetValue<bool>() && today < MandatoryMentions.Reform2026)
            {
                // Le mode strict de la référence applique la réforme par avance.
                today = MandatoryMentions.Reform2026;
            }

            ComplianceReport report = ComplianceChecker.Check(ToDocument(c["company"]!, c["invoice"]!), today);
            Dictionary<string, string> expected = ExpectedVerdicts(c["expected"]!);
            Dictionary<string, string> actual = report.Findings
                .Where(f => !Deviating.Contains(f.Code))
                // La référence ne signale les pénalités que lorsqu'elles manquent.
                .Where(f => !(f.Code == MentionCodes.LatePenalties && f.Severity == ComplianceSeverity.Ok))
                .ToDictionary(f => Key(f.Code, f.Line), f => f.Severity.ToString());

            bool franchise = c["company"]!["tax"]?["regime_tva"]?.ToString() == "franchise";

            // Écart documenté : une quantité nulle est refusée par Lettra, acceptée par la référence.
            bool zeroQuantity = c["invoice"]!["lines"]?.AsArray().Any(l => Number(l?["quantity"]) == 0m) == true;
            foreach (string key in expected.Keys.Union(actual.Keys)
                .Where(k => !(franchise && k == MentionCodes.SellerVatNumber))
                .Where(k => !(zeroQuantity && (k == MentionCodes.Lines || k.StartsWith(MentionCodes.LineQuantity, StringComparison.Ordinal)))))
            {
                string want = expected.GetValueOrDefault(key, "Absent");
                string got = actual.GetValueOrDefault(key, "Absent");
                compared++;
                if (want != got)
                {
                    mismatches.Add($"{c["name"]} · {key} : référence {want}, Lettra {got}");
                }
            }
        }

        cases.Count.Should().BeGreaterThan(1000);
        compared.Should().BeGreaterThan(10_000);
        mismatches.Should().BeEmpty(string.Join("\n", mismatches.GroupBy(m => m.Split(" · ")[1]).Select(g => $"{g.Count()} × {g.Key}")));
    }

    [Fact]
    public void Les_ecarts_avec_la_reference_sont_documentes()
    {
        ComplianceChecker.DeviationsFromReference.Should().HaveCountGreaterThanOrEqualTo(Deviating.Count - 2);
    }

    private static string Key(string code, int? line) => line is null ? code : $"{code}#{line}";

    private static Dictionary<string, string> ExpectedVerdicts(JsonNode expected)
    {
        Dictionary<string, string> verdicts = [];
        foreach ((string list, string severity) in new[] { ("ok", "Ok"), ("warnings", "Warning"), ("errors", "Error") })
        {
            foreach (JsonNode? finding in expected[list]!.AsArray())
            {
                if (ToKey(finding!["id"]!.GetValue<string>()) is { } key && !Deviating.Contains(key.Split('#')[0]))
                {
                    verdicts[key] = severity;
                }
            }
        }

        return verdicts;
    }

    /// <summary>Identifiant Paperasse → code Lettra (et rang de ligne pour <c>line_N_*</c>).</summary>
    private static string? ToKey(string id)
    {
        Match line = LineFinding().Match(id);
        if (line.Success)
        {
            int rank = int.Parse(line.Groups[1].Value, CultureInfo.InvariantCulture) + 1;
            string code = line.Groups[2].Value switch
            {
                "desc" => MentionCodes.LineDesignation,
                "qty" => MentionCodes.LineQuantity,
                _ => MentionCodes.LineUnitPrice,
            };
            return Key(code, rank);
        }

        return id switch
        {
            "lines" => MentionCodes.Lines,
            "montants" => MentionCodes.Totals,
            "paiement" or "date_echeance" => MentionCodes.DueDate,
            "penalites" => MentionCodes.LatePenalties,
            "escompte" => MentionCodes.EarlyPaymentDiscount,
            "mention_franchise" => MentionCodes.VatExemptionText,
            _ => MandatoryMentions.All.FirstOrDefault(m => m.PaperasseId == id)?.Code,
        };
    }

    private static ComplianceDocument ToDocument(JsonNode company, JsonNode invoice)
    {
        JsonNode? client = invoice["client"];
        string? clientName = Text(client?["name"]);
        bool isBusiness = Text(client?["siren"]) is not null || (clientName is not null && BusinessFormInName().IsMatch(clientName));
        JsonArray lines = invoice["lines"]?.AsArray() ?? [];
        return new ComplianceDocument(
            CompliancePieceKind.Invoice,
            IsDraft: false,
            new ComplianceParty(
                Text(company["name"]), Text(company["address"]), Text(company["siren"]), Text(company["siret"]), Text(company["tva_intracom"]), "FR",
                Text(company["legal_form"]), Number(company["capital"]), Text(company["rcs"])),
            new ComplianceParty(
                clientName, Text(client?["address"]), Text(client?["siren"]), Text(client?["siret"]), Text(client?["tva_intracom"]),
                Text(client?["country"]), null, null, null, isBusiness),
            SellerVatFranchise: Text(company["tax"]?["regime_tva"]) == "franchise",
            SellerVatOnDebits: company["tax"]?["option_debits"]?.ToString() == "true",
            Number: Text(invoice["number"]),
            IssueDate: Date(invoice["date"]),
            DeliveryDate: Date(invoice["delivery_date"]),
            DeliveryAddress: Text(invoice["delivery_address"]),
            OperationCategory: Text(invoice["category"]),
            DueDate: Date(invoice["due_date"]),
            PaymentTerms: Text(invoice["payment"]?["terms"]),
            LatePenaltyRate: (Text(company["payment"]?["late_penalty_rate"]) ?? Text(invoice["payment"]?["late_penalty_rate"])) is null ? null : 0m,
            EarlyPaymentDiscount: Text(company["payment"]?["escompte_label"]) ?? Text(invoice["payment"]?["escompte_label"]),
            Lines: [.. lines.Select(l => new ComplianceLine(Text(l!["description"]), Number(l["quantity"]), Number(l["unit_price"]), "S"))],
            LegalMentions: []);
    }

    /// <summary>Valeur « vraie » au sens JavaScript : une chaîne vide ou absente ne compte pas.</summary>
    private static string? Text(JsonNode? node) => node?.ToString() is { Length: > 0 } text ? text : null;

    private static decimal? Number(JsonNode? node) =>
        node is null ? null : decimal.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value) ? value : 0m;

    /// <summary>Une date présente mais illisible reste « présente », comme pour la référence qui ne la lit pas.</summary>
    private static DateOnly? Date(JsonNode? node) =>
        Text(node) is not { } text ? null : DateOnly.TryParse(text, CultureInfo.InvariantCulture, out DateOnly date) ? date : DateOnly.MinValue;

    [GeneratedRegex(@"\b(SAS|SASU|SARL|EURL|SA|SCI|GmbH|Ltd|LLC|Inc|Corp|BV|AB|AG|SpA)\b", RegexOptions.IgnoreCase | RegexOptions.ECMAScript)]
    private static partial Regex BusinessFormInName();

    [GeneratedRegex(@"^line_(\d+)_(desc|qty|pu)$")]
    private static partial Regex LineFinding();
}
