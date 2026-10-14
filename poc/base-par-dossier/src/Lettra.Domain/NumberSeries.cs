namespace Lettra.Domain;

/// Série de numérotation d'un dossier (ADR-0007). Le compteur avance sous concurrence optimiste
/// (colonne Version), ce qui reste neutre vis-à-vis du fournisseur de base.
public sealed class NumberSeries
{
    private NumberSeries() { }

    public NumberSeries(string code, string prefix)
    {
        Code = code;
        Prefix = prefix;
        Next = 1;
    }

    public string Code { get; private set; } = "";
    public string Prefix { get; private set; } = "";
    public long Next { get; private set; }
    public long Version { get; private set; }

    public string Take()
    {
        var number = $"{Prefix}{Next:D6}";
        Next++;
        Version++;
        return number;
    }
}
