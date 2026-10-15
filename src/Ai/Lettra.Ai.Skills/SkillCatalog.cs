using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Lettra.Application.Abstractions.Ai;

namespace Lettra.Ai.Skills;

/// <summary>
/// Catalogue du skill pack Paperasse embarqué. Au chargement, chaque fichier est comparé au SHA-256 du manifest :
/// un fichier modifié, manquant ou ajouté empêche l'assistant de démarrer (chaîne d'approvisionnement, annexe B §7).
/// </summary>
public sealed partial class SkillCatalog : IAiSkillCatalog
{
    private const string ResourcePrefix = "skillpack/";
    private const string ManifestPath = "skillpack.manifest.json";

    private static readonly FrozenDictionary<AiSkill, string> Roots = new Dictionary<AiSkill, string>
    {
        [AiSkill.Comptable] = "vendor/paperasse/comptable",
        [AiSkill.ControleurFiscal] = "vendor/paperasse/controleur-fiscal",
        [AiSkill.CommissaireAuxComptes] = "vendor/paperasse/commissaire-aux-comptes",
    }.ToFrozenDictionary();

    private readonly FrozenDictionary<string, string> _files;

    private SkillCatalog(IReadOnlyDictionary<string, byte[]> files, string manifestJson)
    {
        Manifest manifest = JsonSerializer.Deserialize(manifestJson, SkillPackJsonContext.Default.Manifest)
            ?? throw new SkillPackIntegrityException("Manifest du skill pack illisible.");

        List<string> problems = [];
        foreach ((string path, string expected) in manifest.Files)
        {
            if (!files.TryGetValue(path, out byte[]? content))
            {
                problems.Add($"fichier manquant : {path}");
            }
            else if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(content)), expected, StringComparison.Ordinal))
            {
                problems.Add($"empreinte différente : {path}");
            }
        }

        problems.AddRange(files.Keys.Where(p => !manifest.Files.ContainsKey(p)).Select(p => $"fichier non déclaré : {p}"));
        if (problems.Count > 0)
        {
            throw new SkillPackIntegrityException("Skill pack refusé : " + string.Join(" ; ", problems));
        }

        _files = files.ToFrozenDictionary(kv => kv.Key, kv => Encoding.UTF8.GetString(kv.Value).TrimStart('﻿'), StringComparer.Ordinal);
        Info = new SkillPackInfo(
            manifest.Source,
            manifest.Commit,
            manifest.License,
            manifest.SkillsLastUpdated.ToDictionary(
                kv => AiSkillNames.TryParse(kv.Key, out AiSkill skill) ? skill : throw new SkillPackIntegrityException($"Skill inconnu dans le registre : {kv.Key}"),
                kv => DateOnly.Parse(kv.Value, CultureInfo.InvariantCulture)),
            manifest.ReviewedOn is { } reviewed ? DateOnly.Parse(reviewed, CultureInfo.InvariantCulture) : null,
            manifest.ReviewedBy);
    }

    public SkillPackInfo Info { get; }

    /// <summary>Skill pack embarqué dans l'assemblage, vérifié.</summary>
    public static SkillCatalog LoadEmbedded()
    {
        Assembly assembly = typeof(SkillCatalog).Assembly;
        Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        string? manifest = null;
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using Stream stream = assembly.GetManifestResourceStream(name)!;
            using MemoryStream buffer = new();
            stream.CopyTo(buffer);
            string path = name[ResourcePrefix.Length..].Replace('\\', '/');
            if (path == ManifestPath)
            {
                manifest = Encoding.UTF8.GetString(buffer.ToArray());
            }
            else
            {
                files[path] = buffer.ToArray();
            }
        }

        return new SkillCatalog(files, manifest ?? throw new SkillPackIntegrityException("Manifest du skill pack absent."));
    }

    /// <summary>Construit un catalogue à partir de fichiers donnés (tests d'intégrité).</summary>
    public static SkillCatalog FromFiles(IReadOnlyDictionary<string, byte[]> files, string manifestJson) => new(files, manifestJson);

    public string GetOverlay() => Read("overlays/lettra-context.md") + "\n\n" + Read("overlays/company-json-mapping.md");

    public string GetSkillInstructions(AiSkill skill) => Read($"{Roots[skill]}/SKILL.md");

    public IReadOnlyList<string> ListReferences(AiSkill skill)
    {
        string prefix = $"{Roots[skill]}/references/";
        return [.. _files.Keys
            .Where(p => p.StartsWith(prefix, StringComparison.Ordinal) && p.EndsWith(".md", StringComparison.Ordinal))
            .Select(p => p[prefix.Length..])
            .Order(StringComparer.Ordinal)];
    }

    public string? ReadReference(AiSkill skill, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        // Liste blanche : seulement references/*.md du skill demandé. Pas de « .. », pas de scripts, pas de données.
        string path = relativePath.Trim().Replace('\\', '/');
        if (path.StartsWith("references/", StringComparison.Ordinal))
        {
            path = path["references/".Length..];
        }

        return SafeReferencePath().IsMatch(path) ? _files.GetValueOrDefault($"{Roots[skill]}/references/{path}") : null;
    }

    private string Read(string path) =>
        _files.TryGetValue(path, out string? content) ? content : throw new SkillPackIntegrityException($"Fichier absent du skill pack : {path}");

    [GeneratedRegex(@"^[a-z0-9-]+(/[a-z0-9-]+)?\.md\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeReferencePath();

    internal sealed record Manifest(
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("commit")] string Commit,
        [property: JsonPropertyName("license")] string License,
        [property: JsonPropertyName("skillsLastUpdated")] Dictionary<string, string> SkillsLastUpdated,
        [property: JsonPropertyName("reviewedOn")] string? ReviewedOn,
        [property: JsonPropertyName("reviewedBy")] string? ReviewedBy,
        [property: JsonPropertyName("files")] Dictionary<string, string> Files);
}

/// <summary>Le skill pack embarqué ne correspond pas à son manifest : l'assistant ne doit pas démarrer.</summary>
public sealed class SkillPackIntegrityException(string message) : Exception(message);

[JsonSerializable(typeof(SkillCatalog.Manifest))]
internal sealed partial class SkillPackJsonContext : JsonSerializerContext;
