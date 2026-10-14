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
}

public sealed class CatalogDatabaseOptions
{
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>Nom de la chaîne de connexion du catalogue dans <c>ConnectionStrings</c>.</summary>
    public string ConnectionStringName { get; set; } = "Catalog";

    /// <summary>Migrer le catalogue au démarrage de l'hôte. <c>false</c> en SaaS : c'est le Worker qui migre.</summary>
    public bool MigrateOnStartup { get; set; } = true;
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
