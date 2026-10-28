using Lettra.Domain.Common;

namespace Lettra.Domain.Banking;

/// <summary>Justificatif d'une opération débitrice du relevé (valeurs identiques à <c>ReceiptStatusDto</c>).</summary>
public enum ReceiptStatus
{
    /// <summary>Aucun justificatif ni demande : la pièce manque.</summary>
    Missing = 0,

    /// <summary>La pièce a été réclamée au client, qui ne l'a pas encore déposée.</summary>
    Requested = 1,

    /// <summary>La pièce est déposée.</summary>
    Received = 2,

    /// <summary>Aucune pièce n'est attendue : le relevé suffit (frais bancaires, virement interne).</summary>
    NotNeeded = 3,
}

/// <summary>Codes d'erreur des pièces justificatives.</summary>
public static class ReceiptErrors
{
    public const string LineNotDebitCode = "Receipt.LineNotDebit";
    public const string AlreadyReceivedCode = "Receipt.AlreadyReceived";
    public const string FileRequiredCode = "Receipt.FileRequired";
    public const string FileTooLargeCode = "Receipt.FileTooLarge";
    public const string FileTypeInvalidCode = "Receipt.FileTypeInvalid";
    public const string LinesRequiredCode = "Receipt.LinesRequired";
    public const string EmailInvalidCode = "Receipt.EmailInvalid";
    public const string NoteTooLongCode = "Receipt.NoteTooLong";
    public const string NotFoundCode = "Receipt.NotFound";
    public const string AmountInvalidCode = "Receipt.AmountInvalid";
    public const string VatInvalidCode = "Receipt.VatInvalid";
    public const string DateInvalidCode = "Receipt.DateInvalid";

    /// <summary>Seule une opération débitrice (une dépense) appelle une pièce justificative.</summary>
    public static Error LineNotDebit => Error.Validation(LineNotDebitCode, "BankLineId");

    /// <summary>La pièce est déjà déposée : il n'y a plus rien à réclamer.</summary>
    public static Error AlreadyReceived => Error.Conflict(AlreadyReceivedCode);

    public static Error FileRequired => Error.Validation(FileRequiredCode, "File");

    public static Error FileTooLarge => Error.Validation(FileTooLargeCode, "File");

    /// <summary>Le fichier n'est ni une photo (JPEG, PNG, WebP, HEIC) ni un PDF, d'après son contenu.</summary>
    public static Error FileTypeInvalid => Error.Validation(FileTypeInvalidCode, "File");

    public static Error LinesRequired => Error.Validation(LinesRequiredCode, "LineIds");

    public static Error EmailInvalid => Error.Validation(EmailInvalidCode, "Email");

    public static Error NoteTooLong => Error.Validation(NoteTooLongCode, "Note");

    public static Error NotFound => Error.NotFound(NotFoundCode);

    /// <summary>Le montant TTC confirmé est positif, au centime près.</summary>
    public static Error AmountInvalid => Error.Validation(AmountInvalidCode, "Figures.AmountInclTax");

    /// <summary>La TVA confirmée est positive ou nulle, au centime près, et ne dépasse pas le montant TTC.</summary>
    public static Error VatInvalid => Error.Validation(VatInvalidCode, "Figures.VatAmount");

    /// <summary>La date de la pièce n'est ni dans le futur ni avant l'an 2000.</summary>
    public static Error DateInvalid => Error.Validation(DateInvalidCode, "Figures.Date");
}

/// <summary>
/// La pièce justificative déposée pour une opération du relevé : une photo prise au téléphone ou un PDF. Le type se lit
/// dans les premiers octets du fichier, jamais dans son nom ni dans ce que déclare le navigateur.
/// </summary>
public sealed class BankLineReceipt : Entity<Guid>
{
    /// <summary>Une photo de téléphone tient sous 5 Mo ; au-delà, l'écran demande une photo moins lourde.</summary>
    public const int MaxContentLength = 5 * 1024 * 1024;

    public const int MaxFileNameLength = 200;

    private BankLineReceipt(Guid id)
        : base(id)
    {
        FileName = string.Empty;
        ContentType = string.Empty;
        Content = [];
    }

    public Guid LineId { get; private set; }

    public string FileName { get; private set; }

