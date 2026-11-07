namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : dossiers, cabinets et numérotation.</summary>
public static partial class ErrorCodes
{
    public static class Tenancy
    {
        public const string DossierRequired = "Dossier.Required";
        public const string DossierNotFound = "Dossier.NotFound";
        public const string DossierMismatch = "Dossier.Mismatch";
        public const string DossierNameRequired = "Dossier.NameRequired";
        public const string DossierNameTooLong = "Dossier.NameTooLong";
        public const string OrganisationNotFound = "Organisation.NotFound";
        public const string NoCapacity = "Dossier.NoCapacity";
        public const string ProvisioningFailed = "Dossier.ProvisioningFailed";
        public const string DossierDeclared = "Dossier.Declared";
        public const string ErasureNotCancellable = "Dossier.ErasureNotCancellable";
        public const string PassphraseTooShort = "Dossier.PassphraseTooShort";
        public const string PackageUnreadable = "Dossier.PackageUnreadable";
        public const string PackageTooNew = "Dossier.PackageTooNew";
        public const string DossierAlreadyExists = "Dossier.AlreadyExists";
        public const string TransferUnavailable = "Dossier.TransferUnavailable";
    }

    public static class Numbering
    {
        public const string SeriesNotFound = "Numbering.SeriesNotFound";
        public const string Contention = "Numbering.Contention";
    }

    public static class CompanyProfile
    {
        public const string NameRequired = "CompanyProfile.NameRequired";
        public const string NameTooLong = "CompanyProfile.NameTooLong";
        public const string PaymentDaysInvalid = "CompanyProfile.PaymentDaysInvalid";
        public const string NotFound = "CompanyProfile.NotFound";
    }
}
