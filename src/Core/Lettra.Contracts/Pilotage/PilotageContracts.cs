using Lettra.Contracts.Cabinet;

namespace Lettra.Contracts.Pilotage;

/// <summary>
/// Un mois de l'activité d'un dossier : chiffre d'affaires HT en indice (100 = mois moyen des 12 derniers mois,
/// <c>null</c> sans vente sur l'année) et factures émises. <paramref name="Month"/> s'écrit <c>2026-10</c>.
/// </summary>
public sealed record MonthlyActivityDto(string Month, double? RevenueIndex, int InvoicesIssued);

/// <summary>
/// Les indicateurs de pilotage d'un dossier (docs/tableaux-de-bord.md). Un ratio vaut entre 0 et 1 ; il est <c>null</c>
/// quand il n'a pas de base (aucune vente, aucun devis tranché…). Les mêmes valeurs que les tableaux Grafana.
/// </summary>
public sealed record DossierPilotageDto(
    Guid DossierId,
    string Name,
    double? DaysSalesOutstanding,
    double? OverdueReceivablesRatio,
    double? CollectionRatio,
    double? RevenueTrendRatio,
    int ActiveCustomers,
    double? TopCustomerShareRatio,
    double? QuoteConversionRatio,
    int QuotesPending,
    int QuotesExpiringSoon,
    IReadOnlyList<MonthlyActivityDto> Months);

/// <summary>
/// Le tableau de bord du dossier ouvert : ses indicateurs calculés à l'instant, et ce qui reste à faire selon le dernier
/// calcul de la vue cabinet (<paramref name="ToDoComputedAt"/> est <c>null</c> s'il n'a jamais tourné).
/// </summary>
public sealed record DossierPilotageResponse(
    DateTimeOffset ComputedAt,
    DossierPilotageDto Dossier,
    DateTimeOffset? ToDoComputedAt,
    IReadOnlyList<DossierIndicatorDto> ToDo);

/// <summary>
/// Les indicateurs des dossiers que l'utilisateur peut ouvrir, lus dans la dernière collecte du serveur
/// (<paramref name="CollectedAt"/>, au plus quelques minutes). <paramref name="Available"/> est faux là où le serveur ne
/// collecte pas (poste isolé) : l'écran le dit au lieu d'afficher une liste vide.
/// </summary>
public sealed record CabinetPilotageResponse(bool Available, DateTimeOffset? CollectedAt, IReadOnlyList<DossierPilotageDto> Dossiers);

/// <summary>
/// L'exploitation d'un dossier : place occupée, postes reliés au serveur, fraîcheur du dernier calcul de la vue cabinet.
/// </summary>
public sealed record DossierOperationsDto(
    Guid DossierId,
    string Name,
    long? DatabaseSizeBytes,
    int ActiveDevices,
    int RevokedDevices,
    int SilentDevices,
    double? OldestDeviceSyncAgeSeconds,
    double? CabinetIndicatorAgeSeconds);

/// <summary>
/// Le tableau d'administration : les dossiers que l'utilisateur administre, lus dans la dernière collecte du serveur.
/// </summary>
public sealed record AdministrationPilotageResponse(bool Available, DateTimeOffset? CollectedAt, IReadOnlyList<DossierOperationsDto> Dossiers);

/// <summary>Un indicateur comparé aux dossiers du même secteur.</summary>
public enum PeerMetricDto
{
    DaysSalesOutstanding = 1,
    OverdueReceivables = 2,
    Collection = 3,
    RevenueTrend = 4,
    QuoteConversion = 5,
    TopCustomerShare = 6,
    CreditNotes = 7,
}

/// <summary>
/// Pourquoi la comparaison est rendue ou non : pas de code NAF sur la fiche entreprise, pas de collecte sur ce poste, ou
/// moins de <see cref="PeerComparisonResponse.MinimumPeers"/> dossiers du même secteur dans le cabinet.
/// </summary>
public enum PeerComparisonStatusDto
{
    Available = 1,
    NoSector = 2,
    NoCollection = 3,
    TooFewPeers = 4,
}

/// <summary>Le dossier par rapport à la médiane de ses pairs : mieux, comparable ou moins bien.</summary>
public enum PeerPositionDto
{
    Better = 1,
    Similar = 2,
    Worse = 3,
}

/// <summary>
/// Un indicateur du dossier et la médiane des pairs qui en ont un (<paramref name="Peers"/>, jamais moins de
/// <see cref="PeerComparisonResponse.MinimumPeers"/>). <paramref name="Position"/> est vide quand le dossier n'a pas la
/// valeur (aucune vente, aucun devis tranché…).
/// </summary>
public sealed record PeerMetricComparisonDto(PeerMetricDto Metric, double? Value, double Median, int Peers, PeerPositionDto? Position);

/// <summary>
/// Le dossier ouvert comparé aux autres dossiers du cabinet de la même division NAF (<paramref name="Sector"/>, « 62 »),
/// lus dans la dernière collecte. Rien que des médianes : aucun dossier, aucun nom, aucun montant n'est rendu.
/// </summary>
public sealed record PeerComparisonResponse(
    PeerComparisonStatusDto Status,
    string? ActivityCode,
    string? Sector,
    int Peers,
    DateTimeOffset? CollectedAt,
    IReadOnlyList<PeerMetricComparisonDto> Metrics)
{
    /// <summary>En dessous de cinq pairs, une médiane laisserait deviner un dossier : rien n'est rendu.</summary>
    public const int MinimumPeers = 5;
}