    public string ContentType { get; private set; }

    public byte[] Content { get; private set; }

    public int Size { get; private set; }

    public Guid AddedBy { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    /// <summary>Montant, date et TVA confirmés par celui qui dépose la pièce ; vides s'il ne les a pas donnés.</summary>
    public ReceiptFigures? Figures { get; private set; }

    public static Result<BankLineReceipt> Create(Guid id, Guid lineId, string? fileName, byte[]? content, Guid userId, DateTimeOffset now)
    {
        if (content is null || content.Length == 0)
        {
            return ReceiptErrors.FileRequired;
        }

        if (content.Length > MaxContentLength)
        {
            return ReceiptErrors.FileTooLarge;
        }

        if (ReceiptFiles.Sniff(content) is not { } type)
        {
            return ReceiptErrors.FileTypeInvalid;
        }

        string name = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            name = "justificatif" + type.Extension;
        }

        return new BankLineReceipt(id)
        {
            LineId = lineId,
            FileName = name.Length > MaxFileNameLength ? name[..MaxFileNameLength] : name,
            ContentType = type.ContentType,
            Content = content,
            Size = content.Length,
            AddedBy = userId,
            AddedAt = now,
        };
    }

    /// <summary>Note le montant, la date et la TVA confirmés par celui qui dépose la pièce.</summary>
    public void Confirm(ReceiptFigures figures) => Figures = figures ?? throw new ArgumentNullException(nameof(figures));
}

/// <summary>
/// Ce que dit la pièce, confirmé par l'utilisateur : montant TTC, date et TVA. <paramref name="ReadOnDevice"/> : les
/// valeurs ont été proposées par la lecture de la photo sur l'appareil (aucune image ni aucun texte lu ne quitte
/// l'appareil pour cela), puis relues et confirmées.
/// </summary>
public sealed record ReceiptFigures(decimal AmountInclTax, DateOnly Date, decimal VatAmount, bool ReadOnDevice)
{
    public const decimal MaxAmount = 100_000_000m;

    public static readonly DateOnly MinDate = new(2000, 1, 1);

    public static Result<ReceiptFigures> Create(decimal amountInclTax, DateOnly date, decimal vatAmount, bool readOnDevice, DateOnly today)
    {
        List<Error> errors = [];
        if (amountInclTax is <= 0m or > MaxAmount || decimal.Round(amountInclTax, 2) != amountInclTax)
        {
            errors.Add(ReceiptErrors.AmountInvalid);
        }

        if (vatAmount < 0m || vatAmount > amountInclTax || decimal.Round(vatAmount, 2) != vatAmount)
        {
            errors.Add(ReceiptErrors.VatInvalid);
        }

        if (date > today || date < MinDate)
        {
            errors.Add(ReceiptErrors.DateInvalid);
        }

        return errors.Count > 0 ? Result<ReceiptFigures>.Failure(errors) : new ReceiptFigures(amountInclTax, date, vatAmount, readOnDevice);
    }
}

/// <summary>Les types de fichiers acceptés comme justificatifs, reconnus à leur signature.</summary>
public static class ReceiptFiles
{
    /// <summary>Un type reconnu : son type MIME et l'extension proposée au téléchargement.</summary>
    public sealed record FileType(string ContentType, string Extension);

    private static readonly FileType Jpeg = new("image/jpeg", ".jpg");
    private static readonly FileType Png = new("image/png", ".png");
    private static readonly FileType Webp = new("image/webp", ".webp");
    private static readonly FileType Heic = new("image/heic", ".heic");
    private static readonly FileType Pdf = new("application/pdf", ".pdf");

    /// <summary>Le type du fichier d'après ses premiers octets, ou <c>null</c> s'il n'est pas accepté.</summary>
    public static FileType? Sniff(ReadOnlySpan<byte> content) => content switch
    {
        [0xFF, 0xD8, 0xFF, ..] => Jpeg,
        [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, ..] => Png,
        [(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', ..] => Pdf,
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => Webp,
        [_, _, _, _, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c' or (byte)'x', ..] => Heic,
        [_, _, _, _, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'m', (byte)'i', (byte)'f', (byte)'1', ..] => Heic,
        _ => null,
    };
}
