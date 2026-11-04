using System.Text.Json.Serialization;

namespace Lettra.Contracts.Insights;

/// <summary>Le niveau d'attention d'un dossier : calme, à surveiller, à appeler.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PreventionLevelDto>))]
public enum PreventionLevelDto
{
    /// <summary>Aucun signal allumé.</summary>
    Calm = 0,

    /// <summary>Au moins un signal à surveiller.</summary>
    Watch = 1,

    /// <summary>Un signal grave, ou assez de signaux à la fois : proposer un rendez-vous au client.</summary>
    Call = 2,
}

/// <summary>Les signaux de difficulté que Lettra lit dans un dossier, dans l'ordre où l'écran les montre.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PreventionSignalDto>))]
public enum PreventionSignalDto
{
    /// <summary>La trésorerie prévue passe sous zéro dans l'horizon réglé (huit semaines par défaut).</summary>
    Treasury = 1,

    /// <summary>Le délai de paiement des clients se dégrade trois mois de suite.</summary>
    PaymentDelay = 2,

    /// <summary>Le chiffre d'affaires décroche par rapport aux dossiers du même secteur (au moins cinq).</summary>
    PeerGap = 3,

    /// <summary>La TVA de la prochaine échéance dépasse les encaissements attendus d'ici là.</summary>
    VatBeyondCollections = 4,

    /// <summary>Le premier client pèse plus que le seuil réglé dans le chiffre d'affaires des douze derniers mois.</summary>
    TopCustomer = 5,

    /// <summary>Des clients du dossier sont en procédure collective (d'après leur dernière vérification au BODACC).</summary>
    CustomersInProceedings = 6,

    /// <summary>L'écart entre facturé et encaissé grandit trois mois de suite.</summary>
    BilledCollectedGap = 7,
}

/// <summary>L'état d'un signal pour le dossier.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PreventionSignalStateDto>))]
public enum PreventionSignalStateDto
{
    /// <summary>Évalué, rien à signaler.</summary>
    Clear = 0,

    /// <summary>Allumé : à surveiller.</summary>
    Watch = 1,

    /// <summary>Allumé : grave, à traiter avec le client.</summary>
    Call = 2,

    /// <summary>Pas assez de données pour conclure ; <see cref="PreventionSignalResponse.Unavailable"/> dit pourquoi.</summary>
    NotEvaluated = 3,

    /// <summary>Coupé par le cabinet pour ce dossier.</summary>
    Disabled = 4,
}

/// <summary>Pourquoi un signal n'a pas pu être évalué.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PreventionUnavailableDto>))]
public enum PreventionUnavailableDto
{
    None = 0,

    /// <summary>Le cabinet n'a pas indiqué de solde de trésorerie pour ce dossier.</summary>
    NoCashBalance = 1,

    /// <summary>Le solde indiqué date de plus de <see cref="PreventionSettingsLimits.CashBalanceMaxAgeDays"/> jours.</summary>
    CashBalanceOutdated = 2,

    /// <summary>Pas assez de ventes sur les mois observés pour suivre une tendance.</summary>
    NotEnoughHistory = 3,

    /// <summary>La fiche entreprise n'a pas de code NAF : pas de secteur, pas de pairs.</summary>
    NoSector = 4,

    /// <summary>Aucune collecte des indicateurs sur ce poste (poste isolé) : pas de pairs.</summary>
    NoCollection = 5,

    /// <summary>Moins de cinq dossiers du même secteur dans le cabinet.</summary>
    TooFewPeers = 6,

    /// <summary>Le dossier n'a pas de ventes sur les six derniers mois : pas d'évolution à comparer.</summary>
    NoTrend = 7,

    /// <summary>Le dossier n'a pas de fiche entreprise.</summary>
    NoCompanyProfile = 8,

    /// <summary>La fiche entreprise n'indique pas le régime de TVA.</summary>
    NoVatRegime = 9,

    /// <summary>L'entreprise est en franchise en base : pas de TVA à reverser.</summary>
    VatFranchise = 10,

    /// <summary>Aucune échéance de TVA à venir dans les quarante-cinq jours.</summary>
    NoVatDeadline = 11,

    /// <summary>Aucune vente sur les douze derniers mois.</summary>
    NoRevenue = 12,

    /// <summary>Aucun client du dossier n'a encore été vérifié auprès des registres.</summary>
    NoCustomerChecked = 13,
}

/// <summary>Comment l'écran met en forme un chiffre d'un signal, selon la langue de l'interface.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PreventionFigureKindDto>))]
public enum PreventionFigureKindDto
{
    /// <summary>Un montant en euros.</summary>
    Amount = 1,

