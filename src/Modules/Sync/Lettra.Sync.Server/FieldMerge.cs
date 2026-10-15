using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Lettra.Contracts.Sync;

namespace Lettra.Sync.Server;

/// <summary>Résultat d'une fusion à trois voies sur un contrat de saisie.</summary>
/// <param name="Merged">L'état à écrire : le serveur, plus les champs changés sur le poste et restés libres.</param>
/// <param name="MineChanged">Le poste a changé au moins un champ, sans conflit.</param>
/// <param name="ServerChanged">Le serveur avait changé d'autres champs entre-temps.</param>
/// <param name="Conflicts">Champs changés des deux côtés avec des valeurs différentes : la valeur du serveur est gardée.</param>
internal sealed record MergeOutcome<T>(T Merged, bool MineChanged, bool ServerChanged, IReadOnlyList<SyncFieldConflict> Conflicts);

/// <summary>
/// Fusion champ par champ (ADR-0013), sur la forme JSON du contrat : un champ que le poste n'a pas touché garde la valeur
/// du serveur ; un champ que seul le poste a changé prend la sienne ; un champ changé des deux côtés vers la même valeur
/// n'est pas un conflit. Le nom de champ rendu est celui de la propriété du contrat (comme <c>ErrorDetail.Target</c>).
/// </summary>
internal static class FieldMerge
{
    public static MergeOutcome<T> Merge<T>(T @base, T mine, T server, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        JsonObject b = Node(@base, typeInfo);
        JsonObject m = Node(mine, typeInfo);
        JsonObject s = Node(server, typeInfo);
        JsonObject merged = Node(server, typeInfo);

        bool mineChanged = false, serverChanged = false;
        List<SyncFieldConflict> conflicts = [];
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            string name = property.Name;
            JsonNode? baseValue = b[name], mineValue = m[name], serverValue = s[name];
            bool byMe = !JsonNode.DeepEquals(baseValue, mineValue);
            bool byServer = !JsonNode.DeepEquals(baseValue, serverValue);
            serverChanged |= byServer && !JsonNode.DeepEquals(mineValue, serverValue);

            if (!byMe || JsonNode.DeepEquals(mineValue, serverValue))
            {
                continue;
            }

            if (byServer)
            {
                conflicts.Add(new SyncFieldConflict(MemberName(property), Element(baseValue), Element(mineValue), Element(serverValue)));
                continue;
            }

            merged[name] = mineValue?.DeepClone();
            mineChanged = true;
        }

        return new MergeOutcome<T>(merged.Deserialize(typeInfo)!, mineChanged, serverChanged, conflicts);
    }

    private static JsonObject Node<T>(T value, JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.SerializeToNode(value, typeInfo)?.AsObject()
        ?? throw new ArgumentException("Le contrat doit se sérialiser en objet JSON.", nameof(value));

    private static string MemberName(JsonPropertyInfo property) =>
        property.AttributeProvider is MemberInfo member ? member.Name : property.Name;

    private static JsonElement Element(JsonNode? node)
    {
        using JsonDocument document = JsonDocument.Parse(node?.ToJsonString() ?? "null");
        return document.RootElement.Clone();
    }
}
