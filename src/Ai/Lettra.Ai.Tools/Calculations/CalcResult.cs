using System.Text;

namespace Lettra.Ai.Tools.Calculations;

/// <summary>Résultat d'un calcul déterministe : un titre et des lignes « libellé : valeur », ou une erreur.</summary>
internal sealed record CalcResult(string? Title, IReadOnlyList<KeyValuePair<string, string>> Rows, string? Error)
{
    public bool IsSuccess => Error is null;

    public static CalcResult Ok(string title, IReadOnlyList<KeyValuePair<string, string>> rows) => new(title, rows, null);
    public static CalcResult Fail(string error) => new(null, [], error);

    public string? this[string key] => Rows.FirstOrDefault(r => r.Key == key).Value;

    /// <summary>Même rendu texte que <c>printResult</c> de calc.js (sortie standard), ou « Erreur: … ».</summary>
    public string ToText()
    {
        if (!IsSuccess)
        {
            return "Erreur: " + Error;
        }

        var sb = new StringBuilder();
        sb.Append(Title).Append('\n').Append(new string('=', Title!.Length)).Append('\n');
        foreach (var (k, v) in Rows)
        {
            sb.Append(k).Append(": ").Append(v).Append('\n');
        }

        return sb.ToString();
    }
}
