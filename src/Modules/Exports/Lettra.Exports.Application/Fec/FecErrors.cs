using Lettra.Contracts.Common;
using Lettra.Domain.Common;

namespace Lettra.Exports.Application.Fec;

/// <summary>Refus d'un export ou d'une validation de FEC (les constats du validateur sont dans le rapport).</summary>
public static class FecErrors
{
    public const string PeriodInvalidCode = ErrorCodes.Fec.PeriodInvalid;
    public const string PeriodTooLongCode = ErrorCodes.Fec.PeriodTooLong;
    public const string SirenMissingCode = ErrorCodes.Fec.SirenMissing;
    public const string ExportInvalidCode = ErrorCodes.Fec.ExportInvalid;
    public const string FileTooLargeCode = ErrorCodes.Fec.FileTooLarge;
    public const string FileNameInvalidCode = ErrorCodes.Fec.FileNameInvalid;

    public static Error PeriodInvalid => Error.Validation(PeriodInvalidCode, "To");

    public static Error PeriodTooLong => Error.Validation(PeriodTooLongCode, "To");

    /// <summary>Le nom du fichier FEC commence par le SIREN : la fiche entreprise doit le porter.</summary>
    public static Error SirenMissing => Error.Conflict(SirenMissingCode);

    /// <summary>Le fichier produit ne passe pas le validateur : il n'est pas remis (le rapport est journalisé par codes).</summary>
    public static Error ExportInvalid => Error.Conflict(ExportInvalidCode);

    public static Error FileTooLarge => Error.Validation(FileTooLargeCode, "Content");

    public static Error FileNameInvalid => Error.Validation(FileNameInvalidCode, "FileName");
}
