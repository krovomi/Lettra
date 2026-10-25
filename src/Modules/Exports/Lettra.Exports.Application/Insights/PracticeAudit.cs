using System.Globalization;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Insights;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Fec;
using Lettra.Exports.Application.Tax;

namespace Lettra.Exports.Application.Insights;

/// <summary>Le contrôle d'entraînement du FEC que Lettra produit pour l'exercice donné (24 mois au plus).</summary>
public sealed record GetPracticeAuditQuery(DossierId Dossier, DateOnly From, DateOnly To) : IQuery<Result<PracticeAuditResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FecExporter;
}

/// <summary>Le contrôle d'entraînement d'un FEC remis (celui d'un autre logiciel), sur un exercice s'il est donné.</summary>
public sealed record RunPracticeAuditQuery(DossierId Dossier, string FileName, ReadOnlyMemory<byte> Content, DateOnly? From = null, DateOnly? To = null)
    : IQuery<Result<PracticeAuditResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FecExporter;
}

/// <summary>
/// Le contrôleur fiscal d'entraînement : les tests de cohérence qu'un vérificateur passe sur un FEC avec ses outils
/// d'analyse, en règles C# déterministes. Chaque test dit ce qu'il a trouvé, avec des exemples ; aucun ne corrige rien.
/// <list type="bullet">
/// <item>format : le validateur intégré (celui de Test Compta Demat) ;</item>
/// <item>trous de numérotation des écritures (par journal) et des pièces (par série) ;</item>
/// <item>écritures datées d'un dimanche ou d'un jour férié ; validations plus de <see cref="LateValidationDays"/> jours
/// après la date de l'écriture ;</item>
/// <item>caisse (comptes 53) négative en fin de journée ; règlements en espèces au-delà de <see cref="CashLimit"/> € ;</item>
/// <item>montants ronds trop fréquents ; lignes en double ; répartition des premiers chiffres loin de la loi de Benford ;</item>
/// <item>clients créditeurs, fournisseurs débiteurs ; taux de TVA collectée sur le chiffre d'affaires au-delà de 20 %.</item>
/// </list>
/// </summary>
public static class PracticeAudit
{
    public const int LateValidationDays = 60;
    public const decimal CashLimit = 1_000m;
    public const decimal RoundAmountFloor = 1_000m;
    public const decimal RoundAmountShare = 10m;
    public const int RoundAmountMinimum = 3;
    public const int BenfordMinimumAmounts = 200;
    public const decimal BenfordMinimumAmount = 10m;
    public const decimal BenfordNonconformity = 1.5m;
    public const decimal MaximumVatRate = 20.5m;

    /// <summary>L'ordre d'affichage des tests à gravité égale : celui d'un vérificateur, du format aux montants.</summary>
    private static readonly string[] Order =
    [
        "Format", "EntryNumberGap", "PieceNumberGap", "SundayOrHoliday", "LateValidation", "NegativeCash", "CashOverLimit",
        "RoundAmounts", "DuplicateLines", "Benford", "CustomerCredit", "SupplierDebit", "VatRate",
    ];

