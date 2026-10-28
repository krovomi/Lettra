using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Domain.Vigilance;

namespace Lettra.Application.Abstractions.External;

/// <summary>Un bénéficiaire effectif inscrit au registre ; <paramref name="SharePercent"/> vide pour un contrôle ou un représentant légal.</summary>
public sealed record RegisteredOwner(string Name, OwnershipKind Kind, decimal? SharePercent);

/// <summary>Une personne ou une entité inscrite au registre national des gels des avoirs, et ses autres noms.</summary>
public sealed record FrozenEntry(string Name, IReadOnlyList<string> Aliases);

/// <summary>Une annonce du BODACC sur une entreprise.</summary>
public sealed record LegalNotice(LegalNoticeKind Kind, DateOnly PublishedOn, string Label);

/// <summary>
/// Registre des bénéficiaires effectifs (INPI, registre national des entreprises), ouvert aux professionnels assujettis à
/// la lutte contre le blanchiment. Seul le SIREN demandé quitte le dossier. Un SIREN inconnu donne une liste vide ; hors
/// ligne : <c>Directory.Unavailable</c>.
/// </summary>
public interface IBeneficialOwnerRegister
{
    Task<Result<IReadOnlyList<RegisteredOwner>>> FindAsync(Siren siren, CancellationToken cancellationToken);
}

/// <summary>
/// Registre national des gels des avoirs (DG Trésor), publié en entier : la liste est téléchargée et le criblage se fait
/// dans Lettra (<see cref="AssetFreezeNames"/>). Aucun nom du dossier ne quitte donc Lettra.
/// </summary>
public interface IAssetFreezeRegister
{
    Task<Result<IReadOnlyList<FrozenEntry>>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Annonces civiles et commerciales (BODACC, données ouvertes de la DILA) : procédures collectives et radiations d'une
/// entreprise, les plus récentes en tête. Seul le SIREN quitte le dossier.
/// </summary>
public interface ILegalNoticeFeed
{
    Task<Result<IReadOnlyList<LegalNotice>>> ListAsync(Siren siren, CancellationToken cancellationToken);
}

/// <summary>
/// Vérification de l'authenticité d'une attestation de vigilance auprès de l'URSSAF, par son code de sécurité et le SIREN
/// du fournisseur.
/// </summary>
public interface IUrssafAttestationCheck
{
    Task<Result<UrssafAnswer>> VerifyAsync(Siren siren, string securityCode, CancellationToken cancellationToken);
}

/// <summary>
/// Sans adaptateur branché, les registres ne répondent pas : <c>Directory.Unavailable</c>, la saisie manuelle reste
/// possible. Lettra.Infrastructure.External branche des registres simulés.
/// </summary>
internal sealed class NoOfficialRegisters : IBeneficialOwnerRegister, IAssetFreezeRegister, ILegalNoticeFeed, IUrssafAttestationCheck
{
    public Task<Result<IReadOnlyList<RegisteredOwner>>> FindAsync(Siren siren, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<RegisteredOwner>>>(DirectoryErrors.Unavailable);

    public Task<Result<IReadOnlyList<FrozenEntry>>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<FrozenEntry>>>(DirectoryErrors.Unavailable);

    public Task<Result<IReadOnlyList<LegalNotice>>> ListAsync(Siren siren, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<LegalNotice>>>(DirectoryErrors.Unavailable);

    public Task<Result<UrssafAnswer>> VerifyAsync(Siren siren, string securityCode, CancellationToken cancellationToken) =>
        Task.FromResult<Result<UrssafAnswer>>(DirectoryErrors.Unavailable);
}
