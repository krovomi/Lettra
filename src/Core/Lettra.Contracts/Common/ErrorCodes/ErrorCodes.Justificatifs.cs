namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : pièces justificatives des dépenses du relevé (module Exports).</summary>
public static partial class ErrorCodes
{
    public static class Receipt
    {
        public const string LineNotDebit = "Receipt.LineNotDebit";
        public const string AlreadyReceived = "Receipt.AlreadyReceived";
        public const string FileRequired = "Receipt.FileRequired";
        public const string FileTooLarge = "Receipt.FileTooLarge";
        public const string FileTypeInvalid = "Receipt.FileTypeInvalid";
        public const string LinesRequired = "Receipt.LinesRequired";
        public const string EmailInvalid = "Receipt.EmailInvalid";
        public const string NoteTooLong = "Receipt.NoteTooLong";
        public const string NotFound = "Receipt.NotFound";
    }
}
