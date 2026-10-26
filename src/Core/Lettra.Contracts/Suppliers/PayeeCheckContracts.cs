using System.Text.Json.Serialization;

namespace Lettra.Contracts.Suppliers;

/// <summary>Réponse d'une vérification du bénéficiaire : correspond, correspond presque, ne correspond pas, impossible à vérifier.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayeeMatchDto>))]
public enum PayeeMatchDto
{
    Match = 1,
    CloseMatch = 2,
    NoMatch = 3,
    NotPossible = 4,
}

/// <summary>Tri des bénéficiaires : par nom, ou les plus à risque d'abord.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayeeCheckSortDto>))]
public enum PayeeCheckSortDto
{
    Name = 1,
    Risk = 2,
}

/// <summary>La dernière vérification du bénéficiaire d'un fournisseur. <paramref name="SuggestedName"/> : le titulaire proposé par la banque quand le nom correspond presque.</summary>
public sealed record PayeeCheckResponse(PayeeMatchDto Result, DateTimeOffset CheckedAt, string? SuggestedName);

/// <summary>
/// Un fournisseur payable par virement, avec ses contrôles avant paiement : nom du titulaire comparé au nom du fournisseur,
/// dernière vérification du bénéficiaire, changement d'IBAN en attente, attestation de vigilance exigée mais absente, périmée
/// ou non vérifiée (<c>AttestationDue</c>). <c>ToReview</c> : au moins un contrôle n'est pas vert.
/// </summary>
public sealed record PayeeCheckRowResponse(
    Guid SupplierId,
    string Name,
    string? AccountHolderName,
    string MaskedIban,
    PayeeMatchDto? HolderNameMatch,
    PayeeCheckResponse? Check,
    bool IbanChangePending,
    bool ToReview,
    bool AttestationDue = false);
