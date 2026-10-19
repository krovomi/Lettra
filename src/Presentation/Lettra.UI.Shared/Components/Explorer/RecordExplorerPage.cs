using System.Globalization;
using Lettra.Contracts.Common;
using Lettra.UI.Shared.Components.Feedback;
using Lettra.UI.Shared.Keyboard;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Components.Explorer;

/// <summary>Réglages communs des pages de consultation.</summary>
public static class ExplorerSettings
{
    /// <summary>Délai avant de relancer la recherche pendant la frappe (raccourci par les tests).</summary>
    public static TimeSpan SearchDelay { get; set; } = TimeSpan.FromMilliseconds(250);
}

/// <summary>Ce que montre une page de consultation : la liste seule, une fiche, ou une fiche en saisie.</summary>
public enum ExplorerMode
{
    /// <summary><c>/clients</c> : la liste ; sur ordinateur, la fiche affiche un résumé.</summary>
    List,

    /// <summary><c>/clients/{id}</c> : la fiche en lecture.</summary>
    View,

    /// <summary><c>/clients/new</c> : création, la fiche vide en saisie.</summary>
    New,

    /// <summary><c>/clients/{id}/edit</c> : la fiche en saisie.</summary>
    Edit,
}

/// <summary>
/// Page de consultation d'une entité (clients, fournisseurs, articles) : une liste avec recherche et une fiche, sur un
/// seul écran. On consulte en lecture et on passe en saisie sans changer d'écran : Nouveau (Insert), Modifier (F2),
/// Échap pour revenir. Chaque état a son adresse (<c>/base</c>, <c>/base/new</c>, <c>/base/{id}</c>,
/// <c>/base/{id}/edit</c>) : liens profonds, retour arrière du navigateur et du téléphone. Une saisie non enregistrée
/// n'est jamais perdue sans le dire : quitter la fiche demande confirmation, sans boîte de dialogue du navigateur
/// (même comportement dans MAUI).
/// </summary>
/// <typeparam name="TSummary">Ligne de la liste.</typeparam>
/// <typeparam name="TRecord">Fiche complète, avec sa version (ADR-0013).</typeparam>
/// <typeparam name="TForm">Contrat de saisie, un record : l'égalité de valeur dit s'il y a des modifications.</typeparam>
public abstract class RecordExplorerPage<TSummary, TRecord, TForm> : ComponentBase, IDisposable
    where TRecord : class
    where TForm : class
{
    public const int PageSize = 50;

    private CancellationTokenSource? _searchDebounce;
    private string? _search;
    private string? _lastLocation;
    private string? _pendingNavigation;
    private bool _leaving;
    private bool _focusForm;
    private bool _disposed;
    private EditContext? _formContext;

    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    [Inject] protected IStringLocalizer<SharedResource> L { get; set; } = default!;

    [Inject] protected ToastService Toasts { get; set; } = default!;

    protected ExplorerMode Mode { get; private set; } = ExplorerMode.List;

    protected Guid? SelectedId { get; private set; }

    protected List<TSummary> Items { get; } = [];

    protected int TotalCount { get; private set; }

    protected bool Loading { get; private set; } = true;

    protected bool LoadingRecord { get; private set; }

    protected TRecord? Record { get; private set; }

    protected TForm? Form { get; set; }

    protected TForm? Original { get; private set; }

    /// <summary>Erreurs de la liste ou de la fiche (droits, serveur injoignable, validation).</summary>
    protected IReadOnlyList<ErrorDetail> Errors { get; set; } = [];

    protected bool Busy { get; private set; }

    /// <summary>Fiche modifiée par quelqu'un d'autre pendant la saisie (refus 412) : ses valeurs actuelles.</summary>
    protected TRecord? StaleRecord { get; private set; }

    /// <summary>Échap ou Annuler sur une saisie modifiée : on demande avant d'abandonner.</summary>
    protected bool ConfirmingDiscard { get; private set; }

    /// <summary>
    /// Contexte de l'EditForm de la fiche, lié au formulaire en cours. Chaque champ modifié redessine la page, même
    /// quand les champs sont dans un composant à part : « Modifications non enregistrées » suit la frappe.
    /// </summary>
    protected EditContext? FormContext
    {
        get
        {
            if (Form is null)
            {
                return null;
            }

            if (_formContext is null || !ReferenceEquals(_formContext.Model, Form))
            {
                if (_formContext is not null)
                {
                    _formContext.OnFieldChanged -= OnFormFieldChanged;
                }

                _formContext = new EditContext(Form);
                _formContext.OnFieldChanged += OnFormFieldChanged;
            }

            return _formContext;
        }
    }

    protected bool IsDirty => Form is not null && Original is not null && !Form.Equals(Original);

    protected bool IsEditing => Mode is ExplorerMode.New or ExplorerMode.Edit;

    protected bool HasMore => Items.Count < TotalCount;

    /// <summary>Lignes affichées : la liste, moins les filtres appliqués à l'écran (nature d'article).</summary>
    protected virtual IReadOnlyList<TSummary> VisibleItems => Items;

    protected KeyboardScope PageScope { get; private set; } = default!;

    protected ElementReference SearchInput { get; set; }

    protected ElementReference ListElement { get; set; }

    /// <summary>Recherche liée au champ de la liste : relance la liste après une courte pause de frappe.</summary>
    protected string? Search
    {
        get => _search;
        set
        {
            if (string.Equals(_search, value, StringComparison.Ordinal))
            {
                return;
            }

            _search = value;
            _ = ReloadAfterPauseAsync();
        }
    }

    /// <summary>Segment d'adresse de l'entité, sans barre (<c>customers</c>).</summary>
    protected abstract string BasePath { get; }

    /// <summary>Préfixe des clés de ressources communes à la page (<c>Customers</c>).</summary>
    protected abstract string ResourcePrefix { get; }

    protected abstract Task<OperationResult<PagedResponse<TSummary>>> LoadPageAsync(string? search, int page, CancellationToken cancellationToken);

    protected abstract Task<OperationResult<TRecord>> GetAsync(Guid id, CancellationToken cancellationToken);

    protected abstract Task<OperationResult<TRecord>> CreateAsync(TForm form, CancellationToken cancellationToken);

    protected abstract Task<OperationResult<TRecord>> UpdateAsync(Guid id, long expectedVersion, TForm form, CancellationToken cancellationToken);

    /// <summary>Formulaire vide ; <paramref name="name"/> vient de la recherche sans résultat (« Créer « Dupont » »).</summary>
    protected abstract TForm NewForm(string? name);

    protected abstract TForm ToForm(TRecord record);

    /// <summary>Copie du formulaire (<c>form with { }</c>) : l'original reste intact pendant la saisie.</summary>
    protected abstract TForm Copy(TForm form);

    protected abstract Guid IdOf(TSummary item);

    protected abstract Guid IdOf(TRecord record);

    protected abstract long VersionOf(TRecord record);

    /// <summary>Le premier champ du formulaire, qui reçoit le focus en passant en saisie.</summary>
    protected virtual ElementReference? FirstField => null;

    /// <summary>Appelée quand une fiche est chargée en lecture, pour ses compléments (activité, totaux).</summary>
    protected virtual Task OnRecordLoadedAsync(TRecord record) => Task.CompletedTask;

    protected string Href(Guid id) => $"{BasePath}/{id}";

    protected string EditHref(Guid id) => $"{BasePath}/{id}/edit";

    protected string NewHref => $"{BasePath}/new";

    protected string Text(string suffix) => L[$"{ResourcePrefix}.{suffix}"];

    protected override void OnInitialized()
    {
        PageScope = new KeyboardScope("page")
            .Bind("F3", L["Explorer.Command.Search"], async _ => await SearchInput.FocusAsync())
            .Bind("Insert", L["Explorer.Command.New"], _ => GoTo(NewHref))
            .Bind("F2", L["Explorer.Command.Edit"], _ => GoTo(EditHref(SelectedId!.Value)), _ => Mode == ExplorerMode.View && SelectedId is not null)
            .Bind("Escape", L["Explorer.Command.Back"], _ => Rendered(EscapeFromPage), _ => Mode != ExplorerMode.List || ConfirmingRow is not null, showInBar: false);
        Navigation.LocationChanged += OnLocationChanged;
    }

    /// <summary>Échap ferme d'abord la confirmation d'une corbeille ouverte, sinon revient en arrière.</summary>
    private void EscapeFromPage()
    {
        if (ConfirmingRow is { } trash)
        {
            trash.Cancel();
        }
        else
        {
            Back();
        }
    }

    /// <summary>Un raccourci arrive hors des événements de la page : on la redessine après l'action.</summary>
    private void Rendered(Action action)
    {
        action();
        StateHasChanged();
    }

    protected override async Task OnInitializedAsync()
    {
        await SyncWithLocationAsync();
        await ReloadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusForm && FirstField is { } field)
        {
            _focusForm = false;
            await field.FocusAsync();
        }
    }

    // ---------- liste ----------

    protected async Task ReloadAsync()
    {
        Loading = true;
        OperationResult<PagedResponse<TSummary>> result = await LoadPageAsync(ListSearch.Normalize(_search), 1, CancellationToken.None);
        Items.Clear();
        if (result is { IsSuccess: true, Value: { } page })
        {
            Items.AddRange(page.Items);
            TotalCount = page.TotalCount;
        }
        else
        {
            TotalCount = 0;
            Errors = result.Errors;
        }

        Loading = false;
    }

    protected async Task LoadMoreAsync()
    {
        int next = (Items.Count / PageSize) + 1;
        OperationResult<PagedResponse<TSummary>> result = await LoadPageAsync(ListSearch.Normalize(_search), next, CancellationToken.None);
        if (result is { IsSuccess: true, Value: { } page })
        {
            Items.AddRange(page.Items);
            TotalCount = page.TotalCount;
        }
        else
        {
            Errors = result.Errors;
        }
    }

    private async Task ReloadAfterPauseAsync()
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        CancellationTokenSource debounce = new();
        _searchDebounce = debounce;
        try
        {
            await Task.Delay(ExplorerSettings.SearchDelay, debounce.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await InvokeAsync(async () =>
        {
            await ReloadAsync();
            StateHasChanged();
        });
    }

    /// <summary>Flèches haut et bas dans la liste : la fiche suit la sélection, comme une messagerie.</summary>
    protected void OnListKeyDown(KeyboardEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        IReadOnlyList<TSummary> items = VisibleItems;
        if (items.Count == 0 || IsEditing)
        {
            return;
        }

        // Confirmation d'une corbeille ouverte : Entrée la valide (Échap passe par le raccourci de la page).
        if (e.Key == "Enter" && ConfirmingRow is { } confirming)
        {
            _ = confirming.ConfirmAsync();
            return;
        }

        int index = -1;
        for (int i = 0; i < items.Count && SelectedId is not null; i++)
        {
            if (IdOf(items[i]) == SelectedId)
            {
                index = i;
                break;
            }
        }

        int target = e.Key switch
        {
            "ArrowDown" => Math.Min(index + 1, items.Count - 1),
            "ArrowUp" => Math.Max(index - 1, 0),
            "Home" => 0,
            "End" => items.Count - 1,
            _ => -1,
        };
        if (target >= 0 && target != index)
        {
            GoTo(Href(IdOf(items[target])));
        }
        else if (e.Key == "Enter" && index >= 0)
        {
            GoTo(EditHref(IdOf(items[index])));
        }
        else if (e.Key == "Delete" && index >= 0 && RowDeletes.TryGetValue(IdOf(items[index]), out RowDelete? trash))
        {
            _ = trash.StartAsync();
        }
    }

    private void OnFormFieldChanged(object? sender, FieldChangedEventArgs e) => StateHasChanged();

    protected string? OptionId(TSummary item) => $"{BasePath}-option-{IdOf(item)}";

    protected string? ActiveOptionId => SelectedId is Guid id && VisibleItems.Any(i => IdOf(i) == id) ? $"{BasePath}-option-{id}" : null;

    protected bool IsSelected(TSummary item) => SelectedId == IdOf(item);

    /// <summary>Recherche sans résultat : crée la fiche avec ce nom.</summary>
    protected string CreateFromSearchHref => string.IsNullOrWhiteSpace(_search)
        ? NewHref
        : $"{NewHref}?name={Uri.EscapeDataString(_search.Trim())}";

    // ---------- adresse ----------

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _ = InvokeAsync(async () =>
        {
            await SyncWithLocationAsync();
            StateHasChanged();
        });
    }

    /// <summary>Le mode et la fiche se déduisent de l'adresse : <c>base</c>, <c>base/new</c>, <c>base/{id}</c>, <c>base/{id}/edit</c>.</summary>
    private async Task SyncWithLocationAsync()
    {
        string location = Navigation.Uri;
        if (string.Equals(location, _lastLocation, StringComparison.Ordinal))
        {
            return;
        }

        _lastLocation = location;
        string relative = Navigation.ToBaseRelativePath(location);
        string[] parts = relative.Split('?', 2);
        string[] segments = parts[0].Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || !string.Equals(segments[0], BasePath.Split('/')[^1], StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        int depth = BasePath.Split('/').Length;
        string? first = segments.Length > depth ? segments[depth] : null;
        string? second = segments.Length > depth + 1 ? segments[depth + 1] : null;
        ConfirmingDiscard = false;
        _pendingNavigation = null;

        if (string.Equals(first, "new", StringComparison.OrdinalIgnoreCase))
        {
            Mode = ExplorerMode.New;
            SelectedId = null;
            Record = null;
            StaleRecord = null;
            Form = NewForm(QueryValue(parts.Length > 1 ? parts[1] : null, "name"));
            Original = Copy(Form);
            Errors = [];
            _focusForm = true;
            return;
        }

        if (first is not null && Guid.TryParse(first, CultureInfo.InvariantCulture, out Guid id))
        {
            bool edit = string.Equals(second, "edit", StringComparison.OrdinalIgnoreCase);
            if (SelectedId != id || Record is null)
            {
                SelectedId = id;
                await LoadRecordAsync(id);
            }

            if (Record is null)
            {
                Mode = ExplorerMode.View;
                return;
            }

            Mode = edit ? ExplorerMode.Edit : ExplorerMode.View;
            if (edit)
            {
                Form = ToForm(Record);
                Original = Copy(Form);
                StaleRecord = null;
                _focusForm = true;
            }
            else
            {
                Form = null;
                Original = null;
            }

            return;
        }

        Mode = ExplorerMode.List;
        SelectedId = null;
        Record = null;
        Form = null;
        Original = null;
    }

    private async Task LoadRecordAsync(Guid id)
    {
        LoadingRecord = true;
        Record = null;
        Errors = [];
        OperationResult<TRecord> result = await GetAsync(id, CancellationToken.None);
        LoadingRecord = false;
        if (result is { IsSuccess: true, Value: { } record })
        {
            Record = record;
            await OnRecordLoadedAsync(record);
        }
        else
        {
            Errors = result.Errors;
        }
    }

    private static string? QueryValue(string? query, string name)
    {
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        foreach (string pair in query.Split('&'))
        {
            string[] kv = pair.Split('=', 2);
            if (kv.Length == 2 && string.Equals(kv[0], name, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(kv[1].Replace('+', ' '));
            }
        }

        return null;
    }

    protected void GoTo(string href)
    {
        Navigation.NavigateTo(href);
    }

    // ---------- suppression ----------

    /// <summary>Corbeilles des lignes de la liste, pour la touche Suppr sur la ligne active.</summary>
    protected Dictionary<Guid, RowDelete> RowDeletes { get; } = [];

    /// <summary>La corbeille dont la confirmation est ouverte, s'il y en a une.</summary>
    private RowDelete? ConfirmingRow => RowDeletes.Values.FirstOrDefault(r => r.IsConfirming);

    /// <summary>Suppression d'un élément de la liste ; les pages qui n'en offrent pas gardent ce refus.</summary>
    protected virtual Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<bool>.Failure(ErrorCodes.Api.Unexpected));

    /// <summary>
    /// Corbeille d'une ligne, après confirmation : l'élément quitte la liste (et la fiche, s'il y était ouvert), puis le
    /// toaster le dit (« {Préfixe}.Toast.Deleted »). Fiche ouverte : la version lue protège d'une modification faite entre-temps.
    /// </summary>
    protected async Task DeleteRowAsync(TSummary item, string name)
    {
        Guid id = IdOf(item);
        long? version = SelectedId == id && Record is { } open && IdOf(open) == id ? VersionOf(open) : null;
        OperationResult<bool> deleted = await DeleteAsync(id, version, CancellationToken.None);
        if (!deleted.IsSuccess)
        {
            Errors = deleted.Errors;
            return;
        }

        Errors = [];
        Items.Remove(item);
        RowDeletes.Remove(id);
        TotalCount = Math.Max(0, TotalCount - 1);
        if (SelectedId == id)
        {
            GoTo(BasePath);
        }

        Toasts.Show(ToastKind.Success, string.Format(CultureInfo.CurrentCulture, Text("Toast.Deleted"), name));
    }

    // ---------- saisie ----------

    /// <summary>Échap ou Annuler : de la saisie à la lecture (ou à la liste pour une création), de la fiche à la liste.</summary>
    protected void Back()
    {
        if (IsEditing && IsDirty && !ConfirmingDiscard)
        {
            ConfirmingDiscard = true;
            return;
        }

        DiscardAndLeave(Mode == ExplorerMode.Edit && SelectedId is Guid id ? Href(id) : BasePath);
    }

    protected void KeepEditing()
    {
        ConfirmingDiscard = false;
        _pendingNavigation = null;
        _focusForm = true;
    }

    /// <summary>Abandonne la saisie et va où l'on voulait aller (Annuler, Échap, ou un lien cliqué pendant la saisie).</summary>
    protected void DiscardAndLeave(string? target = null)
    {
        string destination = target ?? _pendingNavigation ?? (SelectedId is Guid id ? Href(id) : BasePath);
        Form = Original is null ? null : Copy(Original);
        ConfirmingDiscard = false;
        _pendingNavigation = null;
        _leaving = true;
        GoTo(destination);
        _leaving = false;
    }

    /// <summary>Un lien suivi pendant une saisie modifiée : on reste, et on demande.</summary>
    protected void OnBeforeInternalNavigation(LocationChangingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_leaving || !IsEditing || !IsDirty)
        {
            return;
        }

        context.PreventNavigation();
        _pendingNavigation = context.TargetLocation;
        ConfirmingDiscard = true;
        StateHasChanged();
    }

    protected async Task SaveAsync()
    {
        if (Form is null || Busy)
        {
            return;
        }

        Busy = true;
        try
        {
            bool creating = Mode == ExplorerMode.New;
            OperationResult<TRecord> result = creating
                ? await CreateAsync(Form, CancellationToken.None)
                : await UpdateAsync(SelectedId!.Value, VersionOf(StaleRecord ?? Record!), Form, CancellationToken.None);
            await OnSavedAsync(result, creating ? "Created" : "Saved");
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Fiche modifiée entre-temps : reprendre ses valeurs actuelles (la saisie est abandonnée).</summary>
    protected void TakeTheirs()
    {
        if (StaleRecord is null)
        {
            return;
        }

        Record = StaleRecord;
        Form = ToForm(StaleRecord);
        Original = Copy(Form);
        StaleRecord = null;
        Errors = [];
    }

    private async Task OnSavedAsync(OperationResult<TRecord> result, string messageKey)
    {
        if (result is { IsSuccess: true, Value: { } saved })
        {
            Errors = [];
            StaleRecord = null;
            Record = saved;
            Form = null;
            Original = null;
            Guid id = IdOf(saved);
            SelectedId = id;
            await ReloadAsync();
            _leaving = true;
            Navigation.NavigateTo(Href(id));
            _leaving = false;
            Toasts.Show(ToastKind.Success, string.Format(CultureInfo.CurrentCulture, Text(messageKey), DisplayName(saved)));
            await OnRecordLoadedAsync(saved);
            return;
        }

        if (result is { IsStale: true, Current: { } current })
        {
            StaleRecord = current;
            Errors = [];
            return;
        }

        Errors = result.Errors;
    }

    /// <summary>Remplace la fiche affichée après une action propre à l'entité (archiver un article) et relit la liste.</summary>
    protected async Task ShowUpdatedAsync(TRecord record, string status)
    {
        Record = record;
        Errors = [];
        Toasts.Show(ToastKind.Success, status);
        await ReloadAsync();
    }

    /// <summary>Nom affiché de la fiche dans les messages (« Client « Dupont » créé »).</summary>
    protected abstract string DisplayName(TRecord record);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (disposing)
        {
            Navigation.LocationChanged -= OnLocationChanged;
            if (_formContext is not null)
            {
                _formContext.OnFieldChanged -= OnFormFieldChanged;
            }

            _searchDebounce?.Cancel();
            _searchDebounce?.Dispose();
        }
    }
}
