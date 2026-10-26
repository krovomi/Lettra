using Lettra.Domain.Vigilance;

namespace Lettra.Application.Abstractions.Cabinet;

/// <summary>La fiche anti-blanchiment du client, dans la base du dossier : elle suit le dossier (export, sauvegarde).</summary>
public interface IVigilanceStore
{
    /// <summary>La fiche suivie (modifiable), ou <c>null</c> tant qu'elle n'est pas saisie.</summary>
    Task<ClientDueDiligence?> FindAsync(CancellationToken cancellationToken);

    Task AddAsync(ClientDueDiligence file, CancellationToken cancellationToken);
}
