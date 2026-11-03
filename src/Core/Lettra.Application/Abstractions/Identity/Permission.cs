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

    /// <summary>Modifier un client ou un fournisseur existant (phase 3, concurrence optimiste).</summary>
    [PermissionCategory(PermissionKind.DossierData)] TiersModifier = 3,

    // 10-19 : dossier.
    [PermissionCategory(PermissionKind.DossierData)] DossierLire = 10,

    /// <summary>Archiver un dossier, le rattacher à un groupe. N'ouvre pas les données du dossier.</summary>
    [PermissionCategory(PermissionKind.Administration)] DossierAdministrer = 11,

    /// <summary>Créer un dossier (provisionner sa base) dans le cabinet. Portée cabinet.</summary>
    [PermissionCategory(PermissionKind.Administration)] DossiersCreer = 12,

    /// <summary>Lancer le contrôle d'intégrité de la chaîne anti-fraude et lire les clôtures.</summary>
    [PermissionCategory(PermissionKind.DossierData)] IntegriteVerifier = 13,

    /// <summary>Clôturer les périodes écoulées (le Worker le fait aussi une fois par jour).</summary>
    [PermissionCategory(PermissionKind.DossierData)] ClotureEffectuer = 14,

    /// <summary>Télécharger l'archive scellée d'une clôture (contrôle fiscal, auditeur).</summary>
    [PermissionCategory(PermissionKind.DossierData)] ArchivesExporter = 15,

    /// <summary>Faire tourner les clés de la chaîne du dossier (le Worker le fait aussi, chaque année par défaut).</summary>
    [PermissionCategory(PermissionKind.DossierData)] IntegriteClesGerer = 16,

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

    /// <summary>
    /// Approuver un avoir au-delà du seuil de la séparation des tâches (annexe F §3.2). Réservée au manager.
    /// </summary>
    [PermissionCategory(PermissionKind.DossierData)] AvoirApprouver = 44,

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

    // 110-119 : postes hors ligne (étape 4.4). Le registre est dans la base du dossier.

    /// <summary>Voir les postes du dossier, en révoquer un, régler la durée maximale hors ligne.</summary>
    [PermissionCategory(PermissionKind.DossierData)] PostesGerer = 110,

    // 120-129 : lutte contre le blanchiment (LCB-FT). La fiche du client est confidentielle : ni l'auditeur ni le
    // client ne la voient (interdiction de divulgation, art. L561-18 du Code monétaire et financier).

    /// <summary>Lire la fiche anti-blanchiment du client.</summary>
    [PermissionCategory(PermissionKind.DossierData)] VigilanceConsulter = 120,

    /// <summary>Remplir la fiche, fixer le niveau de risque, la revoir.</summary>
    [PermissionCategory(PermissionKind.DossierData)] VigilanceGerer = 121,

    // 130-139 : factures fournisseurs reçues par la plateforme agréée (réforme de la facturation électronique).

    /// <summary>Relever les factures reçues et y répondre (prise en charge, approuvée, litige, refus, paiement transmis).</summary>
    [PermissionCategory(PermissionKind.DossierData)] FacturesRecuesTraiter = 130,

    // 150-159 : décisions à prendre. Chaque décision exige aussi la permission de sa règle : on ne voit que ce sur quoi
    // on peut agir, et l'action acceptée passe par sa commande, qui vérifie la sienne.

    /// <summary>Voir les décisions à prendre du dossier, les accepter, les remettre à plus tard, les ignorer, les actualiser.</summary>
    [PermissionCategory(PermissionKind.DossierData)] DecisionsTraiter = 150,

    /// <summary>Régler le cadran d'autonomie du dossier (montrer seulement, ou préparer l'action).</summary>
    [PermissionCategory(PermissionKind.DossierData)] DecisionsRegler = 151,
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
