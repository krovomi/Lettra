namespace Lettra.Contracts.Customers;

/// <summary>
/// Limites de la liste des clients (GET /api/v1/customers), partagées par l'API et les écrans : une page plus grande
/// est refusée (Validation.Invalid sur PageSize), et un écran qui en demandait une n'affichait aucun client.
/// </summary>
public static class CustomerListing
{
    /// <summary>Taille de page maximale acceptée.</summary>
    public const int MaxPageSize = 200;
}
