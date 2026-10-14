using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Lettra.Ai.Abstractions;

namespace Lettra.Ai.Skills;

/// <summary>
/// Catalogue du skill pack Paperasse embarqué. Au démarrage, chaque fichier est comparé au SHA-256 du manifest :
/// un fichier modifié, manquant ou ajouté empêche l'assistant de démarrer (chaîne d'approvisionnement, annexe B §7).
/// </summary>
public sealed partial class SkillCatalog : IAiSkillCatalog
{
    private const string ResourcePrefix = "skillpack/";

    private static readonly FrozenDictionary<AiSkill, string> Roots = new Dictionary<AiSkill, string>
    {
        [AiSkill.Comptable] = "vendor/paperasse/comptable",
        [AiSkill.ControleurFiscal] = "vendor/paperasse/controleur-fiscal",
        [AiSkill.CommissaireAuxComptes] = "vendor/paperasse/commissaire-aux-comptes",
    }.ToFrozenDictionary();

    private readonly FrozenDictionary<string, string> _files;

    public SkillPackInfo Info { get; }

    private SkillCatalog(IReadOnlyDictionary<string, byte[]> files, string manifestJson)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(manifestJson)
            ?? throw new SkillPackIntegrityException("Manifest du skill pack illisible.");

        var problems = new List<string>();
        foreach (var (path, expected) in manifest.Files)
        {
            if (!files.TryGetValue(path, out var content)) problems.Add($"fichier manquant : {path}");
            else if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(content)), expected, StringComparison.Ordinal))
                problems.Add($"empreinte différente : {path}");
        }
        problems.AddRange(files.Keys.Where(p => !manifest.Files.ContainsKey(p)).Select(p => $"fichier non déclaré : {p}"));
        if (problems.Count > 0)
            throw new SkillPackIntegrityException("Skill pack refusé : " + string.Join(" ; ", problems));

        _files = files.ToFrozenDictionary(kv => kv.Key, kv => Encoding.UTF8.GetString(kv.Value).TrimStart('﻿'));
        Info = new SkillPackInfo(
            manifest.Source,
            manifest.Commit,
            manifest.License,
            DateOnly.Parse(manifest.SkillLastUpdated),
            manifest.ReviewedOn is { } r ? DateOnly.Parse(r) : null,
            manifest.ReviewedBy);
    }

    /// <summary>Skill pack embarqué dans l'assemblage, vérifié.</summary>
    public static SkillCatalog LoadEmbedded()
    {
        var assembly = typeof(SkillCatalog).Assembly;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        string? manifest = null;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var path = name[ResourcePrefix.Length..].Replace('\\', '/');
            if (path == "skillpack.manifest.json") manifest = Encoding.UTF8.GetString(buffer.ToArray());
            else files[path] = buffer.ToArray();
        }
        return new SkillCatalog(files, manifest ?? throw new SkillPackIntegrityException("Manifest du skill pack absent."));
    }

    /// <summary>Construit un catalogue à partir de fichiers donnés (tests d'intégrité).</summary>
    public static SkillCatalog FromFiles(IReadOnlyDictionary<string, byte[]> files, string manifestJson) => new(files, manifestJson);

    public string GetOverlay() => Read("overlays/lettra-context.md") + "\n\n" + Read("overlays/company-json-mapping.md");

    public string GetSkillInstructions(AiSkill skill) => Read($"{Roots[skill]}/SKILL.md");

    public IReadOnlyList<string> ListReferences(AiSkill skill)
    {
        var prefix = $"{Roots[skill]}/references/";
        return _files.Keys.Where(p => p.StartsWith(prefix, StringComparison.Ordinal) && p.EndsWith(".md", StringComparison.Ordinal))
            .Select(p => p[prefix.Length..])
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    public string? ReadReference(AiSkill skill, string relativePath)
    {
        // Liste blanche : seulement references/*.md du skill demandé. Pas de « .. », pas de scripts, pas de données.
        var path = relativePath.Trim().Replace('\\', '/');
        if (path.StartsWith("references/", StringComparison.Ordinal)) path = path["references/".Length..];
        if (!SafeReferencePath().IsMatch(path)) return null;
        return _files.GetValueOrDefault($"{Roots[skill]}/references/{path}");
    }

    private string Read(string path) =>
        _files.TryGetValue(path, out var content) ? content : throw new SkillPackIntegrityException($"Fichier absent du skill pack : {path}");

    [GeneratedRegex(@"^[a-z0-9-]+(/[a-z0-9-]+)?\.md\z")]
    private static partial Regex SafeReferencePath();

    private sealed record Manifest(
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("commit")] string Commit,
        [property: JsonPropertyName("license")] string License,
        [property: JsonPropertyName("skillLastUpdated")] string SkillLastUpdated,
        [property: JsonPropertyName("reviewedOn")] string? ReviewedOn,
        [property: JsonPropertyName("reviewedBy")] string? ReviewedBy,
        [property: JsonPropertyName("files")] Dictionary<string, string> Files);
}

public sealed class SkillPackIntegrityException(string message) : Exception(message);
