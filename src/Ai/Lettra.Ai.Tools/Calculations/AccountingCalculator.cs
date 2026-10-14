using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Lettra.Ai.Tools.Calculations;

/// <summary>
/// Port C# de <c>comptable/scripts/calc.js</c> (Paperasse, commit f2e15c4) : calculs en centimes entiers,
/// arrondi au centime le plus proche, demi-centime éloigné de zéro. C'est l'outil « calculer » de l'assistant :
/// le modèle ne calcule jamais lui-même (annexe B §2).
/// </summary>
internal static partial class AccountingCalculator
{
    public static readonly IReadOnlyList<string> Commands = ["cca", "amortissement-lineaire", "is", "tva-acomptes-rs", "prorata"];

    /// <summary>Exécute une commande avec ses paramètres (noms identiques aux options de calc.js, sans « -- »).</summary>
    public static CalcResult Run(string command, IReadOnlyDictionary<string, string> args)
    {
        try
        {
            return command switch
            {
                "cca" => Cca(args),
                "amortissement-lineaire" => AmortissementLineaire(args),
                "is" => ImpotSurLesSocietes(args),
                "tva-acomptes-rs" => TvaAcomptesRs(args),
                "prorata" => Prorata(args),
                _ => throw new CalcException($"commande inconnue: {command}. Lancez: node scripts/calc.js --help"),
            };
        }
        catch (CalcException e)
        {
            return CalcResult.Fail(e.Message);
        }
    }

    private static CalcResult Cca(IReadOnlyDictionary<string, string> a)
    {
        var total = ParseAmountToCents(Get(a, "total"), "total");
        var joursN1 = ParseIntStrict(Get(a, "jours-n-plus-1"), "jours-n-plus-1");
        var joursTotaux = ParseIntStrict(Get(a, "jours-totaux"), "jours-totaux");
        if (joursN1 < 0)
        {
            throw new CalcException("--jours-n-plus-1 doit etre >= 0");
        }

        if (joursTotaux <= 0)
        {
            throw new CalcException("--jours-totaux doit etre > 0");
        }

        var cca = RoundDivSigned(total * joursN1, joursTotaux);
        return CalcResult.Ok("Calcul CCA",
        [
            new("Formule", "CCA = Montant total x (Jours N+1 / Jours totaux)"),
            new("Montant total", FormatCents(total)),
            new("Jours N+1", joursN1.ToString(CultureInfo.InvariantCulture)),
            new("Jours totaux", joursTotaux.ToString(CultureInfo.InvariantCulture)),
            new("CCA", FormatCents(cca)),
        ]);
    }

    private static CalcResult Prorata(IReadOnlyDictionary<string, string> a)
    {
        var montant = ParseAmountToCents(Get(a, "montant"), "montant");
        var jours = ParseIntStrict(Get(a, "jours"), "jours");
        var @base = ParseIntStrict(OrDefault(a, "base", "365"), "base");
        if (jours < 0)
        {
            throw new CalcException("--jours doit etre >= 0");
        }

        if (@base <= 0)
        {
            throw new CalcException("--base doit etre > 0");
        }

        var value = RoundDivSigned(montant * jours, @base);
        return CalcResult.Ok("Calcul Prorata",
        [
            new("Formule", "Montant prorata = Montant x (Jours / Base)"),
            new("Montant", FormatCents(montant)),
            new("Jours", jours.ToString(CultureInfo.InvariantCulture)),
            new("Base", @base.ToString(CultureInfo.InvariantCulture)),
            new("Resultat", FormatCents(value)),
        ]);
    }

    private static CalcResult AmortissementLineaire(IReadOnlyDictionary<string, string> a)
    {
        var valeur = ParseAmountToCents(Get(a, "valeur"), "valeur");
        var duree = ParseIntStrict(Get(a, "duree"), "duree");
        if (duree <= 0)
        {
            throw new CalcException("--duree doit etre > 0");
        }

        var annuitePleine = RoundDivSigned(valeur, duree);
        BigInteger? joursUtilises = a.ContainsKey("jours-utilises")
            ? ParseIntStrict(a["jours-utilises"], "jours-utilises")
            : null;
        var baseJours = ParseIntStrict(OrDefault(a, "base-jours", "365"), "base-jours");
        if (baseJours <= 0)
        {
            throw new CalcException("--base-jours doit etre > 0");
        }

        if (joursUtilises is { } ju && ju < 0)
        {
            throw new CalcException("--jours-utilises doit etre >= 0");
        }

        var dotation = joursUtilises is { } j ? RoundDivSigned(annuitePleine * j, baseJours) : annuitePleine;

        var rows = new List<KeyValuePair<string, string>>
        {
            new("Formule annuite", "Annuite = Valeur brute / Duree"),
            new("Valeur brute", FormatCents(valeur)),
            new("Duree", duree.ToString(CultureInfo.InvariantCulture) + " an(s)"),
            new("Annuite pleine", FormatCents(annuitePleine)),
        };
        if (joursUtilises is { } used)
        {
            rows.Add(new("Formule prorata", "Dotation = Annuite x (Jours utilises / Base jours)"));
            rows.Add(new("Jours utilises", used.ToString(CultureInfo.InvariantCulture)));
            rows.Add(new("Base jours", baseJours.ToString(CultureInfo.InvariantCulture)));
        }
        rows.Add(new("Dotation periode", FormatCents(dotation)));
        return CalcResult.Ok("Calcul Amortissement Lineaire", rows);
    }

