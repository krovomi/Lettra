using System.Text.Json.Serialization;

namespace Lettra.Compliance.Mentions;

/// <summary>Un constat du contrôle : mention présente, manquante (erreur) ou à surveiller (avertissement).</summary>
public sealed record ComplianceFinding(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("base_legale"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LegalBasis = null,
    [property: JsonPropertyName("depuis"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Since = null,
    [property: JsonPropertyName("value"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Value = null);

public sealed record ComplianceReport(
    [property: JsonPropertyName("errors")] IReadOnlyList<ComplianceFinding> Errors,
    [property: JsonPropertyName("warnings")] IReadOnlyList<ComplianceFinding> Warnings,
    [property: JsonPropertyName("ok")] IReadOnlyList<ComplianceFinding> Ok)
{
    /// <summary>Une facture avec au moins une erreur ne peut pas être validée (règle déterministe, jamais l'IA).</summary>
    [JsonIgnore] public bool BlocksValidation => Errors.Count > 0;

    [JsonIgnore]
    public string Status => Errors.Count > 0
        ? "NON CONFORME"
        : Warnings.Count > 0 ? "CONFORME AVEC AVERTISSEMENTS" : "CONFORME";
}
