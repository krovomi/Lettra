namespace Lettra.Worker;

internal sealed class Options(Dictionary<string, string> values)
{
    public static Options Parse(IEnumerable<string> args)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? key = null;
        foreach (var a in args)
        {
            if (a.StartsWith("--", StringComparison.Ordinal)) { key = a[2..]; d[key] = "true"; }
            else if (key is not null) { d[key] = a; key = null; }
        }
        return new Options(d);
    }

    public int Int(string name, int fallback) => values.TryGetValue(name, out var v) ? int.Parse(v) : fallback;
    public int? IntOrNull(string name) => values.TryGetValue(name, out var v) ? int.Parse(v) : null;
    public string Str(string name, string fallback) => values.GetValueOrDefault(name, fallback);
    public bool Flag(string name) => values.ContainsKey(name);
}