    private static CalcResult ImpotSurLesSocietes(IReadOnlyDictionary<string, string> a)
    {
        var resultatFiscal = ParseAmountToCents(Get(a, "resultat-fiscal"), "resultat-fiscal");
        if (resultatFiscal <= 0)
        {
            return CalcResult.Ok("Calcul IS",
            [
                new("Resultat fiscal", FormatCents(resultatFiscal)),
                new("IS", "0,00 EUR (resultat fiscal <= 0)"),
            ]);
        }

        BigInteger totalIs;
        var rows = new List<KeyValuePair<string, string>> { new("Resultat fiscal", FormatCents(resultatFiscal)) };

        if (a.TryGetValue("taux", out var taux))
        {
            var tauxBps = ParseRateToBps(taux, "taux");
            totalIs = RoundDivSigned(resultatFiscal * tauxBps, 10000);
            rows.Add(new("Mode", "taux unique"));
            rows.Add(new("Taux", taux + " %"));
            rows.Add(new("Formule", "IS = Resultat fiscal x Taux"));
        }
        else if (a.ContainsKey("taux-reduit") || a.ContainsKey("taux-normal") || a.ContainsKey("plafond"))
        {
            var tauxReduitRaw = a.GetValueOrDefault("taux-reduit") ?? "15";
            var tauxNormalRaw = a.GetValueOrDefault("taux-normal") ?? "25";
            var tauxReduitBps = ParseRateToBps(tauxReduitRaw, "taux-reduit");
            var tauxNormalBps = ParseRateToBps(tauxNormalRaw, "taux-normal");
            var plafondAnnuel = ParseAmountToCents(a.GetValueOrDefault("plafond") ?? "42500", "plafond");
            BigInteger? joursExercice = a.ContainsKey("jours-exercice")
                ? ParseIntStrict(a["jours-exercice"], "jours-exercice")
                : null;
            var plafond = ProrateAnnualThreshold(plafondAnnuel, joursExercice);

            var trancheReduite = resultatFiscal < plafond ? resultatFiscal : plafond;
            var trancheNormale = resultatFiscal > plafond ? resultatFiscal - plafond : BigInteger.Zero;
            var isReduit = RoundDivSigned(trancheReduite * tauxReduitBps, 10000);
            var isNormal = RoundDivSigned(trancheNormale * tauxNormalBps, 10000);
            totalIs = isReduit + isNormal;

            rows.Add(new("Mode", "deux tranches"));
            rows.Add(new("Plafond annuel taux reduit", FormatCents(plafondAnnuel)));
            if (joursExercice is { } je)
            {
                rows.Add(new("Jours exercice", je.ToString(CultureInfo.InvariantCulture)));
            }

            rows.Add(new("Plafond taux reduit applique", FormatCents(plafond)));
            rows.Add(new("Taux reduit", tauxReduitRaw + " %"));
            rows.Add(new("Taux normal", tauxNormalRaw + " %"));
            rows.Add(new("Tranche reduite", FormatCents(trancheReduite)));
            rows.Add(new("Tranche normale", FormatCents(trancheNormale)));
            rows.Add(new("IS tranche reduite", FormatCents(isReduit)));
            rows.Add(new("IS tranche normale", FormatCents(isNormal)));
        }
        else
        {
            throw new CalcException("pour la commande is, fournir --taux OU (--taux-reduit/--taux-normal/--plafond)");
        }

        rows.Add(new("IS total", FormatCents(totalIs)));
        return CalcResult.Ok("Calcul IS", rows);
    }

    private static CalcResult TvaAcomptesRs(IReadOnlyDictionary<string, string> a)
    {
        var tvaN1 = ParseAmountToCents(Get(a, "tva-n-1"), "tva-n-1");
        var a1 = RoundDivSigned(tvaN1 * 55, 100);
        var a2 = RoundDivSigned(tvaN1 * 40, 100);
        return CalcResult.Ok("Calcul Acomptes TVA Regime Simplifie",
        [
            new("TVA N-1", FormatCents(tvaN1)),
            new("Acompte juillet (55%)", FormatCents(a1)),
            new("Acompte decembre (40%)", FormatCents(a2)),
            new("Total acomptes", FormatCents(a1 + a2)),
        ]);
    }

