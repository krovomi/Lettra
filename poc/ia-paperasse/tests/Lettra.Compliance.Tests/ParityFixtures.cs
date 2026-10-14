using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lettra.Compliance.Tests;

/// <summary>Accès aux résultats de référence produits par tests/parity/harness.js sur les scripts Paperasse.</summary>
internal static class ParityFixtures
{
    public static string GoldenDirectory => Path.Combine(AppContext.BaseDirectory, "parity", "golden");

    public static JsonArray LoadCases(string file)
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(GoldenDirectory, file)))!;
        Assert.Equal("romainsimon/paperasse@f2e15c4", root["reference"]!.GetValue<string>());
        return root["cases"]!.AsArray();
    }

    /// <summary>Dossier tests/parity du dépôt (pour relancer le banc avec Node quand il est disponible).</summary>
    public static string? SourceParityDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "parity", "harness.js");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate);
        }
        return null;
    }

    public static string? FindNode()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            foreach (var name in new[] { "node", "node.exe" })
            {
                var full = Path.Combine(dir, name);
                if (File.Exists(full)) return full;
            }
        }
        return null;
    }

    public static (int ExitCode, string Output) Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    public static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };
}
