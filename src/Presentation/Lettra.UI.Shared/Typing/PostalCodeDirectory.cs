using Lettra.UI.Shared.Components.Palette;
using Microsoft.JSInterop;

namespace Lettra.UI.Shared.Typing;

/// <summary>Une commune et son code postal.</summary>
public sealed record PostalPlace(string PostalCode, string City);

/// <summary>
/// Codes postaux et communes de France (fichier <c>wwwroot/data/codes-postaux.txt</c>, Licence Ouverte), cherchés en
/// mémoire : « 750 » propose les arrondissements de Paris, « st denis » trouve Saint-Denis sans tenir compte des
/// accents ni des tirets.
/// </summary>
public sealed class PostalCodeDirectory
{
    public const int MaxResults = 8;

    private readonly PostalPlace[] _places;
    private readonly string[] _normalized;

    private PostalCodeDirectory(PostalPlace[] places)
    {
        _places = places;
        _normalized = [.. places.Select(p => Normalize(p.City))];
    }

    public static PostalCodeDirectory Empty { get; } = new([]);

    public int Count => _places.Length;

    /// <summary>Lit le fichier « code;commune », une ligne par couple.</summary>
    public static PostalCodeDirectory Parse(string? text)
    {
        List<PostalPlace> places = [];
        foreach (string line in (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = line.IndexOf(';', StringComparison.Ordinal);
            if (separator == 5)
            {
                places.Add(new PostalPlace(line[..5], line[6..]));
            }
        }

        return new PostalCodeDirectory([.. places]);
    }

    /// <summary>Communes dont le code postal commence par <paramref name="prefix"/> (deux chiffres au moins).</summary>
    public IReadOnlyList<PostalPlace> ByPostalCode(string? prefix)
    {
        string value = (prefix ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (value.Length < 2 || !value.All(char.IsAsciiDigit))
        {
            return [];
        }

        return [.. _places.Where(p => p.PostalCode.StartsWith(value, StringComparison.Ordinal)).Take(MaxResults)];
    }

    /// <summary>Communes du nom exact, puis dont le nom commence par le texte, puis qui contiennent un mot qui commence par lui (trois lettres au moins).</summary>
    public IReadOnlyList<PostalPlace> ByCity(string? text)
    {
        string query = Normalize(text ?? string.Empty);
        if (query.Length < 3)
        {
            return [];
        }

        List<PostalPlace> exact = [];
        List<PostalPlace> starts = [];
        List<PostalPlace> contains = [];
        for (int i = 0; i < _places.Length; i++)
        {
            string name = _normalized[i];
            if (name == query)
            {
                exact.Add(_places[i]);
            }
            else if (starts.Count < MaxResults && name.StartsWith(query, StringComparison.Ordinal))
            {
                starts.Add(_places[i]);
            }
            else if (contains.Count < MaxResults && name.Contains(" " + query, StringComparison.Ordinal))
            {
                contains.Add(_places[i]);
            }
        }

        return [.. exact.Concat(starts).Concat(contains).Take(MaxResults)];
    }

    /// <summary>Sans accents, en minuscules, tirets et apostrophes ramenés à des espaces : « Saint-Étienne » → « saint etienne ».</summary>
    private static string Normalize(string text) =>
        string.Join(' ', PaletteMatcher.Normalize(text.Replace('-', ' ').Replace('\'', ' ')).Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>Charge le fichier des codes postaux une fois, à la première utilisation d'un champ d'adresse.</summary>
public sealed class PostalCodes(IJSRuntime js)
{
    public const string DataPath = "_content/Lettra.UI.Shared/data/codes-postaux.txt";

    private Task<PostalCodeDirectory>? _loading;

    public Task<PostalCodeDirectory> GetAsync()
    {
        if (_loading is null || _loading.IsFaulted || (_loading.IsCompletedSuccessfully && _loading.Result.Count == 0))
        {
            _loading = LoadAsync();
        }

        return _loading;
    }

    private async Task<PostalCodeDirectory> LoadAsync()
    {
        try
        {
            await using IJSObjectReference? module = await js.InvokeAsync<IJSObjectReference?>("import", AssistModule.Path);
            string? text = module is null ? null : await module.InvokeAsync<string?>("fetchText", DataPath);
            return PostalCodeDirectory.Parse(text);
        }
        catch (JSException)
        {
            return PostalCodeDirectory.Empty;
        }
        catch (InvalidOperationException)
        {
            return PostalCodeDirectory.Empty;
        }
    }
}

/// <summary>Module JS des champs assistés.</summary>
public static class AssistModule
{
    public const string Path = "./_content/Lettra.UI.Shared/lettra-assist.js";
}
