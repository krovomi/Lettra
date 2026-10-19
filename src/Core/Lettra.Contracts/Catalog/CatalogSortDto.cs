using System.Text.Json.Serialization;

namespace Lettra.Contracts.Catalog;

/// <summary>
/// Colonne de tri de la liste des articles (paramètre de requête <c>sort</c>). Par défaut la référence, de A à Z ; la
/// référence départage toujours les égalités.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CatalogSortDto>))]
public enum CatalogSortDto
{
    Reference = 1,
    Designation = 2,
    UnitPrice = 3,
}
