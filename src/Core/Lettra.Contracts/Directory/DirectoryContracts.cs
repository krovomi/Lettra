using System.Text.Json.Serialization;

namespace Lettra.Contracts.Directory;

/// <summary>
/// Entreprise trouvée dans l'annuaire SIRENE (API Recherche d'entreprises de l'État). Sert à pré-remplir
/// un client ou un fournisseur : l'utilisateur relit et valide, rien n'est enregistré sans lui.
/// </summary>
/// <param name="Siren">SIREN de l'unité légale.</param>
/// <param name="Siret">SIRET du siège.</param>
/// <param name="Name">Dénomination (ou nom complet pour un entrepreneur individuel).</param>
/// <param name="AddressLine1">Numéro et voie du siège.</param>
/// <param name="AddressLine2">Complément d'adresse.</param>
/// <param name="PostalCode">Code postal du siège.</param>
/// <param name="City">Commune du siège.</param>
/// <param name="CountryCode">Pays (FR, sauf siège à l'étranger).</param>
/// <param name="VatNumber">Numéro de TVA français déduit du SIREN (à confirmer par VIES).</param>
/// <param name="ActivityCode">Code NAF/APE de l'activité principale.</param>
/// <param name="IsActive">Faux si l'entreprise est cessée.</param>
public sealed record CompanySearchResult(
    string Siren,
    string? Siret,
    string Name,
    string? AddressLine1,
    string? AddressLine2,
    string? PostalCode,
    string? City,
    string CountryCode,
    string VatNumber,
    string? ActivityCode,
    bool IsActive);

/// <summary>Résultats d'une recherche dans l'annuaire.</summary>
public sealed record CompanySearchResponse(IReadOnlyList<CompanySearchResult> Items, int TotalCount);

/// <summary>Réponse de VIES (Commission européenne) sur un numéro de TVA intracommunautaire.</summary>
/// <param name="VatNumber">Numéro vérifié, sous forme compacte.</param>
/// <param name="IsValid">Vrai si le numéro est actif dans le registre de l'État membre.</param>
/// <param name="Name">Nom déclaré, quand l'État membre le publie.</param>
/// <param name="Address">Adresse déclarée, quand l'État membre la publie.</param>
/// <param name="CheckedAt">Date de la consultation (à conserver comme preuve de diligence).</param>
public sealed record VatNumberCheckResponse(string VatNumber, bool IsValid, string? Name, string? Address, DateTimeOffset CheckedAt);

/// <summary>État d'un client dans l'annuaire SIRENE.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DirectoryStatusDto>))]
public enum DirectoryStatusDto
{
    /// <summary>Entreprise en activité.</summary>
    Active = 1,

    /// <summary>Entreprise cessée (radiée) : ne plus lui facturer sans vérifier.</summary>
    Ceased = 2,

    /// <summary>SIREN inconnu de l'annuaire : faute de frappe probable.</summary>
    NotFound = 3,
}

/// <summary>Un client dont l'annuaire dit qu'il est cessé ou inconnu.</summary>
public sealed record CustomerDirectoryFinding(Guid CustomerId, string Name, string Siren, DirectoryStatusDto Status);

/// <summary>
/// Contrôle des clients du dossier dans l'annuaire SIRENE : combien ont été vérifiés, lesquels posent problème.
/// <paramref name="Complete"/> est faux si l'annuaire n'a pas répondu pour au moins un client.
/// </summary>
public sealed record CustomerDirectoryCheckResponse(int Checked, IReadOnlyList<CustomerDirectoryFinding> Findings, bool Complete);
