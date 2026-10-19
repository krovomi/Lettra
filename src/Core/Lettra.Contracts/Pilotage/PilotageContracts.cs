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
/// L'exploitation d'un dossier : place occupée, postes reliés au serveur, fraîcheur du calcul de nuit de la vue cabinet.
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
