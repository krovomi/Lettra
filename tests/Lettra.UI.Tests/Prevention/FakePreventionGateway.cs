using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Prevention;

/// <summary>
/// Le radar d'un dossier en mémoire : une trésorerie qui passe sous zéro, des délais qui s'allongent, une TVA calme,
/// un client en procédure collective, une comparaison au secteur coupée et un solde à saisir selon le cas.
/// </summary>
internal sealed class FakePreventionGateway : IPreventionGateway
{
    public static readonly DateOnly Today = new(2026, 11, 3);
    public static readonly Guid Fournil = new("0199e3a0-0000-7000-8000-0000000c0001");

    public static PreventionSettingsResponse DefaultSettings { get; } = new(
        PreventionSettingsLimits.DefaultHorizonWeeks,
        8_000m,
        new DateOnly(2026, 11, 2),
        PreventionSettingsLimits.DefaultPaymentDelayRiseDays,
        PreventionSettingsLimits.DefaultPeerGapPoints,
        PreventionSettingsLimits.DefaultTopCustomerSharePercent,
        PreventionSettingsLimits.DefaultBilledCollectedGapPercent,
        PreventionSettingsLimits.DefaultCallSignalCount,
        [PreventionSignalDto.PeerGap],
        3,
        new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));

    public static PreventionSignalResponse Treasury { get; } = new(
        PreventionSignalDto.Treasury,
        PreventionSignalStateDto.Call,
        PreventionUnavailableDto.None,
        4_250m,
        new DateOnly(2026, 11, 16),
        [
            new("week", PreventionFigureKindDto.Date, Date: new DateOnly(2026, 11, 16)),
            new("lowestBalance", PreventionFigureKindDto.Amount, -4_250m),
            new("cashBalance", PreventionFigureKindDto.Amount, 8_000m),
            new("cashBalanceOn", PreventionFigureKindDto.Date, Date: new DateOnly(2026, 11, 2)),
            new("horizonWeeks", PreventionFigureKindDto.Count, 8m),
        ],
        [
            new(new DateOnly(2026, 11, 2), 1_200m, 3_000m, 6_200m),
            new(new DateOnly(2026, 11, 9), 500m, 4_000m, 2_700m),
            new(new DateOnly(2026, 11, 16), 0m, 6_950m, -4_250m),
        ],
        [],
        []);

    public static PreventionSignalResponse PaymentDelay { get; } = new(
        PreventionSignalDto.PaymentDelay,
        PreventionSignalStateDto.Watch,
        PreventionUnavailableDto.None,
        3_000m,
        null,
        [
            new("from", PreventionFigureKindDto.Days, 30m),
            new("to", PreventionFigureKindDto.Days, 45m),
            new("rise", PreventionFigureKindDto.Days, 15m),
            new("threshold", PreventionFigureKindDto.Days, 5m),
            new("tiedUp", PreventionFigureKindDto.Amount, 3_000m),
        ],
        [],
        [
            new(new DateOnly(2026, 8, 1), 30m, null, null),
            new(new DateOnly(2026, 9, 1), 38m, null, null),
            new(new DateOnly(2026, 10, 1), 45m, null, null),
        ],
        []);

    public static PreventionSignalResponse Vat { get; } = new(
        PreventionSignalDto.VatBeyondCollections,
        PreventionSignalStateDto.Clear,
        PreventionUnavailableDto.None,
        null,
        null,
        [
            new("obligation", PreventionFigureKindDto.Text, Text: "VatReturnCa3"),
            new("dueDate", PreventionFigureKindDto.Date, Date: new DateOnly(2026, 11, 24)),
            new("vat", PreventionFigureKindDto.Amount, 1_800m),
            new("collections", PreventionFigureKindDto.Amount, 5_400m),
            new("periodStart", PreventionFigureKindDto.Date, Date: new DateOnly(2026, 10, 1)),
            new("periodEnd", PreventionFigureKindDto.Date, Date: new DateOnly(2026, 10, 31)),
        ],
        [],
        [],
        []);

    public static PreventionSignalResponse Proceedings { get; } = new(
        PreventionSignalDto.CustomersInProceedings,
        PreventionSignalStateDto.Watch,
        PreventionUnavailableDto.None,
        2_400m,
        new DateOnly(2026, 12, 20),
        [
            new("count", PreventionFigureKindDto.Count, 1m),
            new("outstanding", PreventionFigureKindDto.Amount, 2_400m),
            new("checked", PreventionFigureKindDto.Count, 4m),
            new("customers", PreventionFigureKindDto.Count, 6m),
        ],
        [],
        [],
        [new(Fournil, "Le Fournil d'Aurélie", 2_400m, new DateOnly(2026, 10, 20))]);

    public static PreventionSignalResponse PeerGapDisabled { get; } = new(
        PreventionSignalDto.PeerGap, PreventionSignalStateDto.Disabled, PreventionUnavailableDto.None, null, null, [], [], [], []);

    public static PreventionSignalResponse TopCustomerUnavailable { get; } = new(
        PreventionSignalDto.TopCustomer, PreventionSignalStateDto.NotEvaluated, PreventionUnavailableDto.NoRevenue, null, null, [], [], [], []);

    public static PreventionSignalResponse BilledCollectedUnavailable { get; } = new(
        PreventionSignalDto.BilledCollectedGap, PreventionSignalStateDto.NotEvaluated, PreventionUnavailableDto.NotEnoughHistory, null, null, [], [], [], []);

    /// <summary>Le radar rendu au premier appel ; <see cref="RadarAfterSave"/> le remplace après un réglage enregistré.</summary>
    public PreventionRadarResponse Radar { get; set; } = Build(DefaultSettings, canAdjust: true);

    public PreventionRadarResponse? RadarAfterSave { get; set; }

    public IReadOnlyList<ErrorDetail>? RadarErrors { get; set; }

    public int RadarReads { get; private set; }

    public PreventionSummaryResponse Summary { get; set; } = new(true, PreventionLevelDto.Call, 3, [PreventionSignalDto.Treasury, PreventionSignalDto.PaymentDelay, PreventionSignalDto.CustomersInProceedings], new DateOnly(2026, 11, 16));

    public List<UpdatePreventionSettingsRequest> Saved { get; } = [];

    public IReadOnlyList<ErrorDetail>? SaveErrors { get; set; }

    public bool CanSend { get; set; } = true;

    public List<string?> LetterLanguages { get; } = [];

    public List<SendPreventionLetterRequest> Sent { get; } = [];

    public IReadOnlyList<ErrorDetail>? SendErrors { get; set; }

    public static PreventionRadarResponse Build(PreventionSettingsResponse settings, bool canAdjust, params PreventionSignalResponse[] signals)
    {
        PreventionSignalResponse[] all = signals.Length > 0
            ? signals
            : [Treasury, PaymentDelay, PeerGapDisabled, Vat, TopCustomerUnavailable, Proceedings, BilledCollectedUnavailable];
        int active = all.Count(s => s.IsActive);
        PreventionLevelDto level = all.Any(s => s.State == PreventionSignalStateDto.Call) || active >= settings.CallSignalCount
            ? PreventionLevelDto.Call
            : active > 0 ? PreventionLevelDto.Watch : PreventionLevelDto.Calm;
        return new PreventionRadarResponse(Today, level, active, all, settings, canAdjust);
    }

    public Task<OperationResult<PreventionRadarResponse>> GetRadarAsync(CancellationToken cancellationToken)
    {
        RadarReads++;
        return Task.FromResult(RadarErrors is { } errors
            ? OperationResult<PreventionRadarResponse>.Failure(errors)
            : OperationResult<PreventionRadarResponse>.Success(Saved.Count > 0 && RadarAfterSave is { } after ? after : Radar));
    }

    public Task<OperationResult<PreventionSummaryResponse>> GetSummaryAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<PreventionSummaryResponse>.Success(Summary));

    public Task<OperationResult<PreventionSettingsResponse>> UpdateSettingsAsync(UpdatePreventionSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (SaveErrors is { } errors)
        {
            return Task.FromResult(OperationResult<PreventionSettingsResponse>.Failure(errors));
        }

        Saved.Add(request with { Disabled = [.. request.Disabled] });
        PreventionSettingsResponse saved = new(
            request.TreasuryHorizonWeeks,
            request.CashBalance,
            request.CashBalance is null ? null : request.CashBalanceOn,
            request.PaymentDelayRiseDays,
            request.PeerGapPoints,
            request.TopCustomerSharePercent,
            request.BilledCollectedGapPercent,
            request.CallSignalCount,
            [.. request.Disabled],
            request.Version + 1,
            DateTimeOffset.UtcNow);
        return Task.FromResult(OperationResult<PreventionSettingsResponse>.Success(saved));
    }

    public Task<OperationResult<PreventionLetterResponse>> GetLetterAsync(string? language, CancellationToken cancellationToken)
    {
        LetterLanguages.Add(language);
        PreventionLetterResponse letter = language == "en-US"
            ? new("en-US", "Boulangerie Martin", "Meeting proposal: reviewing your business together", "Dear Sir or Madam,\n\nWe follow the activity of Boulangerie Martin closely.\n\n- cash in the coming weeks\n- customer payment times", [PreventionSignalDto.Treasury, PreventionSignalDto.PaymentDelay], CanSend)
            : new("fr-FR", "Boulangerie Martin", "Proposition de rendez-vous : faire le point ensemble", "Madame, Monsieur,\n\nNous suivons de près l'activité de votre entreprise, Boulangerie Martin.\n\n- la trésorerie des prochaines semaines\n- les délais de paiement des clients", [PreventionSignalDto.Treasury, PreventionSignalDto.PaymentDelay], CanSend);
        return Task.FromResult(OperationResult<PreventionLetterResponse>.Success(letter));
    }

    public Task<OperationResult<bool>> SendLetterAsync(SendPreventionLetterRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (SendErrors is { } errors)
        {
            return Task.FromResult(OperationResult<bool>.Failure(errors));
        }

        Sent.Add(request with { });
        return Task.FromResult(OperationResult<bool>.Success(true));
    }
}
