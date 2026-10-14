namespace Lettra.Domain.SharedKernel;

/// <summary>Identifiant du dossier (l'entreprise cliente du cabinet) : c'est le tenant.</summary>
public readonly record struct DossierId(Guid Value)
{
    public static DossierId From(Guid value) =>
        value == Guid.Empty ? throw new ArgumentException("Identifiant de dossier vide.", nameof(value)) : new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>Identifiant du cabinet (organisation) qui gère les dossiers.</summary>
public readonly record struct OrganisationId(Guid Value)
{
    public override string ToString() => Value.ToString();
}

/// <summary>Identifiant d'un client du dossier. Guid v7 : triable, sans collision entre postes hors ligne.</summary>
public readonly record struct CustomerId(Guid Value)
{
    public static CustomerId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
