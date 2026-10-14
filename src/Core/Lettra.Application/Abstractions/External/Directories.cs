using Lettra.Contracts.Directory;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.External;

/// <summary>
/// Annuaire des entreprises françaises (SIRENE, via l'API Recherche d'entreprises de l'État).
/// Seule la saisie de recherche quitte le dossier (un nom ou un SIREN, données publiques) ; aucune
/// donnée du dossier n'y est envoyée. Hors ligne ou en panne : <c>Directory.Unavailable</c>, la saisie
/// manuelle reste toujours possible.
/// </summary>
public interface ICompanyDirectory
{
    Task<Result<CompanySearchResponse>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);
}

/// <summary>Vérification d'un numéro de TVA intracommunautaire auprès de VIES (Commission européenne).</summary>
public interface IVatNumberChecker
{
    Task<Result<VatNumberCheckResponse>> CheckAsync(VatNumber vatNumber, CancellationToken cancellationToken);
}

/// <summary>Codes d'erreur des annuaires externes.</summary>
public static class DirectoryErrors
{
    public const string QueryTooShortCode = "Directory.QueryTooShort";
    public const string UnavailableCode = "Directory.Unavailable";
    public const string VatCountryNotSupportedCode = "Directory.VatCountryNotSupported";

    public static Error QueryTooShort => Error.Validation(QueryTooShortCode, "Query");

    /// <summary>Service injoignable, en panne ou saturé : rien n'est perdu, l'utilisateur saisit à la main.</summary>
    public static Error Unavailable => new(UnavailableCode, ErrorKind.Unexpected);

    public static Error VatCountryNotSupported => Error.Validation(VatCountryNotSupportedCode, "VatNumber");
}
