namespace Lettra.Domain.Catalog;

/// <summary>Identifiant d'un article du catalogue. Guid v7 : triable, sans collision entre postes hors ligne.</summary>
public readonly record struct CatalogItemId(Guid Value)
{
    public static CatalogItemId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
