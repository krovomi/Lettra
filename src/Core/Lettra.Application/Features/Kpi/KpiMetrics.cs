namespace Lettra.Application.Features.Kpi;

/// <summary>
/// Catalogue des indicateurs de pilotage publiés pour Grafana (docs/indicateurs-grafana.md). Tous sont des jauges.
/// Un indicateur absent du catalogue n'est jamais publié : tout nouvel indicateur est revu ici, avec la même règle que
/// la télémétrie (annexe G) — pas de montant, pas de nom de client, pas de personne.
/// </summary>
public static class KpiMetrics
{
    // Pilotage d'un dossier (utilisateurs).
    public const string DossierInfo = "lettra_kpi_dossier_info";
    public const string RevenueIndex = "lettra_kpi_revenue_index";
    public const string InvoicesIssued = "lettra_kpi_invoices_issued";
    public const string RevenueTrend = "lettra_kpi_revenue_trend_ratio";
    public const string DaysSalesOutstanding = "lettra_kpi_dso_days";
    public const string OverdueReceivables = "lettra_kpi_overdue_receivables_ratio";
    public const string Collection = "lettra_kpi_collection_ratio";
    public const string TopCustomerShare = "lettra_kpi_top_customer_share_ratio";
    public const string ActiveCustomers = "lettra_kpi_active_customers";
    public const string QuoteConversion = "lettra_kpi_quote_conversion_ratio";
    public const string QuotesPending = "lettra_kpi_quotes_pending";
    public const string QuotesExpiringSoon = "lettra_kpi_quotes_expiring_soon";

    // Vue cabinet (les compteurs de la nuit, relus dans la base Catalog).
    public const string CabinetIndicatorCount = "lettra_kpi_cabinet_indicator_count";
    public const string CabinetIndicatorLevel = "lettra_kpi_cabinet_indicator_level";
    public const string CabinetIndicatorDueDays = "lettra_kpi_cabinet_indicator_due_days";
    public const string CabinetIndicatorAge = "lettra_kpi_cabinet_indicator_age_seconds";

    // Administration (exploitation).
    public const string DatabaseSize = "lettra_kpi_database_size_bytes";
    public const string Devices = "lettra_kpi_devices";
    public const string SilentDevices = "lettra_kpi_devices_silent";
    public const string OldestDeviceSyncAge = "lettra_kpi_device_oldest_sync_age_seconds";
    public const string Accounts = "lettra_kpi_accounts";
    public const string Dossiers = "lettra_kpi_dossiers";
    public const string OpenAlerts = "lettra_kpi_open_alerts";
    public const string CollectionDuration = "lettra_kpi_collect_duration_seconds";
    public const string CollectionFailures = "lettra_kpi_collect_failures";
    public const string CollectedAt = "lettra_kpi_collected_at_seconds";

    /// <summary>Étiquettes propres aux indicateurs (en plus de <c>dossier_id</c>, <c>dossier</c> et <c>cabinet</c>).</summary>
    public static class Labels
    {
        /// <summary>Mois civil, <c>2026-10</c>.</summary>
        public const string Month = "month";

        /// <summary>État technique : <c>active</c>, <c>revoked</c>, <c>suspended</c>…</summary>
        public const string State = "state";

        /// <summary>Clé d'un indicateur de la vue cabinet (<c>factures.retard</c>).</summary>
        public const string Key = "key";

        /// <summary>Gravité d'une alerte : <c>S1</c> à <c>S4</c>.</summary>
        public const string Severity = "severity";
    }

    /// <summary>
    /// Indicateurs de gestion des dossiers (ventes, devis, vue cabinet), retirés quand une cellule SaaS ne publie que
    /// l'exploitation. Le nom du dossier (<see cref="DossierInfo"/>) reste : les tailles et les postes en ont besoin.
    /// </summary>
    public static IReadOnlySet<string> Business { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        RevenueIndex, InvoicesIssued, RevenueTrend, DaysSalesOutstanding, OverdueReceivables, Collection, TopCustomerShare,
        ActiveCustomers, QuoteConversion, QuotesPending, QuotesExpiringSoon, CabinetIndicatorCount, CabinetIndicatorLevel,
        CabinetIndicatorDueDays,
    };

    /// <summary>Le texte d'aide de chaque indicateur publié (ligne <c># HELP</c> de Prometheus).</summary>
    public static IReadOnlyDictionary<string, string> Help { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [DossierInfo] = "Dossier suivi (toujours 1) : relie l'identifiant au nom du dossier et du cabinet.",
        [RevenueIndex] = "Chiffre d'affaires HT du mois, en indice : 100 = mois moyen des 12 derniers mois.",
        [InvoicesIssued] = "Factures et acomptes émis dans le mois (hors avoirs).",
        [RevenueTrend] = "Évolution du chiffre d'affaires HT des 3 derniers mois complets par rapport aux 3 mois d'avant.",
        [DaysSalesOutstanding] = "Délai moyen de paiement des clients (DSO), en jours, sur les ventes TTC des 90 derniers jours.",
        [OverdueReceivables] = "Part des créances clients échues dans le reste à encaisser.",
        [Collection] = "Part encaissée des factures émises sur 12 mois (hors avoirs).",
        [TopCustomerShare] = "Part du premier client dans le chiffre d'affaires HT des 12 derniers mois.",
        [ActiveCustomers] = "Clients facturés dans les 12 derniers mois.",
        [QuoteConversion] = "Devis acceptés sur les devis tranchés des 12 derniers mois (acceptés, refusés ou expirés).",
        [QuotesPending] = "Devis envoyés en attente de réponse, pas encore expirés.",
        [QuotesExpiringSoon] = "Devis en attente qui expirent dans les 7 jours.",
        [CabinetIndicatorCount] = "Compteur d'un indicateur de la vue cabinet (calcul de la nuit).",
        [CabinetIndicatorLevel] = "Urgence d'un indicateur de la vue cabinet : 0 rien, 1 en attente, 2 à traiter, 3 urgent.",
        [CabinetIndicatorDueDays] = "Jours avant l'échéance d'un indicateur de la vue cabinet (négatif : en retard).",
        [CabinetIndicatorAge] = "Âge du dernier calcul de la vue cabinet du dossier.",
        [DatabaseSize] = "Taille de la base du dossier.",
        [Devices] = "Postes enregistrés sur le dossier, par état.",
        [SilentDevices] = "Postes actifs sans synchronisation depuis plus de 7 jours.",
        [OldestDeviceSyncAge] = "Temps écoulé depuis la dernière synchronisation du poste actif le plus en retard.",
        [Accounts] = "Comptes des personnes du cabinet, par état.",
        [Dossiers] = "Dossiers de la cellule, par état.",
        [OpenAlerts] = "Alertes ouvertes (nouvelles ou prises en charge), par gravité.",
        [CollectionDuration] = "Durée de la dernière collecte des indicateurs.",
        [CollectionFailures] = "Dossiers ou sources en échec lors de la dernière collecte.",
        [CollectedAt] = "Horodatage Unix de la dernière collecte des indicateurs.",
    };
}
