using Lettra.Application.Abstractions.Tenancy;

namespace Lettra.Infrastructure.Persistence.Catalog;

/// <summary>Un cabinet. La cellule qui l'héberge est fixée à son arrivée (ADR-0009).</summary>
public sealed class CatalogOrganisation
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string CellId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Un serveur de bases d'une cellule (PostgreSQL derrière PgBouncer, SQL Server ou répertoire SQLite en mode 2).
/// Ses identifiants ne sont pas ici : ils viennent du coffre de la cellule (<c>Lettra:Clusters:{Id}</c>).
/// </summary>
public sealed class CatalogCluster
{
    public required string Id { get; set; }

    public required string CellId { get; set; }

    public DatabaseProvider Provider { get; set; }

    public int MaxDatabases { get; set; }

    public bool AcceptsNewDossiers { get; set; } = true;
}

public enum DossierState
{
    /// <summary>Base en cours de création ; reprise par le Worker si le processus s'arrête.</summary>
    Provisioning = 0,

    Active = 1,

    /// <summary>Création ou migration en échec ; reprise par le Worker.</summary>
    Failed = 2,

    /// <summary>Effacé : DEK détruite, base supprimée ou illisible.</summary>
    Deleted = 3,
}

/// <summary>Un dossier client du cabinet et l'emplacement de sa base. Aucune donnée métier du dossier ici.</summary>
public sealed class CatalogDossier
{
    public const int MaxNameLength = 200;

    public Guid Id { get; set; }

    public Guid OrganisationId { get; set; }

    public required string Name { get; set; }

    public DatabaseProvider Provider { get; set; }

    /// <summary>Référence de secret de la base (<c>cluster:pg-a/d_…</c>, <c>config:Nom</c>), jamais une chaîne de connexion.</summary>
    public required string ConnectionSecretRef { get; set; }

    /// <summary>Serveur et nom de base quand c'est le catalogue qui a créé la base ; <c>null</c> pour une base déclarée.</summary>
    public string? ClusterId { get; set; }

    public string? DatabaseName { get; set; }

    public string? Schema { get; set; }

    public DossierState State { get; set; }

    /// <summary>Dernière migration appliquée (identifiant commun aux trois providers).</summary>
    public string? SchemaVersion { get; set; }

    /// <summary>DEK du dossier enveloppée par la KEK du cabinet. La vider rend le dossier illisible.</summary>
    public byte[] WrappedDek { get; set; } = [];

    /// <summary>Bail du Worker qui crée ou migre la base (plusieurs Workers se répartissent les dossiers).</summary>
    public string? LeaseOwner { get; set; }

    /// <summary>En UTC. <see cref="DateTime"/> plutôt que <see cref="DateTimeOffset"/> : comparable sur les trois providers.</summary>
    public DateTime? LeaseUntil { get; set; }

    /// <summary>Message technique de la dernière erreur (aucune donnée du dossier).</summary>
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public TenantDescriptor ToDescriptor() =>
        new(new Domain.SharedKernel.DossierId(Id), new Domain.SharedKernel.OrganisationId(OrganisationId), Provider, ConnectionSecretRef, Schema);
}
