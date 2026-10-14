using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lettra.Ai.Tools;

/// <summary>Petits constructeurs de JSON Schema stricts (<c>additionalProperties: false</c> partout).</summary>
internal static class Schema
{
    public static JsonObject Object(params (string Name, JsonNode Schema, bool Required)[] properties)
    {
        JsonObject props = [];
        foreach ((string name, JsonNode schema, bool _) in properties)
        {
            props[name] = schema;
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JsonArray([.. properties.Where(p => p.Required).Select(p => (JsonNode)p.Name)]),
            ["additionalProperties"] = false,
        };
    }

    public static JsonObject String(string description, params string[] allowed)
    {
        JsonObject schema = new() { ["type"] = "string", ["description"] = description };
        if (allowed.Length > 0)
        {
            schema["enum"] = new JsonArray([.. allowed.Select(a => (JsonNode)a)]);
        }

        return schema;
    }

    public static JsonElement ToElement(this JsonNode node) => JsonSerializer.SerializeToElement(node);
}
