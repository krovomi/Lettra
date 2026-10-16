using Lettra.Application.Abstractions.Identity;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Identity.Local;

/// <summary>Cabinet du poste lu dans la configuration, quand aucun registre local des dossiers n'est composé.</summary>
internal sealed class ConfiguredLocalCabinet(IOptions<LocalIdentityOptions> options) : ILocalCabinet
{
    public ValueTask<OrganisationId> GetAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new OrganisationId(options.Value.CabinetId));
}
