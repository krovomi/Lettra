using System.Collections.Frozen;
using System.Reflection;
using Lettra.Application.Abstractions.Identity;

namespace Lettra.Application.Authorization;

/// <summary>
/// Les rôles fournis (annexe F §3.2). Un cabinet peut composer ses propres rôles à partir des mêmes
/// permissions. La matrice rôle × action × portée de <c>Lettra.Identity.Tests</c> fige ce tableau.
/// </summary>
public static class RoleTemplates
{
    public const string AdministrateurPlateforme = "administrateur-plateforme";
    public const string AdministrateurCabinet = "administrateur-cabinet";
    public const string Manager = "manager";
    public const string AdministrateurGroupe = "administrateur-groupe";
    public const string CollaborateurEtendu = "collaborateur-etendu";
    public const string Collaborateur = "collaborateur";
    public const string Auditeur = "auditeur";
    public const string ClientPortail = "client-portail";
    public const string ExploitantSurSite = "exploitant-sur-site";

    /// <summary>Délégation temporaire : avoirs et exports confiés à un collaborateur étendu, avec échéance.</summary>
    public const string DelegationAvoirsExports = "delegation-avoirs-exports";

    private static readonly Permission[] Production =
    [
        Permission.TiersConsulter, Permission.TiersCreer, Permission.DossierLire, Permission.CatalogueConsulter, Permission.DevisCreer,
        Permission.FactureSaisir, Permission.IaUtiliser, Permission.AlertesLire,
    ];

    public static FrozenDictionary<string, RoleDefinition> All { get; } = new RoleDefinition[]
    {
        new(AdministrateurPlateforme, Set(Permission.PlateformeAdministrer), ScopeKind.Plateforme),
        new(AdministrateurCabinet, Set(
            Permission.CabinetAdministrer, Permission.UtilisateursGerer, Permission.GroupeGerer, Permission.DossierAdministrer, Permission.DossiersCreer,
            Permission.AlertesLire, Permission.AlertesConfigurer), ScopeKind.Cabinet),
        new(Manager, Set([.. Production,
            Permission.CatalogueGerer, Permission.FactureValider, Permission.FactureEmettre, Permission.AvoirCreer, Permission.FecExporter,
            Permission.DelegationsApprouver, Permission.AlertesTraiter,
            Permission.IntegriteVerifier, Permission.ClotureEffectuer, Permission.ArchivesExporter, Permission.IntegriteClesGerer]), ScopeKind.Cabinet),
        new(AdministrateurGroupe, Set(Permission.GroupeGerer, Permission.DossierAdministrer), ScopeKind.Groupe),
        new(CollaborateurEtendu, Set([.. Production, Permission.FactureValider, Permission.FactureEmettre]), ScopeKind.Groupe),
        new(Collaborateur, Set(Production), ScopeKind.Groupe),
        new(Auditeur, Set(
            Permission.TiersConsulter, Permission.CatalogueConsulter, Permission.DossierLire, Permission.FecExporter,
            Permission.IntegriteVerifier, Permission.ArchivesExporter), ScopeKind.Dossier, RequiresExpiry: true),
        new(ClientPortail, Set(Permission.PortailConsulter, Permission.DevisAccepter, Permission.PiecesDeposer), ScopeKind.Dossier),
        new(ExploitantSurSite, Set(Permission.AlertesLire, Permission.AlertesTraiter), ScopeKind.Cabinet),
        new(DelegationAvoirsExports, Set(Permission.AvoirCreer, Permission.FecExporter), ScopeKind.Groupe, RequiresExpiry: true),
    }.ToFrozenDictionary(r => r.Key, StringComparer.Ordinal);

    private static FrozenSet<Permission> Set(params Permission[] permissions) => permissions.ToFrozenSet();
}

/// <summary>Métadonnées des permissions.</summary>
public static class PermissionCatalog
{
    private static readonly FrozenDictionary<Permission, PermissionKind> Kinds = Enum.GetValues<Permission>()
        .ToFrozenDictionary(p => p, p => typeof(Permission).GetField(p.ToString())!
            .GetCustomAttribute<PermissionCategoryAttribute>()?.Kind ?? PermissionKind.DossierData);

    public static PermissionKind KindOf(Permission permission) => Kinds[permission];

    /// <summary>Au moins une permission qui ouvre les données du dossier : c'est la condition pour ouvrir sa base.</summary>
    public static bool OpensDossierData(IReadOnlySet<Permission> granted)
    {
        ArgumentNullException.ThrowIfNull(granted);
        return granted.Any(p => KindOf(p) == PermissionKind.DossierData);
    }
}
