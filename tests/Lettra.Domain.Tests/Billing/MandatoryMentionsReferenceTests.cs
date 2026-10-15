using System.Text.Json.Nodes;
using Lettra.Calculations.Compliance;

namespace Lettra.Domain.Tests.Billing;

/// <summary>Le référentiel C# des mentions suit le fichier Paperasse embarqué dans Lettra.Ai.Skills (même commit).</summary>
public sealed class MandatoryMentionsReferenceTests
{
    private static readonly JsonNode Reference =
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "parity", "mentions-obligatoires.json")))!;

    [Fact]
    public void Chaque_mention_du_referentiel_est_portee()
    {
        Reference["version"]!.GetValue<string>().Should().Be(MandatoryMentions.ReferenceVersion);
        List<string> ids = [.. Reference["mentions"]!.AsObject()
            .SelectMany(section => section.Value!.AsArray().Select(m => m!["id"]!.GetValue<string>()))];

        ids.Should().NotBeEmpty();
        MandatoryMentions.All.Select(m => m.PaperasseId).Should().BeEquivalentTo(ids);
    }

    [Fact]
    public void Les_mentions_speciales_reprennent_le_texte_du_referentiel()
    {
        foreach (JsonNode? node in Reference["mentions_speciales"]!.AsArray())
        {
            string id = node!["id"]!.GetValue<string>();
            MandatoryMentions.SpecialText(id).Should().Be(node["texte"]!.GetValue<string>(), id);
        }
    }
}
