using Lettra.Domain.SharedKernel;
using Lettra.Domain.Vigilance;

namespace Lettra.Application.Abstractions.Persistence;

/// <summary>Un client ou un fournisseur actif et sa dernière vérification (vide s'il n'a jamais été vérifié).</summary>
public sealed record ThirdPartyEntry(ThirdPartyKind Kind, Guid PartyId, string Name, Siren? Siren, ThirdPartyCheck? Check);

/// <summary>Les vérifications des tiers auprès des registres, dans la base du dossier.</summary>
public interface IThirdPartyCheckStore
{
    /// <summary>La dernière vérification d'un tiers (modifiable), ou <c>null</c> s'il n'a jamais été vérifié.</summary>
    Task<ThirdPartyCheck?> FindAsync(Guid partyId, CancellationToken cancellationToken);

    Task AddAsync(ThirdPartyCheck check, CancellationToken cancellationToken);

    /// <summary>Les clients et fournisseurs non archivés et leur vérification ; un dossier en compte quelques centaines.</summary>
    Task<IReadOnlyList<ThirdPartyEntry>> ListAsync(CancellationToken cancellationToken);
}
