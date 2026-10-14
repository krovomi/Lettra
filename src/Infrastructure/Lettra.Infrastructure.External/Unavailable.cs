using Lettra.Application.Abstractions.External;
using Lettra.Contracts.Directory;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Infrastructure.External;

/// <summary>Annuaires coupés par configuration : réponse immédiate, aucune connexion tentée.</summary>
internal sealed class DisabledDirectories : ICompanyDirectory, IVatNumberChecker
{
    public Task<Result<CompanySearchResponse>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken) =>
        Task.FromResult<Result<CompanySearchResponse>>(DirectoryErrors.Unavailable);

    public Task<Result<VatNumberCheckResponse>> CheckAsync(VatNumber vatNumber, CancellationToken cancellationToken) =>
        Task.FromResult<Result<VatNumberCheckResponse>>(DirectoryErrors.Unavailable);
}
