using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lettra.Compliance.Mentions;

/// <summary>Une mention obligatoire du référentiel Paperasse (data/facturation/mentions-obligatoires.json).</summary>
public sealed record MandatoryMention(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("base_legale")] string? LegalBasis,
    [property: JsonPropertyName("obligatoire")] bool Mandatory,
    [property: JsonPropertyName("depuis")] string Since,
    [property: JsonPropertyName("condition")] string? Condition)
{
    public bool IsLongstanding => Since == "existant";
}

public sealed record SpecialMention(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("condition")] string Condition,
    [property: JsonPropertyName("texte")] string Text);

/// <summary>Référentiel des mentions obligatoires, chargé depuis la ressource embarquée (copie épinglée de Paperasse).</summary>
public sealed class MandatoryMentionCatalog
{
    public string Version { get; }
    public string Source { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<MandatoryMention>> Sections { get; }
    public IReadOnlyList<SpecialMention> SpecialMentions { get; }

    private MandatoryMentionCatalog(Raw raw)
    {
        Version = raw.Version;
        Source = raw.Source;
        Sections = raw.Mentions.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<MandatoryMention>)kv.Value);
        SpecialMentions = raw.SpecialMentions;
    }

    public IReadOnlyList<MandatoryMention> Section(string name) =>
        Sections.TryGetValue(name, out var list) ? list : [];

    public static MandatoryMentionCatalog Parse(string json) =>
        new(JsonSerializer.Deserialize<Raw>(json) ?? throw new JsonException("Référentiel vide"));

    private static readonly Lazy<MandatoryMentionCatalog> Embedded = new(() =>
    {
        using var stream = typeof(MandatoryMentionCatalog).Assembly
            .GetManifestResourceStream("Lettra.Compliance.mentions-obligatoires.json")
            ?? throw new InvalidOperationException("Ressource mentions-obligatoires.json absente");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    });

    public static MandatoryMentionCatalog Default => Embedded.Value;

    private sealed record Raw(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("mentions")] Dictionary<string, List<MandatoryMention>> Mentions,
        [property: JsonPropertyName("mentions_speciales")] List<SpecialMention> SpecialMentions);
}
