using System.Globalization;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Missions;
using Lettra.Printing;
using Lettra.UI.Shared.Components.Explorer;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Missions;

/// <summary>
/// Rédige la lettre de mission d'après la mission saisie, sur le plan de la lettre type de la profession : objet,
/// travaux, durée, engagements de chacun, honoraires, données personnelles, signatures. Les textes viennent des
/// ressources (<c>Mission.Letter.*</c>), dans la langue de l'écran ; le cabinet relit la lettre avant de l'envoyer.
/// </summary>
public static class EngagementLetter
{
    public static PrintableLetter Compose(
        MissionResponse mission,
        CompanyProfileResponse? company,
        string? cabinetName,
        DateOnly today,
        IStringLocalizer localizer,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(culture);
        string T(string key, params object[] args) => string.Format(culture, localizer[key], args);
        string client = company?.Name ?? T("Mission.Letter.ClientFallback");
        string cabinet = string.IsNullOrWhiteSpace(cabinetName) ? T("Mission.Letter.CabinetFallback") : cabinetName;
        string start = mission.StartDate.ToString("D", culture);

        List<string> recipient = [client];
        if (company is not null)
        {
            recipient.Add(company.AddressLine1);
            if (!string.IsNullOrWhiteSpace(company.AddressLine2))
            {
                recipient.Add(company.AddressLine2);
            }

            recipient.Add($"{company.PostalCode} {company.City}");
            if (!string.IsNullOrWhiteSpace(company.Siren))
            {
                recipient.Add(T("Mission.Letter.Siren", company.Siren));
            }
        }

        recipient.Add(T("Mission.Letter.Attention", mission.ClientSignatory));

        return new PrintableLetter(
            culture,
            [cabinet, .. mission.Manager is { } manager ? [T("Mission.Letter.Manager", manager)] : Array.Empty<string>()],
            recipient,
            T("Mission.Letter.Dateline", today.ToString("D", culture)),
            T("Mission.Letter.Title", T($"Mission.Letter.Kind.{mission.Kind}")),
            [
                new(T("Mission.Letter.Object"), [T("Mission.Letter.Object.Text", client, T($"Mission.Letter.Kind.{mission.Kind}"), start), T($"Mission.Letter.Object.{mission.Kind}")]),
                new(T("Mission.Letter.Services"), [T("Mission.Letter.Services.Text"), .. mission.Services.Select(s => "– " + T($"Mission.Service.{s}"))]),
                new(T("Mission.Letter.Duration"), [T("Mission.Letter.Duration.Text", start)]),
                new(T("Mission.Letter.ClientDuties"), [T("Mission.Letter.ClientDuties.Text")]),
                new(T("Mission.Letter.CabinetDuties"), [T("Mission.Letter.CabinetDuties.Text")]),
                new(T("Mission.Letter.Fees"),
                [
                    T("Mission.Letter.Fees.Text", ExplorerText.Money(mission.AnnualFeeExclTax), T($"Mission.Letter.Schedule.{mission.Schedule}"), ExplorerText.Money(mission.FeePerInstalment)),
                    T("Mission.Letter.Fees.Extra"),
                ]),
                new(T("Mission.Letter.Privacy"), [T("Mission.Letter.Privacy.Text")]),
                new(T("Mission.Letter.Closing"), [T("Mission.Letter.Closing.Text")]),
            ],
            [
                new(T("Mission.Letter.Sign.Cabinet"), mission.Manager ?? cabinet),
                new(T("Mission.Letter.Sign.Client", client), mission.ClientSignatory),
            ]);
    }
}
