namespace Lettra.Application.Abstractions.Identity;

/// <summary>
/// Permissions élémentaires, stables dans le code (annexe F §3.2). Les rôles sont des ensembles de ces
/// permissions ; un cabinet peut composer ses propres rôles, sans jamais en ajouter.
/// <para>
/// <b>Ajout seulement</b> : une valeur n'est jamais renumérotée ni supprimée (elle est stockée dans les
/// rôles personnalisés et les instantanés hors ligne). Chaque module prend sa plage de numéros et chaque
/// permission déclare sa <see cref="PermissionCategoryAttribute"/> (vérifié par un test).
/// </para>
/// </summary>
public enum Permission
{
    // 1-9 : Tiers (module Tiers).
    [PermissionCategory(PermissionKind.DossierData)] TiersConsulter = 1,
    [PermissionCategory(PermissionKind.DossierData)] TiersCreer = 2,

    // 10-19 : dossier.
    [PermissionCategory(PermissionKind.DossierData)] DossierLire = 10,

    /// <summary>Archiver un dossier, le rattacher à un groupe. N'ouvre pas les données du dossier.</summary>
    [PermissionCategory(PermissionKind.Administration)] DossierAdministrer = 11,

    /// <summary>Créer un dossier (provisionner sa base) dans le cabinet. Portée cabinet.</summary>
    [PermissionCategory(PermissionKind.Administration)] DossiersCreer = 12,

    // 20-29 : Catalogue (module Catalogue).
    [PermissionCategory(PermissionKind.DossierData)] CatalogueConsulter = 20,
    [PermissionCategory(PermissionKind.DossierData)] CatalogueGerer = 21,

    // 30-39 : devis.
    [PermissionCategory(PermissionKind.DossierData)] DevisCreer = 30,

    // 40-49 : factures et avoirs.
    [PermissionCategory(PermissionKind.DossierData)] FactureSaisir = 40,
    [PermissionCategory(PermissionKind.DossierData)] FactureValider = 41,
    [PermissionCategory(PermissionKind.DossierData)] FactureEmettre = 42,
    [PermissionCategory(PermissionKind.DossierData)] AvoirCreer = 43,

    // 50-59 : exports.
    [PermissionCategory(PermissionKind.DossierData)] FecExporter = 50,

    // 60-69 : IA.
    [PermissionCategory(PermissionKind.DossierData)] IaUtiliser = 60,

    // 70-79 : administration du cabinet.
    [PermissionCategory(PermissionKind.Administration)] GroupeGerer = 70,
    [PermissionCategory(PermissionKind.Administration)] UtilisateursGerer = 71,
    [PermissionCategory(PermissionKind.Administration)] CabinetAdministrer = 72,

    /// <summary>Approuver une délégation temporaire (congés, remplacement).</summary>
    [PermissionCategory(PermissionKind.Administration)] DelegationsApprouver = 73,

    // 80-89 : alertes (annexe G §6). Les alertes de dossier ne contiennent pas de données métier.
    [PermissionCategory(PermissionKind.Administration)] AlertesLire = 80,
    [PermissionCategory(PermissionKind.Administration)] AlertesTraiter = 81,
    [PermissionCategory(PermissionKind.Administration)] AlertesConfigurer = 82,

    // 90-99 : portail du client du cabinet.
    [PermissionCategory(PermissionKind.DossierData)] PortailConsulter = 90,
    [PermissionCategory(PermissionKind.DossierData)] DevisAccepter = 91,
    [PermissionCategory(PermissionKind.DossierData)] PiecesDeposer = 92,

    // 100-109 : plateforme (l'éditeur). Aucune n'ouvre un dossier.
    [PermissionCategory(PermissionKind.Administration)] PlateformeAdministrer = 100,
}

/// <summary>Ce qu'une permission ouvre.</summary>
public enum PermissionKind
{
    /// <summary>Lit ou modifie les données d'un dossier : ouvre sa base.</summary>
    DossierData = 1,

    /// <summary>Administration ou supervision : n'ouvre jamais la base d'un dossier.</summary>
    Administration = 2,
}

[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class PermissionCategoryAttribute(PermissionKind kind) : Attribute
{
    public PermissionKind Kind { get; } = kind;
}