    public static PracticeAuditResponse Run(string fileName, ReadOnlySpan<byte> content, DateOnly? from = null, DateOnly? to = null)
    {
        FecValidationResponse validation = FecValidator.Validate(fileName, content, from, to);
        IReadOnlyList<Line>? lines = Parse(FecFormat.Decode(content));
        if (lines is null)
        {
            return new PracticeAuditResponse(fileName, from, to, 0, 0, false, [Format(validation)], []);
        }

        Entry[] entries =
        [
            .. lines.GroupBy(l => (l.Journal, l.EntryNumber)).Select(g => new Entry(g.Key.Journal, g.Key.EntryNumber, g.First().EntryDate, g.First().ValidDate, [.. g])),
        ];
        (PracticeAuditCheckResponse benford, IReadOnlyList<BenfordDigitResponse> digits) = Benford(lines);
        List<PracticeAuditCheckResponse> checks =
        [
            Format(validation),
            NumberingGaps("EntryNumberGap", entries.Select(e => (Series: e.Journal, Number: e.Number))),
            NumberingGaps("PieceNumberGap", lines.Where(l => l.Journal == "VE").Select(l => l.PieceRef).Distinct(StringComparer.Ordinal).Select(p => (Series: string.Empty, Number: p))),
            Check("SundayOrHoliday", PracticeAuditSeverityDto.Watch, entries.Where(e => e.Date is { } d && !FrenchCalendar.IsOuvrable(d)).Select(e => $"{e.Journal} {e.Number} · {Day(e.Date!.Value)}")),
            Check("LateValidation", PracticeAuditSeverityDto.Watch, entries.Where(e => e is { Date: { } d, Validated: { } v } && v.DayNumber - d.DayNumber > LateValidationDays).Select(e => $"{e.Journal} {e.Number}")),
            NegativeCash(lines),
            CashOverLimit(lines),
            RoundAmounts(entries),
            DuplicateLines(lines),
            benford,
            Balances("CustomerCredit", lines.Where(l => l.Account.StartsWith("411", StringComparison.Ordinal)), credit: true),
            Balances("SupplierDebit", lines.Where(l => l.Account.StartsWith("401", StringComparison.Ordinal)), credit: false),
            VatRate(lines),
        ];

        return new PracticeAuditResponse(
            fileName,
            from,
            to,
            entries.Length,
            lines.Count,
            true,
            [.. checks.OrderByDescending(c => c.Severity).ThenBy(c => Array.IndexOf(Order, c.Code))],
            digits);
    }

    private static PracticeAuditCheckResponse Format(FecValidationResponse validation) => new(
        "Format",
        !validation.IsValid ? PracticeAuditSeverityDto.Alert : validation.Findings.Count > 0 ? PracticeAuditSeverityDto.Watch : PracticeAuditSeverityDto.Passed,
        validation.Findings.Count,
        null,
        [.. validation.Findings.Select(f => f.Line is int line ? $"{f.Code} · L{line}" : f.Code).Distinct(StringComparer.Ordinal).Take(PracticeAuditResponse.MaxExamples)]);

    private static PracticeAuditCheckResponse Check(string code, PracticeAuditSeverityDto severity, IEnumerable<string> found, decimal? value = null)
    {
        string[] all = [.. found];
        return new PracticeAuditCheckResponse(code, all.Length == 0 ? PracticeAuditSeverityDto.Passed : severity, all.Length, value, [.. all.Take(PracticeAuditResponse.MaxExamples)]);
    }

    /// <summary>Numéros qui manquent dans chaque série : même préfixe, suffixe numérique qui saute.</summary>
    private static PracticeAuditCheckResponse NumberingGaps(string code, IEnumerable<(string Series, string Number)> numbers)
    {
        List<string> gaps = [];
        int missing = 0;
        foreach (IGrouping<(string, string), (string Prefix, long Value, int Width)> series in numbers
            .Select(n => (n.Series, Split: Split(n.Number)))
            .Where(n => n.Split is not null)
            .GroupBy(n => (n.Series, n.Split!.Value.Prefix), n => n.Split!.Value)
            .OrderBy(g => g.Key.Item1, StringComparer.Ordinal).ThenBy(g => g.Key.Item2, StringComparer.Ordinal))
        {
            long[] values = [.. series.Select(s => s.Value).Distinct().Order()];
            for (int i = 1; i < values.Length; i++)
            {
                long hole = values[i] - values[i - 1] - 1;
                if (hole > 0)
                {
                    missing += (int)Math.Min(hole, int.MaxValue - missing);
                    string prefix = (series.Key.Item1.Length > 0 ? series.Key.Item1 + " " : string.Empty) + series.Key.Item2;
                    gaps.Add(hole == 1 ? $"{prefix}{values[i - 1] + 1}" : $"{prefix}{values[i - 1] + 1} → {prefix}{values[i] - 1}");
                }
            }
        }

        return new PracticeAuditCheckResponse(code, missing == 0 ? PracticeAuditSeverityDto.Passed : PracticeAuditSeverityDto.Alert, missing, null, [.. gaps.Take(PracticeAuditResponse.MaxExamples)]);
    }

