using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Suppliers;

/// <summary>Où en est la vérification d'un changement d'IBAN.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IbanChangeStatusDto>))]
public enum IbanChangeStatusDto
{
    Pending = 1,
    Confirmed = 2,
    Rejected = 3,
    Superseded = 4,
}

/// <summary>Le canal par lequel le changement a été vérifié, distinct de celui de la demande.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IbanVerificationMethodDto>))]
public enum IbanVerificationMethodDto
{
    KnownPhoneNumber = 1,
    SignedLetter = 2,
    InPerson = 3,
    Other = 4,
}

/// <summary>Tri de la liste des changements d'IBAN.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IbanChangeSortDto>))]
public enum IbanChangeSortDto
{
    ChangedAt = 1,
    Supplier = 2,
}

/// <summary>
/// Un changement d'IBAN d'un fournisseur, à vérifier avant tout paiement. Les IBAN sont masqués (pays, clé et quatre
/// derniers caractères) ; <paramref name="CountryChanged"/> signale un compte qui change de pays.
/// </summary>
public sealed record IbanChangeResponse(
    Guid Id,
    Guid SupplierId,
    string SupplierName,
    string? PreviousIban,
    string NewIban,
    bool CountryChanged,
    DateTimeOffset ChangedAt,
    IbanChangeStatusDto Status,
    DateTimeOffset? DecidedAt,
    IbanVerificationMethodDto? Method,
    string? Note)
{
    /// <summary>Version de concurrence optimiste, à renvoyer pour décider.</summary>
    public long Version { get; init; }
}

/// <summary>Confirmer un nouvel IBAN après l'avoir vérifié par un autre canal que la demande.</summary>
public sealed record ConfirmIbanChangeRequest
{
    [Required(ErrorMessage = ErrorCodes.IbanChange.MethodRequired)]
    public IbanVerificationMethodDto? Method { get; set; }

    [StringLength(500, ErrorMessage = ErrorCodes.IbanChange.NoteTooLong)]
    public string? Note { get; set; }

    public long? ExpectedVersion { get; set; }
}

/// <summary>Refuser un nouvel IBAN jugé suspect ; la note dit pourquoi.</summary>
public sealed record RejectIbanChangeRequest
{
    [Required(ErrorMessage = ErrorCodes.IbanChange.NoteRequired)]
    [StringLength(500, ErrorMessage = ErrorCodes.IbanChange.NoteTooLong)]
    public string? Note { get; set; }

    public long? ExpectedVersion { get; set; }
}
