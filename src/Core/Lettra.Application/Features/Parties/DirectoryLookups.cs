using Lettra.Application.Abstractions.External;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Common;
using Lettra.Contracts.Directory;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Parties;

/// <summary>
/// Chercher une entreprise dans l'annuaire SIRENE (nom, SIREN ou SIRET) pour pré-remplir un client ou un
/// fournisseur. Permission de création de tiers : la recherche ne sert qu'à la saisie.
/// </summary>
public sealed record SearchCompaniesQuery(DossierId Dossier, string Query, int MaxResults = 10)
    : IQuery<Result<CompanySearchResponse>>, IRequirePermission
{
    public const int MinQueryLength = 3;
    public const int MaxQueryLength = 100;
    public const int MaxMaxResults = 25;

    public Permission Permission => Permission.TiersCreer;
}

/// <summary>Vérifier un numéro de TVA intracommunautaire auprès de VIES.</summary>
public sealed record CheckVatNumberQuery(DossierId Dossier, string VatNumber)
    : IQuery<Result<VatNumberCheckResponse>>, IRequirePermission
{
    public Permission Permission => Permission.TiersCreer;
}

internal sealed class SearchCompaniesValidator : IValidator<SearchCompaniesQuery>
{
    public IReadOnlyList<Error> Validate(SearchCompaniesQuery instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        List<Error> errors = [];
        string query = (instance.Query ?? string.Empty).Trim();
        if (query.Length < SearchCompaniesQuery.MinQueryLength)
        {
            errors.Add(DirectoryErrors.QueryTooShort);
        }
        else if (query.Length > SearchCompaniesQuery.MaxQueryLength)
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.TooLong, "Query"));
        }

        if (instance.MaxResults is < 1 or > SearchCompaniesQuery.MaxMaxResults)
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.Invalid, nameof(instance.MaxResults)));
        }

        return errors;
    }
}

internal sealed class SearchCompaniesHandler(ICompanyDirectory directory)
    : IQueryHandler<SearchCompaniesQuery, Result<CompanySearchResponse>>
{
    public Task<Result<CompanySearchResponse>> HandleAsync(SearchCompaniesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return directory.SearchAsync(query.Query.Trim(), query.MaxResults, cancellationToken);
    }
}

internal sealed class CheckVatNumberHandler(IVatNumberChecker checker)
    : IQueryHandler<CheckVatNumberQuery, Result<VatNumberCheckResponse>>
{
    // Pays interrogeables dans VIES (préfixes TVA : la Grèce est « EL », l'Irlande du Nord « XI »).
    private static readonly HashSet<string> ViesPrefixes = new(StringComparer.Ordinal)
    {
        "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "EL", "ES", "FI", "FR", "HR", "HU", "IE",
        "IT", "LT", "LU", "LV", "MT", "NL", "PL", "PT", "RO", "SE", "SI", "SK", "XI",
    };

    public async Task<Result<VatNumberCheckResponse>> HandleAsync(CheckVatNumberQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Result<VatNumber> vat = VatNumber.Create(query.VatNumber);
        if (vat.IsFailure)
        {
            return Result<VatNumberCheckResponse>.Failure(vat.Errors.Select(e => e.For(nameof(query.VatNumber))).ToList());
        }

        return ViesPrefixes.Contains(vat.Value.CountryPrefix)
            ? await checker.CheckAsync(vat.Value, cancellationToken).ConfigureAwait(false)
            : DirectoryErrors.VatCountryNotSupported;
    }
}