    private static (string Prefix, long Value, int Width)? Split(string number)
    {
        int start = number.Length;
        while (start > 0 && char.IsAsciiDigit(number[start - 1]))
        {
            start--;
        }

        return start < number.Length && number.Length - start <= 18 && long.TryParse(number.AsSpan(start), out long value)
            ? (number[..start], value, number.Length - start)
            : null;
    }

    /// <summary>Le solde des comptes de caisse, jour après jour : un solde négatif est une caisse qui a payé plus qu'elle n'avait.</summary>
    private static PracticeAuditCheckResponse NegativeCash(IReadOnlyList<Line> lines)
    {
        List<string> days = [];
        decimal lowest = 0m;
        foreach (IGrouping<string, Line> account in lines.Where(l => l.Account.StartsWith("53", StringComparison.Ordinal) && l.EntryDate is not null).GroupBy(l => l.Account).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            decimal balance = 0m;
            foreach (IGrouping<DateOnly, Line> day in account.GroupBy(l => l.EntryDate!.Value).OrderBy(g => g.Key))
            {
                balance += day.Sum(l => l.Debit - l.Credit);
                if (balance < 0m)
                {
                    days.Add($"{account.Key} · {Day(day.Key)} · {Amount(balance)}");
                    lowest = Math.Min(lowest, balance);
                }
            }
        }

        return Check("NegativeCash", PracticeAuditSeverityDto.Alert, days, days.Count == 0 ? null : lowest);
    }

    /// <summary>Les espèces au-delà du plafond légal des règlements (article L112-6 du code monétaire et financier).</summary>
    private static PracticeAuditCheckResponse CashOverLimit(IReadOnlyList<Line> lines)
    {
        Line[] over = [.. lines.Where(l => l.Account.StartsWith("53", StringComparison.Ordinal) && Math.Max(l.Debit, l.Credit) > CashLimit)];
        return Check("CashOverLimit", PracticeAuditSeverityDto.Alert, over.Select(l => $"{l.Journal} {l.EntryNumber} · {Amount(Math.Max(l.Debit, l.Credit))}"), over.Length == 0 ? null : over.Sum(l => Math.Max(l.Debit, l.Credit)));
    }

    /// <summary>Exemple lisible dans toutes les langues : dates AAAA-MM-JJ, montants au point décimal.</summary>
    private static string Amount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Les écritures d'au moins mille euros à la centaine près : rares dans une activité réelle, fréquentes dans une estimation.</summary>
    private static PracticeAuditCheckResponse RoundAmounts(IReadOnlyList<Entry> entries)
    {
        Entry[] large = [.. entries.Where(e => e.Amount >= RoundAmountFloor)];
        Entry[] round = [.. large.Where(e => e.Amount % 100m == 0m)];
        decimal share = large.Length == 0 ? 0m : Math.Round(100m * round.Length / large.Length, 1);
        bool flagged = round.Length >= RoundAmountMinimum && share > RoundAmountShare;
        return new PracticeAuditCheckResponse(
            "RoundAmounts",
            flagged ? PracticeAuditSeverityDto.Watch : PracticeAuditSeverityDto.Passed,
            round.Length,
            share,
            [.. round.Take(PracticeAuditResponse.MaxExamples).Select(e => $"{e.Journal} {e.Number} · {Amount(e.Amount)}")]);
    }

    /// <summary>Même jour, même compte, même montant, même libellé, dans deux écritures différentes : une saisie en double ?</summary>
    private static PracticeAuditCheckResponse DuplicateLines(IReadOnlyList<Line> lines) => Check(
        "DuplicateLines",
        PracticeAuditSeverityDto.Watch,
        lines
            .GroupBy(l => (l.EntryDate, l.Account, l.Auxiliary, l.Debit, l.Credit, Label: l.Label.ToUpperInvariant()))
            .Where(g => g.Select(l => (l.Journal, l.EntryNumber)).Distinct().Count() > 1)
            .Select(g => string.Join(" / ", g.Select(l => $"{l.Journal} {l.EntryNumber}").Distinct(StringComparer.Ordinal))));

