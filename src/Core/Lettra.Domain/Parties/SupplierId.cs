namespace Lettra.Domain.Parties;

/// <summary>Identifiant d'un fournisseur du dossier. Guid v7 : triable, sans collision entre postes hors ligne.</summary>
public readonly record struct SupplierId(Guid Value)
{
    public static SupplierId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