    /// <summary>Un nombre de jours.</summary>
    Days = 2,

    /// <summary>Un pourcentage (12,5 pour 12,5 %).</summary>
    Percent = 3,

    /// <summary>Un nombre entier (clients, semaines, dossiers).</summary>
    Count = 4,

    /// <summary>Une date.</summary>
    Date = 5,

    /// <summary>Un texte tel quel (un nom de client, le code d'une déclaration).</summary>
    Text = 6,
}

/// <summary>
/// Un chiffre cité par la phrase d'un signal. Les phrases sont des ressources de l'écran (<c>{0}</c>, <c>{1}</c>…), en
/// français et en anglais : l'ordre des chiffres de chaque signal est fixe. <paramref name="Name"/> est leur clé stable
/// (celle que « Décisions à prendre » reprendra dans ses faits).
/// </summary>
public sealed record PreventionFigureDto(string Name, PreventionFigureKindDto Kind, decimal? Number = null, DateOnly? Date = null, string? Text = null);

/// <summary>Une semaine de la trésorerie prévue : ce qui entre, ce qui sort, le solde en fin de semaine.</summary>
public sealed record PreventionWeekDto(DateOnly Start, decimal Inflows, decimal Outflows, decimal Balance);

/// <summary>Un mois observé : le délai de paiement à sa fin, ou ce qui a été facturé et encaissé dans le mois.</summary>
public sealed record PreventionMonthDto(DateOnly Month, decimal? DaysSalesOutstanding, decimal? Billed, decimal? Collected);

/// <summary>Un client cité par un signal (premier client, client en procédure collective), avec ce qu'il pèse.</summary>
public sealed record PreventionPartyDto(Guid Id, string Name, decimal Amount, DateOnly? Since);

/// <summary>
/// Un signal du radar, évalué pour le dossier : son état, ses chiffres, l'effet chiffré (<paramref name="Impact"/>, en
/// euros, s'il y en a un) et l'échéance (<paramref name="DueOn"/>, avant quand agir).
/// </summary>
public sealed record PreventionSignalResponse(
    PreventionSignalDto Signal,
    PreventionSignalStateDto State,
    PreventionUnavailableDto Unavailable,
    decimal? Impact,
    DateOnly? DueOn,
    IReadOnlyList<PreventionFigureDto> Figures,
    IReadOnlyList<PreventionWeekDto> Weeks,
    IReadOnlyList<PreventionMonthDto> Months,
    IReadOnlyList<PreventionPartyDto> Parties)
{
    /// <summary>Vrai quand le signal est allumé (à surveiller ou grave).</summary>
    public bool IsActive => State is PreventionSignalStateDto.Watch or PreventionSignalStateDto.Call;
}

/// <summary>
/// Le radar d'un dossier : le niveau d'attention, chaque signal avec sa raison et ses chiffres, et les seuils appliqués.
/// Calculé à la demande dans la base du dossier ; réservé au cabinet (il ne sort jamais vers le client ni l'auditeur).
/// <paramref name="CanAdjust"/> dit si l'utilisateur peut régler les seuils du dossier (<c>PreventionRegler</c>).
/// </summary>
public sealed record PreventionRadarResponse(
    DateOnly Today,
    PreventionLevelDto Level,
    int ActiveSignals,
    IReadOnlyList<PreventionSignalResponse> Signals,
    PreventionSettingsResponse Settings,
    bool CanAdjust = false);

/// <summary>
/// La carte « Signaux de prévention » du dossier : le niveau, les signaux allumés (les plus graves d'abord) et l'échéance
/// la plus proche. <paramref name="Visible"/> est faux pour qui n'a pas le droit de voir le radar : la carte ne s'affiche
/// pas, sans erreur.
/// </summary>
public sealed record PreventionSummaryResponse(bool Visible, PreventionLevelDto Level, int ActiveSignals, IReadOnlyList<PreventionSignalDto> Active, DateOnly? DueOn)
{
    public static PreventionSummaryResponse Hidden { get; } = new(false, PreventionLevelDto.Calm, 0, [], null);
}

/// <summary>Les bornes des seuils réglables (les mêmes que celles du domaine, vérifiées par un test).</summary>
public static class PreventionSettingsLimits
{
    public const int DefaultHorizonWeeks = 8;
    public const int MinHorizonWeeks = 2;
    public const int MaxHorizonWeeks = 26;

    /// <summary>Au-delà, le solde indiqué est trop ancien pour prévoir la trésorerie.</summary>
    public const int CashBalanceMaxAgeDays = 45;