    /// <summary>
    /// La loi de Benford : dans des montants réels, le premier chiffre est un 1 trois fois sur dix et un 9 moins d'une
    /// fois sur vingt. L'écart moyen absolu (Nigrini) au-delà de 1,5 point signale une série à regarder.
    /// </summary>
    private static (PracticeAuditCheckResponse Check, IReadOnlyList<BenfordDigitResponse> Digits) Benford(IReadOnlyList<Line> lines)
    {
        int[] digits = [.. lines.Select(l => Math.Max(l.Debit, l.Credit)).Where(a => a >= BenfordMinimumAmount).Select(FirstDigit)];
        if (digits.Length < BenfordMinimumAmounts)
        {
            return (new PracticeAuditCheckResponse("Benford", PracticeAuditSeverityDto.Passed, digits.Length, null, []), []);
        }

        BenfordDigitResponse[] distribution =
        [
            .. Enumerable.Range(1, 9).Select(d => new BenfordDigitResponse(
                d,
                Math.Round(100m * digits.Count(x => x == d) / digits.Length, 1),
                Math.Round(100m * (decimal)Math.Log10(1 + (1d / d)), 1))),
        ];
        decimal deviation = Math.Round(
            Enumerable.Range(1, 9).Average(d => Math.Abs((decimal)digits.Count(x => x == d) / digits.Length - (decimal)Math.Log10(1 + (1d / d)))) * 100m, 2);
        return (
            new PracticeAuditCheckResponse(
                "Benford",
                deviation > BenfordNonconformity ? PracticeAuditSeverityDto.Watch : PracticeAuditSeverityDto.Passed,
                digits.Length,
                deviation,
                [.. distribution.OrderByDescending(d => Math.Abs(d.Observed - d.Expected)).Take(3).Select(d => $"{d.Digit} · {d.Observed.ToString("0.0", CultureInfo.InvariantCulture)} / {d.Expected.ToString("0.0", CultureInfo.InvariantCulture)}")]),
            distribution);
    }

    private static int FirstDigit(decimal amount)
    {
        while (amount >= 10m)
        {
            amount /= 10m;
        }

        return (int)decimal.Truncate(amount);
    }

    /// <summary>Les comptes de tiers (auxiliaires) dont le solde final est du mauvais côté.</summary>
    private static PracticeAuditCheckResponse Balances(string code, IEnumerable<Line> lines, bool credit)
    {
        (string Account, decimal Balance)[] wrong =
        [
            .. lines
                .GroupBy(l => l.Auxiliary.Length > 0 ? l.Auxiliary : l.Account, StringComparer.Ordinal)
                .Select(g => (Account: g.Key, Balance: g.Sum(l => l.Debit - l.Credit)))
                .Where(b => credit ? b.Balance < 0m : b.Balance > 0m)
                .OrderBy(b => credit ? b.Balance : -b.Balance),
        ];
        return Check(code, PracticeAuditSeverityDto.Watch, wrong.Select(w => $"{w.Account} · {Amount(Math.Abs(w.Balance))}"), wrong.Length == 0 ? null : wrong.Sum(w => Math.Abs(w.Balance)));
    }

    /// <summary>La TVA collectée (4457) rapportée au chiffre d'affaires (70) : au-delà du taux normal, il y a une erreur.</summary>
    private static PracticeAuditCheckResponse VatRate(IReadOnlyList<Line> lines)
    {
        decimal revenue = lines.Where(l => l.Account.StartsWith("70", StringComparison.Ordinal)).Sum(l => l.Credit - l.Debit);
        decimal vat = lines.Where(l => l.Account.StartsWith("4457", StringComparison.Ordinal)).Sum(l => l.Credit - l.Debit);
        decimal? rate = revenue > 0m ? Math.Round(100m * vat / revenue, 2) : null;
        return new PracticeAuditCheckResponse(
            "VatRate",
            rate > MaximumVatRate ? PracticeAuditSeverityDto.Alert : PracticeAuditSeverityDto.Passed,
            0,
            rate,
            []);
    }

