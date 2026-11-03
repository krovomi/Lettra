using Lettra.Contracts.Insights;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.DemoData;

/// <summary>
/// Le solde bancaire du radar de prévention, que le cabinet saisit d'ordinaire à la main : sans lui, la trésorerie des
/// prochaines semaines ne s'évalue pas. Posé une seule fois par dossier, tant que personne n'a réglé ses seuils (une
/// démonstration déjà remplie le reçoit aussi, un réglage fait à la main reste). Le dossier principal reçoit un solde
/// juste, qui passe sous zéro dans l'horizon s'il a des sorties prévues ; les autres un solde confortable.
/// Par les écrans (droit « régler le radar ») : sans ce droit, rien n'est posé.
/// </summary>
internal sealed class DemoPreventionLoader(IServiceProvider services)
{
    /// <summary>La marge des dossiers confortables, au-delà du point le plus bas de leurs flux prévus.</summary>
    internal const decimal ComfortMargin = 12_000m;

    private readonly IPreventionGateway _prevention = services.GetRequiredService<IPreventionGateway>();

    public async Task SetCashBalanceAsync(bool tight, CancellationToken cancellationToken)
    {
        if ((await _prevention.GetRadarAsync(cancellationToken).ConfigureAwait(false)).Value is not { CanAdjust: true, Settings.Version: 0 } radar)
        {
            return;
        }

        // Un premier solde à zéro donne les flux prévus semaine par semaine ; le solde retenu en découle.
        UpdatePreventionSettingsRequest probe = UpdatePreventionSettingsRequest.From(radar.Settings);
        probe.CashBalance = 0m;
        probe.CashBalanceOn = radar.Today;
        if ((await _prevention.UpdateSettingsAsync(probe, cancellationToken).ConfigureAwait(false)).Value is not { } zero)
        {
            return;
        }

        IReadOnlyList<PreventionWeekDto> flows = (await _prevention.GetRadarAsync(cancellationToken).ConfigureAwait(false)).Value?
            .Signals.FirstOrDefault(s => s.Signal == PreventionSignalDto.Treasury)?.Weeks ?? [];
        UpdatePreventionSettingsRequest settled = UpdatePreventionSettingsRequest.From(zero);
        settled.CashBalance = CashBalance(flows, tight);
        _ = await _prevention.UpdateSettingsAsync(settled, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Juste : la moitié du creux des flux prévus, de sorte que le solde passe sous zéro à mi-chemin (rien à creuser,
    /// un petit solde). Confortable : le creux couvert, plus une marge. Arrondi à la centaine.
    /// </summary>
    internal static decimal CashBalance(IReadOnlyList<PreventionWeekDto> flows, bool tight)
    {
        ArgumentNullException.ThrowIfNull(flows);
        decimal trough = Math.Max(0m, -flows.Select(w => w.Balance).DefaultIfEmpty(0m).Min());
        decimal balance = tight ? (trough > 0m ? trough / 2m : 1_500m) : trough + ComfortMargin;
        return Math.Round(balance / 100m, 0, MidpointRounding.AwayFromZero) * 100m;
    }
}
