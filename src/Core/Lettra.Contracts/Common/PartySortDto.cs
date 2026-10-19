using System.Text.Json.Serialization;

namespace Lettra.Contracts.Common;

/// <summary>
/// Colonne de tri des listes de clients et de fournisseurs (paramètre de requête <c>sort</c>). Par défaut le nom, de A à
/// Z ; le nom puis l'identifiant départagent toujours les égalités.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PartySortDto>))]
public enum PartySortDto
{
    Name = 1,
    City = 2,
}
