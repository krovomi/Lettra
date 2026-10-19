using System.Text.Json.Serialization;

namespace Lettra.Contracts.Banking;

/// <summary>
/// Colonne de tri de la liste des encaissements (paramètre de requête <c>sort</c>). Par défaut la date, la plus récente
/// en tête ; le numéro départage toujours les égalités.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentSortDto>))]
public enum PaymentSortDto
{
    Date = 1,
    Number = 2,
    Customer = 3,
    Amount = 4,
}
