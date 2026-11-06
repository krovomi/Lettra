using Lettra.Application.Abstractions.Tenancy;

namespace Lettra.Infrastructure.Persistence.Catalog;

/// <summary>Un cabinet. La cellule qui l'héberge est fixée à son arrivée (ADR-0009).</summary>
public sealed class CatalogOrganisation
{
    public const int MaxWrappedKekLength = 512;

    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string CellId { get; set; }

    /// <summary>
    /// KEK du cabinet enveloppée par sa clé racine dans le KMS de la cellule (chiffré Transit <c>vault:vN:…</c>), quand
    /// le KMS tient les clés ; <c>null</c> sinon (KEK déclarée dans le coffre de la cellule). Détruire la clé racine du
    /// cabinet dans le KMS rend cette enveloppe, et donc tous ses dossiers, illisibles, sauvegardes comprises.
    /// </summary>
    public string? WrappedKek { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public OrganisationState State { get; set; }

    /// <summary>
    /// Cabinet inscrit par la plateforme (organisation et comptes créés chez le fournisseur d'identité) : à son départ,
    /// ses comptes et son organisation y sont supprimés. Un cabinet déclaré dans la configuration ne l'est pas.
    /// </summary>
    public bool ManagedIdentity { get; set; }

    /// <summary>En UTC : fin du délai de rétractation d'un départ demandé (<see cref="OrganisationState.Leaving"/>).</summary>
    public DateTime? DepartureDueAt { get; set; }

    /// <summary>L'administrateur de la plateforme qui a demandé le départ (trace, pas de nom).</summary>
    public Guid? DepartureRequestedBy { get; set; }

    /// <summary>En UTC : départ effectif (dossiers effacés, clé détruite, comptes supprimés).</summary>
    public DateTime? DepartedAt { get; set; }
}

/// <summary>Le cycle de vie d'un cabinet sur la plateforme.</summary>
public enum OrganisationState
{
    /// <summary>Le cabinet travaille.</summary>
    Active = 0,

    /// <summary>Départ demandé : le cabinet travaille et exporte jusqu'à la fin du délai, sans nouveau dossier.</summary>
    Leaving = 1,

    /// <summary>Parti : seule la trace (identifiant, nom, dates) reste au catalogue.</summary>
    Departed = 2,
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

    /// <summary>
    /// Effacement demandé par le cabinet : le dossier est fermé à tous, mais le cabinet peut encore l'annuler jusqu'à
    /// <see cref="CatalogDossier.ErasureDueAt"/>. Ensuite, le Worker détruit sa DEK puis supprime sa base.
    /// </summary>
    ErasureScheduled = 4,
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

    /// <summary>Type de l'exception de la dernière erreur ; le message reste dans le journal technique.</summary>
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>En UTC : fin du délai de rétractation d'un effacement demandé (<see cref="DossierState.ErasureScheduled"/>).</summary>
    public DateTime? ErasureDueAt { get; set; }

    /// <summary>Qui a demandé l'effacement (identifiant du compte, aucune donnée du dossier).</summary>
    public Guid? ErasureRequestedBy { get; set; }

    /// <summary>En UTC : quand le dossier a été effacé (DEK détruite, base supprimée).</summary>
    public DateTime? ErasedAt { get; set; }

    public TenantDescriptor ToDescriptor() =>
        new(new Domain.SharedKernel.DossierId(Id), new Domain.SharedKernel.OrganisationId(OrganisationId), Provider, ConnectionSecretRef, Schema);
}
