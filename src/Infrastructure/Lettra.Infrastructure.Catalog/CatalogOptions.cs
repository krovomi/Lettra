using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;

namespace Lettra.Infrastructure.Catalog;

/// <summary>
/// Ce que la configuration déclare au catalogue (section <c>Lettra</c>) : cabinets, serveurs de bases et dossiers
/// existants. Importé à chaque démarrage par <see cref="CatalogInitializer"/>, sans jamais rien supprimer.
/// Les identifiants des serveurs restent dans la configuration (coffre de la cellule), jamais dans le catalogue.
/// </summary>
public sealed class CatalogOptions
{
    public const string SectionName = "Lettra";

    public IList<OrganisationEntry> Organisations { get; init; } = [];

    /// <summary>Clé : identifiant du serveur, repris dans les références <c>cluster:{id}/{base}</c>.</summary>
    public IDictionary<string, ClusterEntry> Clusters { get; init; } = new Dictionary<string, ClusterEntry>(StringComparer.Ordinal);

    /// <summary>Dossiers dont la base existe déjà (serveur de cabinet, démonstration, tests).</summary>
    public IList<DossierEntry> Dossiers { get; init; } = [];

    public CatalogDatabaseOptions Catalog { get; init; } = new();

    /// <summary>Comptes, groupes et rôles déclarés (section <c>Lettra:Authorization</c>, même forme qu'en mode 2 sans catalogue).</summary>
    public AuthorizationEntries Authorization { get; init; } = new();
}

public sealed class CatalogDatabaseOptions
{
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>Nom de la chaîne de connexion du catalogue dans <c>ConnectionStrings</c>.</summary>
    public string ConnectionStringName { get; set; } = "Catalog";

    /// <summary>Migrer le catalogue au démarrage de l'hôte. <c>false</c> en SaaS : c'est le Worker qui migre.</summary>
    public bool MigrateOnStartup { get; set; } = true;

    /// <summary>
    /// Délai de rétractation d'un effacement de dossier demandé par le cabinet : le dossier est fermé aussitôt, sa clé
    /// et sa base ne sont détruites qu'à la fin de ce délai (30 jours par défaut).
    /// </summary>
    public TimeSpan DossierErasureDelay { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Délai de rétractation du départ d'un cabinet : il travaille et exporte ses données jusqu'à son terme.</summary>
    public TimeSpan CabinetDepartureDelay { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Dossiers qu'un cabinet peut tenir dans la cellule (créés ou importés, effacements en attente compris) : un cabinet
    /// ne remplit pas à lui seul un serveur de bases partagé. 500 par défaut, au-delà de la taille XL du dimensionnement.
    /// </summary>
    public int MaxDossiersPerCabinet { get; set; } = 500;

    /// <summary>
    /// Qui crée et supprime les bases des dossiers. <see cref="DatabaseAdministration.Host"/> (par défaut) : l'hôte qui
    /// reçoit la demande, avec la connexion d'administration de chaque serveur. <see cref="DatabaseAdministration.Worker"/>
    /// (cellule en ligne) : l'API inscrit la demande au catalogue et attend que le Worker, seul à détenir cette connexion,
    /// l'ait menée.
    /// </summary>
    public DatabaseAdministration DatabaseAdministration { get; set; } = DatabaseAdministration.Host;

    /// <summary>Attente par l'API d'une création menée par le Worker, au-delà de laquelle la demande lui est laissée (60 s).</summary>
    public TimeSpan ProvisioningWait { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>Qui détient la connexion d'administration des serveurs de bases (<see cref="CatalogDatabaseOptions.DatabaseAdministration"/>).</summary>
public enum DatabaseAdministration
{
    /// <summary>L'hôte qui reçoit la demande crée la base lui-même (poste, serveur du cabinet, démonstration, tests).</summary>
    Host,

    /// <summary>Seul le Worker crée et supprime les bases : l'API d'une cellule n'a aucun accès d'administration.</summary>
    Worker,
}

public sealed class OrganisationEntry
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "Cabinet";

    public string CellId { get; set; } = "local";

    /// <summary>Clé maîtresse (KEK) du cabinet, 32 octets en base 64, injectée depuis le coffre de la cellule.</summary>
    public string? Kek { get; set; }
}

public sealed class ClusterEntry
{
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.PostgreSql;

    public string CellId { get; set; } = "local";

    public int MaxDatabases { get; set; } = 1000;

    public bool AcceptsNewDossiers { get; set; } = true;

    /// <summary>Connexion applicative (via PgBouncer en SaaS), sans nom de base.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Connexion d'administration directe (CREATE / DROP DATABASE), sans nom de base.</summary>
    public string? AdminConnectionString { get; set; }

    /// <summary>SQLite : répertoire des fichiers de dossiers.</summary>
    public string? Directory { get; set; }

    /// <summary>
    /// PostgreSQL : chaque cabinet se connecte à ses bases par son propre rôle, et chaque base n'accepte que le rôle de son
    /// cabinet (plan du mode 5, étape 3). Le rôle d'administration doit alors avoir CREATEROLE. Désactivé par défaut :
    /// un serveur de cabinet garde son rôle unique.
    /// </summary>
    public bool CabinetRoles { get; set; }
}

/// <summary>Un dossier déclaré dans la configuration (section <c>Lettra:Dossiers</c>).</summary>
public sealed class DossierEntry
{
    public Guid Id { get; set; }

    public Guid OrganisationId { get; set; }

    public string Name { get; set; } = "Dossier";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>Référence de secret, de la forme <c>config:NomDeChaine</c> (résolue dans <c>ConnectionStrings</c>).</summary>
    public string ConnectionSecretRef { get; set; } = string.Empty;

    public string? Schema { get; set; }
}

/// <summary>
/// Registre des droits déclaré en configuration (démonstration, tests, amorçage d'un cabinet). L'import ajoute et met
/// à jour ; il ne retire ni membre, ni placement, ni attribution.
/// </summary>
public sealed class AuthorizationEntries
{
    public IList<AccountEntry> Users { get; init; } = [];

    public IList<GroupEntry> Groups { get; init; } = [];

    public IList<CustomRoleEntry> Roles { get; init; } = [];
}

public sealed class AssignmentEntry
{
    public string Role { get; set; } = string.Empty;

    public ScopeKind Scope { get; set; } = ScopeKind.Dossier;

    /// <summary>Cabinet, groupe ou dossier (ignoré pour la plateforme).</summary>
    public Guid ScopeId { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class AccountEntry
{
    public Guid Id { get; set; }

    public AccountState State { get; set; } = AccountState.Active;

    public Guid? Cabinet { get; set; }

    /// <summary>Nom affiché dans l'espace d'administration (facultatif ; une valeur vide ne remplace pas celle saisie).</summary>
    public string? DisplayName { get; set; }

    /// <summary>Adresse affichée dans l'espace d'administration (facultative).</summary>
    public string? Email { get; set; }

    public IList<Guid> Groups { get; init; } = [];

    public IList<AssignmentEntry> Assignments { get; init; } = [];
}

public sealed class GroupEntry
{
    public Guid Id { get; set; }

    public Guid Cabinet { get; set; }

    public string Name { get; set; } = "Groupe";

    public IList<Guid> Dossiers { get; init; } = [];

    public IList<AssignmentEntry> Assignments { get; init; } = [];
}

public sealed class CustomRoleEntry
{
    public Guid Cabinet { get; set; }

    public string Key { get; set; } = string.Empty;

    public IList<Permission> Permissions { get; init; } = [];

    public ScopeKind WidestScope { get; set; } = ScopeKind.Dossier;

    public bool RequiresExpiry { get; set; }
}
