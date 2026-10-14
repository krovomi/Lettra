using Lettra.Poc.Sync.Contracts;

namespace Lettra.Poc.Sync.Client;

public enum ClientKind
{
    /// <summary>App MAUI : réplique SQLite chiffrée durable, valide hors ligne dans la série du poste.</summary>
    Maui,
    /// <summary>Navigateur (PWA) : stockage effaçable, brouillons seulement, numérotation à la synchro.</summary>
    Browser,
}

public sealed record ClientProfile(Guid DeviceId, Guid DossierId, ClientKind Kind, string? SeriesCode)
{
    public static ClientProfile Maui(Guid deviceId, Guid dossierId, string seriesCode) => new(deviceId, dossierId, ClientKind.Maui, seriesCode);
    public static ClientProfile Browser(Guid deviceId, Guid dossierId) => new(deviceId, dossierId, ClientKind.Browser, null);
}

public enum NetworkKind { None, Wifi, Ethernet, Cellular }

public sealed record NetworkStatus(bool ServerReachable, NetworkKind Kind);

/// <summary>Sonde réelle : un évènement « réseau revenu » ne suffit pas (portail captif d'hôtel).</summary>
public interface INetworkProbe
{
    Task<NetworkStatus> ProbeAsync(CancellationToken ct);
}

public enum SyncConsent { Now, Later, ShowDetail }

public sealed record SyncSummary(int ValidatedInvoices, int DraftChanges, int PendingValidations, long EstimatedBytes)
{
    public bool IsEmpty => ValidatedInvoices + DraftChanges + PendingValidations == 0;

    /// <summary>Texte du dialogue, par exemple « 3 factures validées, 5 modifications de brouillons ».</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (ValidatedInvoices > 0) parts.Add(Plural(ValidatedInvoices, "facture validée", "factures validées"));
        if (PendingValidations > 0) parts.Add(Plural(PendingValidations, "facture à numéroter", "factures à numéroter"));
        if (DraftChanges > 0) parts.Add(Plural(DraftChanges, "modification de brouillon", "modifications de brouillons"));
        return parts.Count == 0 ? "Rien à envoyer" : string.Join(", ", parts);
    }

    private static string Plural(int n, string one, string many) => $"{n} {(n > 1 ? many : one)}";
}

public sealed record OutboxItemView(long Sequence, string Kind, Guid InvoiceId, string Description, DateTimeOffset CreatedAt);

/// <summary>Port implémenté par l'UI Blazor : dialogue non bloquant et pastille dans la barre d'état.</summary>
public interface ISyncConsentPrompt
{
    Task<SyncConsent> AskAsync(SyncSummary summary, NetworkKind network, CancellationToken ct);
    Task ShowDetailAsync(IReadOnlyList<OutboxItemView> items, CancellationToken ct);
}

public enum SyncPreference
{
    /// <summary>Défaut : on demande à chaque retour du réseau.</summary>
    AlwaysAsk,
    /// <summary>Synchronise sans demander en Wi-Fi ou Ethernet ; demande en réseau mobile.</summary>
    AutoOnWifi,
    /// <summary>Jamais en réseau mobile (ni synchro, ni question) ; demande ailleurs.</summary>
    NeverOnCellular,
}

public enum SyncState { Offline, PendingConsent, Authenticating, Pushing, Pulling, ResolvingConflicts, UpToDate, Failed, Revoked }

public enum ReminderLevel { None, Banner, Alert }

public sealed record SyncReport(
    SyncState State,
    SyncSummary Summary,
    int FactsAccepted,
    int FactsRejected,
    int IntentsApplied,
    int Conflicts,
    int Pulled,
    IReadOnlyList<string> Messages);

/// <summary>Accès au serveur de synchronisation (HTTP en vrai, in-process possible en mode 1 à 3).</summary>
public interface ISyncClient
{
    Task EnsureAuthenticatedAsync(CancellationToken ct);
    Task<PushFactsResponse> PushFactsAsync(IReadOnlyList<ValidatedInvoiceFact> facts, CancellationToken ct);
    Task<PushIntentsResponse> PushIntentsAsync(IReadOnlyList<DraftCommand> commands, CancellationToken ct);
    Task<ChangesResponse> PullSinceAsync(long syncToken, CancellationToken ct);
}

public sealed class ReauthenticationRequiredException()
    : Exception("Session expirée : reconnectez-vous avant de synchroniser.");

public sealed class DeviceRevokedException()
    : Exception("Cet appareil a été révoqué : ses données locales sont effacées.");
