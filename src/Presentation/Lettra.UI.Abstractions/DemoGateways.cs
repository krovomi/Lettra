using Lettra.Contracts.Common;
using Lettra.Contracts.Demo;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Jeu de démonstration (vague 5) : un dossier vide peut le charger depuis l'accueil. Seuls les hôtes de démonstration
/// l'enregistrent ; le serveur ou le poste refusent hors <c>Lettra:Demo:Seed=true</c> et en environnement Production.
/// </summary>
public interface IDemoDataGateway
{
    Task<OperationResult<DemoDataStatusResponse>> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>Remplit le dossier ouvert s'il n'a encore aucune pièce (un chargement dure de l'ordre d'une demi-minute).</summary>
    Task<OperationResult<DemoSeedResponse>> SeedAsync(CancellationToken cancellationToken);
}
