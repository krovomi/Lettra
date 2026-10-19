using System.Text.Json.Serialization;

namespace Lettra.Contracts.Common;

/// <summary>Sens d'un tri de liste, porté par le paramètre de requête <c>direction</c> (<c>Ascending</c>, <c>Descending</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SortDirectionDto>))]
public enum SortDirectionDto
{
    Ascending = 1,
    Descending = 2,
}
