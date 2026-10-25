using Lettra.Application.Abstractions.External;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Directory;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Customers;

/// <summary>
/// Contrôler les clients entreprises du dossier dans l'annuaire SIRENE : ceux qui sont cessés (radiés) ou dont le SIREN
/// est inconnu. Seul le SIREN part vers l'annuaire (donnée publique) ; le nom et le reste de la fiche restent au dossier.
/// Les clients sans SIREN et les particuliers ne sont pas contrôlés.
/// </summary>
public sealed record CheckCustomersInDirectoryQuery(DossierId Dossier)
    : IQuery<Result<CustomerDirectoryCheckResponse>>, IRequirePermission
{
    /// <summary>Plafond d'un contrôle, pour ménager l'annuaire public (quelques requêtes par seconde).</summary>
    public const int MaxCustomers = 500;

    /// <summary>Réponses manquées d'affilée au-delà desquelles l'annuaire est tenu pour indisponible.</summary>
    public const int MaxConsecutiveFailures = 3;

    public Permission Permission => Permission.TiersConsulter;
}

internal sealed class CheckCustomersInDirectoryHandler(ICustomerReadStore customers, ICompanyDirectory directory)
    : IQueryHandler<CheckCustomersInDirectoryQuery, Result<CustomerDirectoryCheckResponse>>
{
    public async Task<Result<CustomerDirectoryCheckResponse>> HandleAsync(CheckCustomersInDirectoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        List<CustomerSummary> companies = await CompaniesAsync(cancellationToken).ConfigureAwait(false);

        int checkedCount = 0;
        int failures = 0;
        int consecutiveFailures = 0;
        Error? lastError = null;
        List<CustomerDirectoryFinding> findings = [];
        foreach (CustomerSummary customer in companies)
        {
            Result<CompanySearchResponse> found = await directory.SearchAsync(customer.Siren!, 1, cancellationToken).ConfigureAwait(false);
            if (found.IsFailure)
            {
                // Une réponse manquée ne bloque pas les autres clients ; un annuaire qui ne répond plus arrête le contrôle.
                failures++;
                lastError = found.Errors[0];
                if (++consecutiveFailures >= CheckCustomersInDirectoryQuery.MaxConsecutiveFailures)
                {
                    break;
                }

                continue;
            }

            consecutiveFailures = 0;
            checkedCount++;
            CompanySearchResult? company = found.Value.Items.FirstOrDefault(c => c.Siren == customer.Siren);
            DirectoryStatusDto status = company is null ? DirectoryStatusDto.NotFound
                : company.IsActive ? DirectoryStatusDto.Active : DirectoryStatusDto.Ceased;
            if (status != DirectoryStatusDto.Active)
            {
                findings.Add(new CustomerDirectoryFinding(customer.Id, customer.Name, customer.Siren!, status));
            }
        }

        if (checkedCount == 0 && lastError is not null)
        {
            return lastError;
        }

        bool complete = failures == 0;
        return new CustomerDirectoryCheckResponse(checkedCount, [.. findings.OrderBy(f => f.Status).ThenBy(f => f.Name, StringComparer.CurrentCulture)],
            complete);
    }

    private async Task<List<CustomerSummary>> CompaniesAsync(CancellationToken cancellationToken)
    {
        List<CustomerSummary> result = [];
        for (int page = 1; result.Count < CheckCustomersInDirectoryQuery.MaxCustomers; page++)
        {
            PagedResponse<CustomerSummary> batch = await customers.ListAsync(
                new PartyListFilter(null, page, Paging.MaxPageSize, Kind: CustomerKind.Company), cancellationToken).ConfigureAwait(false);
            result.AddRange(batch.Items.Where(c => !string.IsNullOrWhiteSpace(c.Siren)));
            if (batch.Items.Count < Paging.MaxPageSize)
            {
                break;
            }
        }

        return [.. result.Take(CheckCustomersInDirectoryQuery.MaxCustomers)];
    }
}
