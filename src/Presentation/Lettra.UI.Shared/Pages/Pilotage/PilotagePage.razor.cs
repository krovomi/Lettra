using System.Globalization;
using Lettra.Contracts.Common;
using Lettra.Contracts.Pilotage;
using Lettra.UI.Shared.Components.Lists;
using Lettra.UI.Shared.Components.Palette;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Pages.Pilotage;

/// <summary>Les onglets des tableaux de bord, dans l'ordre d'affichage.</summary>
public enum PilotageTab
{
    Dossier,
    Dossiers,
    Administration,
}

public sealed partial class PilotagePage
{
    private const string NameColumn = "name";

    private readonly CancellationTokenSource _disposed = new();
    private readonly SortState<string> _dossierSort = new(NameColumn);
    private readonly SortState<string> _adminSort = new(NameColumn);
    private PilotageTab _tab = PilotageTab.Dossier;
    private DossierPilotageResponse? _dossier;
    private CabinetPilotageResponse? _cabinet;
    private AdministrationPilotageResponse? _administration;
    private IReadOnlyList<ErrorDetail> _errors = [];
    private ErrorDetail? _dossierError;
    private bool _loading;
    private string? _query;

    /// <summary>Onglet ouvert à l'arrivée : <c>/pilotage?vue=dossiers</c> ou <c>?vue=administration</c>.</summary>
    [SupplyParameterFromQuery(Name = "vue")]
    public string? View { get; set; }

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>« Administration » n'apparaît qu'à qui administre au moins un dossier (le serveur en décide).</summary>
    private IEnumerable<PilotageTab> VisibleTabs =>
        Enum.GetValues<PilotageTab>().Where(t => t != PilotageTab.Administration || _administration is { Available: true, Dossiers.Count: > 0 });

    private string Subtitle => _tab switch
    {
        PilotageTab.Dossier when _dossier is { } d => string.Format(Culture, L["Pilotage.Subtitle.Dossier"], d.Dossier.Name, When(d.ComputedAt)),
        PilotageTab.Dossiers when _cabinet?.CollectedAt is DateTimeOffset at => string.Format(Culture, L["Pilotage.Subtitle.Collected"], When(at)),
        PilotageTab.Administration when _administration?.CollectedAt is DateTimeOffset at => string.Format(Culture, L["Pilotage.Subtitle.Collected"], When(at)),
        _ => L["Pilotage.Subtitle"],
    };

    private IReadOnlyList<SortOption<string>> DossierSortOptions =>
    [
        new(NameColumn, L["Pilotage.Field.Dossier"]),
        new("dso", L["Pilotage.Field.Dso"], SortDirectionDto.Descending),
        new("overdue", L["Pilotage.Field.Overdue"], SortDirectionDto.Descending),
        new("collection", L["Pilotage.Field.Collection"]),
        new("trend", L["Pilotage.Field.Trend"]),
        new("customers", L["Pilotage.Field.Customers"], SortDirectionDto.Descending),
        new("conversion", L["Pilotage.Field.Conversion"], SortDirectionDto.Descending),
    ];

    private IReadOnlyList<SortOption<string>> AdminSortOptions =>
    [
        new(NameColumn, L["Pilotage.Field.Dossier"]),
        new("size", L["Pilotage.Field.Size"], SortDirectionDto.Descending),
        new("devices", L["Pilotage.Field.Devices"], SortDirectionDto.Descending),
        new("revoked", L["Pilotage.Field.Revoked"], SortDirectionDto.Descending),
        new("silent", L["Pilotage.Field.Silent"], SortDirectionDto.Descending),
        new("silence", L["Pilotage.Field.LongestSilence"], SortDirectionDto.Descending),
        new("nightly", L["Pilotage.Field.Nightly"], SortDirectionDto.Descending),
    ];

    private IReadOnlyList<DossierPilotageDto> DossierRows => ListSorting.Sort(
        (_cabinet?.Dossiers ?? []).Where(d => PaletteMatcher.Score(d.Name, _query ?? string.Empty) > 0),
        _dossierSort,
        new Dictionary<string, Func<DossierPilotageDto, object?>>(StringComparer.Ordinal)
        {
            [NameColumn] = d => d.Name,
            ["dso"] = d => d.DaysSalesOutstanding,
            ["overdue"] = d => d.OverdueReceivablesRatio,
            ["collection"] = d => d.CollectionRatio,
            ["trend"] = d => d.RevenueTrendRatio,
            ["customers"] = d => d.ActiveCustomers,
            ["conversion"] = d => d.QuoteConversionRatio,
        });

    private IReadOnlyList<DossierOperationsDto> AdminRows => ListSorting.Sort(
        _administration?.Dossiers ?? [],
        _adminSort,
        new Dictionary<string, Func<DossierOperationsDto, object?>>(StringComparer.Ordinal)
        {
            [NameColumn] = d => d.Name,
            ["size"] = d => d.DatabaseSizeBytes,
            ["devices"] = d => d.ActiveDevices,
            ["revoked"] = d => d.RevokedDevices,
            ["silent"] = d => d.SilentDevices,
            ["silence"] = d => d.OldestDeviceSyncAgeSeconds,
            ["nightly"] = d => d.CabinetIndicatorAgeSeconds,
        });