    // ---------------------------------------------------------------------
    // Primitives (mêmes règles que calc.js)
    // ---------------------------------------------------------------------

    /// <summary>Division entière arrondie au plus proche, demi éloigné de zéro.</summary>
    public static BigInteger RoundDivSigned(BigInteger numer, BigInteger denom)
    {
        if (denom <= 0)
        {
            throw new CalcException("denominateur doit etre > 0");
        }

        return numer >= 0
            ? (numer + denom / 2) / denom
            : -((-numer + denom / 2) / denom);
    }

    public static BigInteger ProrateAnnualThreshold(BigInteger thresholdCents, BigInteger? exerciseDays)
    {
        if (exerciseDays is not { } days)
        {
            return thresholdCents;
        }

        if (days <= 0)
        {
            throw new CalcException("--jours-exercice doit etre > 0");
        }

        return RoundDivSigned(thresholdCents * days, 365);
    }

    public static string FormatCents(BigInteger cents)
    {
        var neg = cents < 0;
        var abs = BigInteger.Abs(cents);
        var text = (abs / 100).ToString(CultureInfo.InvariantCulture) + "," +
                   (abs % 100).ToString(CultureInfo.InvariantCulture).PadLeft(2, '0') + " EUR";
        return neg ? "-" + text : text;
    }

    public static BigInteger ParseAmountToCents(string? value, string name)
    {
        if (value is null)
        {
            throw new CalcException("parametre manquant --" + name);
        }

        var raw = ReplaceFirst(RemoveJsWhitespace(value), ",", ".");
        if (!AmountPattern().IsMatch(raw))
        {
            throw new CalcException("montant invalide pour --" + name + ": " + value);
        }

        var neg = raw.StartsWith('-');
        var abs = neg ? raw[1..] : raw;
        var parts = abs.Split('.');
        var cents = BigInteger.Parse(parts[0] + (parts.Length > 1 ? parts[1] : "").PadRight(2, '0'), CultureInfo.InvariantCulture);
        return neg ? -cents : cents;
    }

    public static BigInteger ParseRateToBps(string? value, string name)
    {
        if (value is null)
        {
            throw new CalcException("parametre manquant --" + name);
        }

        var raw = ReplaceFirst(JsTrim(value), ",", ".");
        if (!RatePattern().IsMatch(raw))
        {
            throw new CalcException("taux invalide pour --" + name + ": " + value);
        }

        var num = double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
        return new BigInteger(JsMathRound(num * 100));
    }

    /// <summary>Comme calc.js : entier validé par regex puis converti via Number (double), puis BigInt.</summary>
    public static BigInteger ParseIntStrict(string? value, string name)
    {
        if (value is null)
        {
            throw new CalcException("parametre manquant --" + name);
        }

        if (!IntPattern().IsMatch(value))
        {
            throw new CalcException("entier invalide pour --" + name + ": " + value);
        }

        return new BigInteger(double.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
    }

    /// <summary>Math.round de JavaScript : arrondi vers +∞ sur les demis.</summary>
    private static double JsMathRound(double v)
    {
        var floor = Math.Floor(v);
        return v - floor >= 0.5 ? floor + 1 : floor;
    }

    private static string? Get(IReadOnlyDictionary<string, string> a, string key) => a.GetValueOrDefault(key);

    /// <summary>Équivalent de <c>args[key] || défaut</c> : une chaîne vide prend la valeur par défaut.</summary>
    private static string OrDefault(IReadOnlyDictionary<string, string> a, string key, string fallback) =>
        a.TryGetValue(key, out var v) && v.Length > 0 ? v : fallback;

    private static string ReplaceFirst(string s, string oldValue, string newValue)
    {
        var i = s.IndexOf(oldValue, StringComparison.Ordinal);
        return i < 0 ? s : string.Concat(s.AsSpan(0, i), newValue, s.AsSpan(i + oldValue.Length));
    }

    private static bool IsJsWhitespace(char c) => char.IsWhiteSpace(c) || c == '﻿';
    private static string RemoveJsWhitespace(string s) => string.Concat(s.Where(c => !IsJsWhitespace(c)));
    private static string JsTrim(string s) => s.Trim().Trim('﻿');

    [GeneratedRegex(@"^-?[0-9]+(\.[0-9]{1,2})?\z")] private static partial Regex AmountPattern();
    [GeneratedRegex(@"^-?[0-9]+(\.[0-9]+)?\z")] private static partial Regex RatePattern();
    [GeneratedRegex(@"^-?[0-9]+\z")] private static partial Regex IntPattern();

    private sealed class CalcException(string message) : Exception(message);
}