    /// <summary>Les lignes du FEC, ou rien si l'en-tête n'est pas celui d'un FEC (les lignes mal formées sont ignorées).</summary>
    private static List<Line>? Parse(string text)
    {
        string[] rows = [.. text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0)];
        if (rows.Length == 0)
        {
            return null;
        }

        char separator = rows[0].Contains('\t', StringComparison.Ordinal) ? '\t' : '|';
        string[] header = rows[0].Split(separator);
        if (!header.SequenceEqual(FecFormat.Columns, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        Dictionary<string, int> index = FecFormat.Columns.Select((c, i) => (c, i)).ToDictionary(p => p.c, p => p.i, StringComparer.Ordinal);
        List<Line> lines = [];
        foreach (string row in rows.Skip(1))
        {
            string[] fields = row.Split(separator);
            if (fields.Length != FecFormat.Columns.Count)
            {
                continue;
            }

            string Field(string column) => fields[index[column]].Trim();
            lines.Add(new Line(
                Field(FecFormat.JournalCode),
                Field(FecFormat.EcritureNum),
                FecFormat.TryParseDate(Field(FecFormat.EcritureDate), out DateOnly date) ? date : null,
                FecFormat.TryParseDate(Field(FecFormat.ValidDate), out DateOnly validated) ? validated : null,
                Field(FecFormat.CompteNum),
                Field(FecFormat.CompAuxNum),
                Field(FecFormat.PieceRef),
                Field(FecFormat.EcritureLib),
                FecFormat.TryParseAmount(Field(FecFormat.Debit), out decimal debit) ? debit : 0m,
                FecFormat.TryParseAmount(Field(FecFormat.Credit), out decimal credit) ? credit : 0m));
        }

        return lines;
    }

    private sealed record Line(
        string Journal, string EntryNumber, DateOnly? EntryDate, DateOnly? ValidDate, string Account, string Auxiliary, string PieceRef, string Label, decimal Debit, decimal Credit);

    private sealed record Entry(string Journal, string Number, DateOnly? Date, DateOnly? Validated, IReadOnlyList<Line> Lines)
    {
        public decimal Amount => Lines.Sum(l => l.Debit);
    }
}

internal sealed class GetPracticeAuditHandler(FecExportService fec) : IQueryHandler<GetPracticeAuditQuery, Result<PracticeAuditResponse>>
{
    public async Task<Result<PracticeAuditResponse>> HandleAsync(GetPracticeAuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Result<FecFile> file = await fec.ExportAsync(query.From, query.To, cancellationToken).ConfigureAwait(false);
        return file.IsSuccess
            ? PracticeAudit.Run(file.Value.FileName, file.Value.Content, query.From, query.To)
            : Result<PracticeAuditResponse>.Failure(file.Errors);
    }
}

internal sealed class RunPracticeAuditHandler : IQueryHandler<RunPracticeAuditQuery, Result<PracticeAuditResponse>>
{
    public Task<Result<PracticeAuditResponse>> HandleAsync(RunPracticeAuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Content.Length > ValidateFecQuery.MaxContentLength)
        {
            return Task.FromResult<Result<PracticeAuditResponse>>(FecErrors.FileTooLarge);
        }

        if (query is { From: { } from, To: { } to } && FecExportService.ValidatePeriod(from, to) is { IsFailure: true } period)
        {
            return Task.FromResult(Result<PracticeAuditResponse>.Failure(period.Errors));
        }

        return Task.FromResult<Result<PracticeAuditResponse>>(PracticeAudit.Run(query.FileName, query.Content.Span, query.From, query.To));
    }
}