    protected override async Task OnInitializedAsync()
    {
        _tab = View?.ToLowerInvariant() switch
        {
            "dossiers" => PilotageTab.Dossiers,
            "administration" => PilotageTab.Administration,
            _ => PilotageTab.Dossier,
        };

        try
        {
            await LoadAsync();
        }
        catch (OperationCanceledException) when (_disposed.IsCancellationRequested)
        {
            // Écran quitté pendant la lecture.
        }
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            // Les trois lectures partent ensemble : l'onglet affiché ne fait pas attendre les deux autres plus longtemps.
            Task<OperationResult<DossierPilotageResponse>> dossier = Gateway.GetDossierAsync(_disposed.Token);
            Task<OperationResult<CabinetPilotageResponse>> cabinet = Gateway.GetCabinetAsync(_disposed.Token);
            Task<OperationResult<AdministrationPilotageResponse>> administration = Gateway.GetAdministrationAsync(_disposed.Token);
            OperationResult<DossierPilotageResponse> d = await dossier;
            OperationResult<CabinetPilotageResponse> c = await cabinet;
            OperationResult<AdministrationPilotageResponse> a = await administration;

            // Dossier illisible (droit retiré, aucun dossier ouvert) : l'onglet le dit, les autres restent utilisables.
            _dossier = d.IsSuccess ? d.Value : null;
            _dossierError = d.IsSuccess || d.Errors.Count == 0 ? null : d.Errors[0];
            _cabinet = c.IsSuccess ? c.Value : _cabinet ?? new CabinetPilotageResponse(true, null, []);
            // Refusé (aucun dossier administré) : l'onglet reste caché, sans message d'erreur.
            _administration = a.IsSuccess ? a.Value : null;
            _errors = c.Errors;

            if (_tab == PilotageTab.Administration && !VisibleTabs.Contains(PilotageTab.Administration))
            {
                _tab = PilotageTab.Dossier;
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task ReloadAsync()
    {
        await LoadAsync();
        if (_errors.Count == 0 && _dossierError is null)
        {
            Toasts.Show(ToastKind.Success, L["Pilotage.Refreshed"]);
        }
        else
        {
            Toasts.Show(ToastKind.Error, L.Error((_dossierError ?? _errors[0]).Code));
        }
    }

    private void SetTab(PilotageTab tab)
    {
        _tab = tab;
        string? view = tab == PilotageTab.Dossier ? null : tab.ToString().ToLowerInvariant();
        Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("vue", view), replace: true);
    }

    /// <summary>Ouvre le dossier choisi et montre son tableau de bord.</summary>
    private async Task OpenAsync(DossierPilotageDto row)
    {
        if (row.DossierId != Dossiers.Id)
        {
            if (!await Dossiers.SwitchAsync(row.DossierId))
            {
                return;
            }

            Toasts.Show(ToastKind.Success, string.Format(Culture, L["Cabinet.Picker.Opened"], row.Name));
            _dossier = null;
            OperationResult<DossierPilotageResponse> loaded = await Gateway.GetDossierAsync(_disposed.Token);
            _dossier = loaded.IsSuccess ? loaded.Value : null;
            _dossierError = loaded.IsSuccess || loaded.Errors.Count == 0 ? null : loaded.Errors[0];
        }

        SetTab(PilotageTab.Dossier);
    }

    private static string TabIcon(PilotageTab tab) => tab switch
    {
        PilotageTab.Dossier => "folder",
        PilotageTab.Dossiers => "layers",
        _ => "shield-check",
    };

    private string IndicatorLabel(string key)
    {
        LocalizedString text = L["Cabinet.Indicator." + key];
        return text.ResourceNotFound ? key : text.Value;
    }

    /// <summary>L'écran où traiter chaque point (les clés inconnues mènent à la vue cabinet).</summary>
    private static string ToDoHref(string key) => key switch
    {
        Contracts.Cabinet.DossierIndicatorKeys.OverdueInvoices or Contracts.Cabinet.DossierIndicatorKeys.PlatformRejections => "invoices",
        Contracts.Cabinet.DossierIndicatorKeys.VatToDeclare => "tax",
        Contracts.Cabinet.DossierIndicatorKeys.BankUnreconciled => "bank",
        Contracts.Cabinet.DossierIndicatorKeys.OpenAlerts => "alerts",
        Contracts.Cabinet.DossierIndicatorKeys.ReviewPending => "review",
        _ => "cabinet",
    };

    private string ToDoLine(DateTimeOffset? at) =>
        at is DateTimeOffset value ? string.Format(Culture, L["Pilotage.ToDo.Computed"], When(value)) : L["Pilotage.ToDo.NeverShort"];

    private string When(DateTimeOffset at)
    {
        DateTimeOffset local = at.ToLocalTime();
        return local.Date == DateTime.Today
            ? string.Format(Culture, L["Cabinet.TodayAt"], local.ToString("t", Culture))
            : local.ToString("g", Culture);
    }

    public void Dispose()
    {
        // Annulé sans être libéré : une lecture encore en cours lit son jeton sans lever ObjectDisposedException.
        _disposed.Cancel();
    }
}
