using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Banking;

/// <summary>Justificatif d'une dépense du relevé : manquant, réclamé au client, déposé, ou non attendu.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReceiptStatusDto>))]
public enum ReceiptStatusDto
{
    Missing = 0,
    Requested = 1,
    Received = 2,
    NotNeeded = 3,
}

/// <summary>
/// Montant TTC, date et TVA d'une pièce, confirmés par celui qui la dépose. <c>ReadOnDevice</c> : proposés par la lecture
/// de la photo sur l'appareil, puis relus.
/// </summary>
public sealed record ReceiptFiguresDto
{
    public decimal AmountInclTax { get; set; }

    public DateOnly Date { get; set; }

    public decimal VatAmount { get; set; }

    public bool ReadOnDevice { get; set; }
}

/// <summary>La pièce déposée : nom, type, taille, date de dépôt, et ce qu'elle dit si celui qui l'a déposée l'a confirmé.</summary>
public sealed record ReceiptFileResponse(Guid Id, string FileName, string ContentType, int Size, DateTimeOffset AddedAt, ReceiptFiguresDto? Figures = null);

/// <summary>Une dépense du relevé et son justificatif.</summary>
public sealed record ReceiptLineResponse(
    Guid LineId,
    DateOnly Date,
    decimal Amount,
    string Label,
    string? CounterpartyName,
    ReceiptStatusDto Status,
    DateTimeOffset? RequestedAt,
    string? RequestedTo,
    int Requests,
    ReceiptFileResponse? Receipt,
    long Version);

/// <summary>
/// Les dépenses du relevé et leurs justificatifs, les plus récentes en tête, avec les totaux des pièces manquantes ou
/// réclamées, la dernière adresse utilisée et si l'installation sait envoyer des e-mails.
/// </summary>
public sealed record ReceiptsResponse(
    IReadOnlyList<ReceiptLineResponse> Lines,
    int MissingCount,
    decimal MissingAmount,
    int RequestedCount,
    int ReceivedCount,
    string? LastRecipient,
    bool EmailConfigured);

/// <summary>Réclamer au client les pièces de plusieurs dépenses, par un seul e-mail.</summary>
public sealed record RequestReceiptsRequest
{
    public IReadOnlyList<Guid> LineIds { get; set; } = [];

    [Required(ErrorMessage = ErrorCodes.Receipt.EmailInvalid)]
    [EmailAddress(ErrorMessage = ErrorCodes.Receipt.EmailInvalid)]
    [StringLength(254, ErrorMessage = ErrorCodes.Receipt.EmailInvalid)]
    public string Email { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = ErrorCodes.Receipt.NoteTooLong)]
    public string? Note { get; set; }
}

/// <summary>Pièces réclamées et sort de l'e-mail (les demandes restent notées même si l'e-mail n'est pas parti).</summary>
public sealed record RequestReceiptsResponse(int Requested, InvitationEmailStatusDto Email);

/// <summary>Aucune pièce n'est attendue pour cette dépense (le relevé suffit), ou elle l'est de nouveau.</summary>
public sealed record SetReceiptNotNeededRequest
{
    public bool NotNeeded { get; set; } = true;
}

/// <summary>
/// Dépôt d'une pièce : une photo (JPEG, PNG, WebP, HEIC) ou un PDF, en base 64, 5 Mo au plus, avec le montant, la date
/// et la TVA confirmés.
/// </summary>
public sealed record UploadReceiptRequest
{
    [StringLength(200, ErrorMessage = ErrorCodes.Receipt.FileRequired)]
    public string FileName { get; set; } = string.Empty;

    public string ContentBase64 { get; set; } = string.Empty;

    /// <summary>Montant, date et TVA confirmés ; facultatifs (un dépôt sans eux reste possible par l'API).</summary>
    public ReceiptFiguresDto? Figures { get; set; }
}
