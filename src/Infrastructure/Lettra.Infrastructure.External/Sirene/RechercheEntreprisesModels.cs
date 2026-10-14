using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lettra.Infrastructure.External.Sirene;

// Sous-ensemble de la réponse de GET /search de l'API Recherche d'entreprises
// (https://recherche-entreprises.api.gouv.fr/docs/). Les champs absents restent null.

internal sealed record SearchResponse(
    [property: JsonPropertyName("results")] IReadOnlyList<CompanyResult>? Results,
    [property: JsonPropertyName("total_results")] int TotalResults);

internal sealed record CompanyResult(
    [property: JsonPropertyName("siren")] string? Siren,
    [property: JsonPropertyName("nom_complet")] string? FullName,
    [property: JsonPropertyName("nom_raison_sociale")] string? LegalName,
    [property: JsonPropertyName("activite_principale")] string? MainActivity,
    [property: JsonPropertyName("etat_administratif")] string? AdministrativeState,
    [property: JsonPropertyName("siege")] Establishment? HeadOffice);

internal sealed record Establishment(
    [property: JsonPropertyName("siret")] string? Siret,
    [property: JsonPropertyName("numero_voie")] string? StreetNumber,
    [property: JsonPropertyName("indice_repetition")] string? RepetitionIndex,
    [property: JsonPropertyName("type_voie")] string? StreetType,
    [property: JsonPropertyName("libelle_voie")] string? StreetName,
    [property: JsonPropertyName("complement_adresse")] string? AddressComplement,
    [property: JsonPropertyName("distribution_speciale")] string? SpecialDistribution,
    [property: JsonPropertyName("code_postal")] string? PostalCode,
    [property: JsonPropertyName("libelle_commune")] string? City,
    [property: JsonPropertyName("libelle_commune_etranger")] string? ForeignCity,
    [property: JsonPropertyName("code_pays_etranger")] string? ForeignCountryCode,
    [property: JsonPropertyName("cedex")] string? Cedex,
    [property: JsonPropertyName("libelle_cedex")] string? CedexLabel,
    [property: JsonPropertyName("etat_administratif")] string? AdministrativeState);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(SearchResponse))]
internal sealed partial class SireneJsonContext : JsonSerializerContext;