    public const decimal MaxCashBalance = 999_999_999m;

    public const int DefaultPaymentDelayRiseDays = 5;
    public const int MinPaymentDelayRiseDays = 1;
    public const int MaxPaymentDelayRiseDays = 60;

    public const int DefaultPeerGapPoints = 15;
    public const int MinPeerGapPoints = 5;
    public const int MaxPeerGapPoints = 100;

    public const int DefaultTopCustomerSharePercent = 50;
    public const int MinTopCustomerSharePercent = 20;
    public const int MaxTopCustomerSharePercent = 100;

    public const int DefaultBilledCollectedGapPercent = 20;
    public const int MinBilledCollectedGapPercent = 5;
    public const int MaxBilledCollectedGapPercent = 100;

    public const int DefaultCallSignalCount = 3;
    public const int MinCallSignalCount = 2;
    public const int MaxCallSignalCount = 7;
}

/// <summary>
/// Les seuils du radar pour ce dossier, réglés par le cabinet (valeurs proposées par Lettra tant qu'il n'a rien
/// changé, à relire avec un expert-comptable). <paramref name="CashBalance"/> : le solde de trésorerie connu à la date
/// <paramref name="CashBalanceOn"/>, point de départ de la trésorerie prévue. <paramref name="Version"/> vaut 0 tant
/// que rien n'a été enregistré.
/// </summary>
public sealed record PreventionSettingsResponse(
    int TreasuryHorizonWeeks,
    decimal? CashBalance,
    DateOnly? CashBalanceOn,
    int PaymentDelayRiseDays,
    int PeerGapPoints,
    int TopCustomerSharePercent,
    int BilledCollectedGapPercent,
    int CallSignalCount,
    IReadOnlyList<PreventionSignalDto> Disabled,
    long Version,
    DateTimeOffset? UpdatedAt);

/// <summary>Régler les seuils du radar d'un dossier. Mutable pour <c>EditForm</c>.</summary>
public sealed record UpdatePreventionSettingsRequest
{
    public int TreasuryHorizonWeeks { get; set; } = PreventionSettingsLimits.DefaultHorizonWeeks;

    public decimal? CashBalance { get; set; }

    public DateOnly? CashBalanceOn { get; set; }

    public int PaymentDelayRiseDays { get; set; } = PreventionSettingsLimits.DefaultPaymentDelayRiseDays;

    public int PeerGapPoints { get; set; } = PreventionSettingsLimits.DefaultPeerGapPoints;

    public int TopCustomerSharePercent { get; set; } = PreventionSettingsLimits.DefaultTopCustomerSharePercent;

    public int BilledCollectedGapPercent { get; set; } = PreventionSettingsLimits.DefaultBilledCollectedGapPercent;

    public int CallSignalCount { get; set; } = PreventionSettingsLimits.DefaultCallSignalCount;

    public List<PreventionSignalDto> Disabled { get; set; } = [];

    /// <summary>La version lue ; 0 pour un premier réglage.</summary>
    public long Version { get; set; }

    public static UpdatePreventionSettingsRequest From(PreventionSettingsResponse settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new UpdatePreventionSettingsRequest
        {
            TreasuryHorizonWeeks = settings.TreasuryHorizonWeeks,
            CashBalance = settings.CashBalance,
            CashBalanceOn = settings.CashBalanceOn,
            PaymentDelayRiseDays = settings.PaymentDelayRiseDays,
            PeerGapPoints = settings.PeerGapPoints,
            TopCustomerSharePercent = settings.TopCustomerSharePercent,
            BilledCollectedGapPercent = settings.BilledCollectedGapPercent,
            CallSignalCount = settings.CallSignalCount,
            Disabled = [.. settings.Disabled],
            Version = settings.Version,
        };
    }
}

/// <summary>
/// Le courrier type « proposition de rendez-vous de prévention », préparé sans IA : un objet, un texte à relire, et les
/// sujets abordés (les signaux allumés, dits en termes neutres : jamais le niveau d'attention).
/// <paramref name="CanSend"/> est faux quand l'envoi d'e-mails n'est pas configuré : le texte se copie alors à la main.
/// </summary>
public sealed record PreventionLetterResponse(string Language, string Company, string Subject, string Body, IReadOnlyList<PreventionSignalDto> Topics, bool CanSend);

/// <summary>Envoyer le courrier relu par le collaborateur. Mutable pour <c>EditForm</c>.</summary>
public sealed record SendPreventionLetterRequest
{
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 8000;

    public string To { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;
}
