using System.Text.Json;

namespace Lettra.Ai.Orchestration;

/// <summary>
/// Validation minimale d'une entrée d'outil contre son JSON Schema (type, required, enum, additionalProperties, minItems).
/// Un moteur peut produire une entrée tronquée ou fantaisiste : elle est refusée avant d'atteindre l'application.
/// </summary>
public static class JsonSchemaValidator
{
    public static string? Validate(JsonElement value, JsonElement schema, string path = "$")
    {
        if (schema.TryGetProperty("type", out var typeEl))
        {
            var type = typeEl.GetString();
            var ok = type switch
            {
                "object" => value.ValueKind == JsonValueKind.Object,
                "array" => value.ValueKind == JsonValueKind.Array,
                "string" => value.ValueKind == JsonValueKind.String,
                "number" => value.ValueKind == JsonValueKind.Number,
                "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                _ => true,
            };
            if (!ok) return $"{path} : type attendu {type}";
        }

        if (schema.TryGetProperty("enum", out var enumEl) && enumEl.EnumerateArray().All(e => !JsonElement.DeepEquals(e, value)))
            return $"{path} : valeur non autorisée";

        if (value.ValueKind == JsonValueKind.Object)
        {
            var props = schema.TryGetProperty("properties", out var p) ? p : default;
            if (schema.TryGetProperty("required", out var req))
                foreach (var r in req.EnumerateArray())
                    if (!value.TryGetProperty(r.GetString()!, out _)) return $"{path}.{r.GetString()} : obligatoire";
            foreach (var member in value.EnumerateObject())
            {
                if (props.ValueKind == JsonValueKind.Object && props.TryGetProperty(member.Name, out var sub))
                {
                    if (Validate(member.Value, sub, $"{path}.{member.Name}") is { } error) return error;
                }
                else if (schema.TryGetProperty("additionalProperties", out var ap) && ap.ValueKind == JsonValueKind.False)
                {
                    return $"{path}.{member.Name} : propriété inconnue";
                }
            }
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            if (schema.TryGetProperty("minItems", out var min) && value.GetArrayLength() < min.GetInt32()) return $"{path} : liste trop courte";
            if (schema.TryGetProperty("items", out var items))
            {
                var i = 0;
                foreach (var item in value.EnumerateArray())
                    if (Validate(item, items, $"{path}[{i++}]") is { } error) return error;
            }
        }
        return null;
    }
}
