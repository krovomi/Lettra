using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Identity;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Cabinet;

/// <summary>
/// Lit le nom du cabinet de l'utilisateur et les noms de ses membres dans le registre : la revue y lit qui a coché,
/// la mission qui a passé du temps et au nom de quel cabinet la lettre est écrite.
/// </summary>
internal sealed class CabinetPeopleReader(ICurrentUser user, IAuthorizationDirectory directory, IAuthorizationRegistry registry)
{
    public async Task<CabinetPeople> ReadAsync(CancellationToken cancellationToken)
    {
        UserAuthorizationProfile? profile = await directory.FindUserAsync(user.UserId, cancellationToken).ConfigureAwait(false);
        if (profile?.Cabinet is not OrganisationId cabinet)
        {
            return CabinetPeople.None;
        }

        CabinetRegistry? read = await registry.ReadAsync(cabinet, cancellationToken).ConfigureAwait(false);
        return read is null
            ? CabinetPeople.None
            : new CabinetPeople(
                read.CabinetName,
                read.People.Where(p => !string.IsNullOrWhiteSpace(p.DisplayName)).ToDictionary(p => p.Id, p => p.DisplayName!));
    }
}
