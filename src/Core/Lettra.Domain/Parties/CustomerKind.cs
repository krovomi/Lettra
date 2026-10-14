namespace Lettra.Domain.Parties;

/// <summary>Nature du client : les règles d'identification diffèrent.</summary>
public enum CustomerKind
{
    /// <summary>Entreprise, association, administration : identifiée par son SIREN si française.</summary>
    Company = 1,

    /// <summary>Particulier : ni SIREN ni TVA.</summary>
    Individual = 2,
}
