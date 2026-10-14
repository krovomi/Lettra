using Xunit.Abstractions;

namespace Lettra.Compliance.Tests;

/// <summary>Les fichiers de référence commités doivent être ceux que produisent les scripts Paperasse épinglés.</summary>
public sealed class ParityGoldenFreshnessTests(ITestOutputHelper output)
{
    [Fact]
    public void Les_resultats_de_reference_correspondent_aux_scripts_paperasse_epingles()
    {
        var node = ParityFixtures.FindNode();
        var parityDir = ParityFixtures.SourceParityDirectory();
        if (node is null || parityDir is null)
        {
            output.WriteLine("Node.js absent : vérification de fraîcheur ignorée (la CI l'exécute).");
            return;
        }

        var temp = Directory.CreateTempSubdirectory("lettra-parity-").FullName;
        try
        {
            var (exit, log) = ParityFixtures.Run(node, Path.Combine(parityDir, "harness.js"), temp);
            Assert.True(exit == 0, log);
            foreach (var file in new[] { "validate-facture.json", "calc.json", "totals.json" })
            {
                Assert.True(
                    File.ReadAllText(Path.Combine(parityDir, "golden", file)) == File.ReadAllText(Path.Combine(temp, file)),
                    $"{file} n'est plus à jour : relancer « node tests/parity/harness.js » et relire l'écart.");
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
