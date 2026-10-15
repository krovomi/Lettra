namespace Lettra.Contracts.Presence;

/// <summary>Documents dont la présence est suivie. La lecture n'est jamais bloquée : la présence informe, elle ne verrouille pas.</summary>
public enum PresenceDocumentType
{
    Customer = 1,
    Supplier = 2,
    CatalogItem = 3,
    Quote = 4,
    Invoice = 5,
    CreditNote = 6,
}

/// <summary>Ce que fait l'utilisateur sur le document. <see cref="Editing"/> est un verrou souple qui expire sans signe de vie.</summary>
public enum PresenceIntent
{
    Viewing = 1,
    Editing = 2,
}

/// <summary>Un utilisateur présent sur un document (une entrée par utilisateur, même avec plusieurs onglets).</summary>
public sealed record PresenceEntry(Guid UserId, string? DisplayName, PresenceIntent Intent, DateTimeOffset Since);

/// <summary>Qui est sur le document, envoyé à chaque changement au groupe du document.</summary>
public sealed record DocumentPresence(Guid DossierId, PresenceDocumentType DocumentType, Guid DocumentId, IReadOnlyList<PresenceEntry> Users);

/// <summary>Contrat du hub SignalR de présence : chemin, méthodes appelées par le client et message reçu.</summary>
public static class PresenceHubContract
{
    public const string Path = "/hubs/presence";

    /// <summary><c>Join(dossierId, documentType, documentId, intent)</c> : entre sur le document ou renouvelle sa présence ; renvoie la présence.</summary>
    public const string Join = "Join";

    /// <summary><c>Leave(dossierId, documentType, documentId)</c> : quitte le document.</summary>
    public const string Leave = "Leave";

    /// <summary>Message reçu par les clients du document : un <see cref="DocumentPresence"/>.</summary>
    public const string Changed = "PresenceChanged";

    /// <summary>Le client renouvelle sa présence (<see cref="Join"/>) plus souvent que cette durée, en secondes, sinon elle expire.</summary>
    public const int HeartbeatSeconds = 30;
}
