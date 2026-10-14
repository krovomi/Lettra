using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lettra.Ai.Tools;

/// <summary>Petits constructeurs de JSON Schema stricts (additionalProperties: false partout).</summary>
internal static class Schema
{
    public static JsonObject Object(params (string Name, JsonNode Schema, bool Required)[] properties)
    {
        var props = new JsonObject();
        foreach (var p in properties) props[p.Name] = p.Schema;
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JsonArray(properties.Where(p => p.Required).Select(p => (JsonNode)p.Name).ToArray()),
            ["additionalProperties"] = false,
        };
    }

    public static JsonObject String(string description, params string[] allowed)
    {
        var s = new JsonObject { ["type"] = "string", ["description"] = description };
        if (allowed.Length > 0) s["enum"] = new JsonArray(allowed.Select(a => (JsonNode)a).ToArray());
        return s;
    }

    public static JsonObject Number(string description) => new() { ["type"] = "number", ["description"] = description };

    public static JsonObject Array(JsonNode items, string description) =>
        new() { ["type"] = "array", ["items"] = items, ["description"] = description, ["minItems"] = 1 };

    public static JsonElement ToElement(this JsonNode node) => JsonSerializer.SerializeToElement(node);
}
