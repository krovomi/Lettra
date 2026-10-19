using System.Text.RegularExpressions;
using Lettra.Domain.Common;

namespace Lettra.Domain.Preferences;

/// <summary>Ce que l'utilisateur choisit souvent, compté dans un dossier (valeurs identiques à <c>UsageKindDto</c>).</summary>
public enum UsageKind
{
    Customer = 1,
    CatalogItem = 2,
    Invoice = 3,
    Quote = 4,
}

/// <summary>
/// Réglages de l'espace de travail : apparence, page d'accueil, menu, dossiers favoris et récents. Des noms et des liens
/// relatifs de l'interface, jamais une donnée de dossier.
/// </summary>
public sealed record WorkspacePreferences
{
    public static readonly WorkspacePreferences Empty = new();

    public string? Theme { get; init; }

    public string? Density { get; init; }

    /// <summary>Langue de l'écran (« fr-FR », « en-US ») ; <c>null</c> : celle du navigateur ou du système.</summary>
    public string? Language { get; init; }

    /// <summary>Écran ouvert à la connexion (lien relatif du menu, « billing/invoices ») ; <c>null</c> : l'accueil.</summary>
    public string? HomePage { get; init; }

    public IReadOnlyList<string> NavFavorites { get; init; } = [];

    public IReadOnlyList<string> CollapsedSections { get; init; } = [];

    public IReadOnlyList<Guid> FavoriteDossiers { get; init; } = [];

    public IReadOnlyList<Guid> RecentDossiers { get; init; } = [];

    /// <summary>Vrai : les clients et articles les plus utilisés remontent en tête des listes de choix.</summary>
    public bool RankByUsage { get; init; } = true;
}

/// <summary>Valeurs pré-remplies dans les nouvelles pièces d'un dossier ; chaque champ vide laisse la valeur habituelle.</summary>
public sealed record EntryDefaults
{
    public Guid DossierId { get; init; }

    /// <summary>Code du taux de TVA des nouvelles lignes (« FR-20 »).</summary>
    public string? VatRateCode { get; init; }

    /// <summary>Unité des nouvelles lignes (code UN/ECE, « C62 », « HUR »).</summary>
    public string? Unit { get; init; }

    public string? PaymentTerms { get; init; }

    public string? InvoiceNotes { get; init; }

    public string? QuoteNotes { get; init; }

    /// <summary>Validité des nouveaux devis, en jours.</summary>
    public int? QuoteValidityDays { get; init; }
}

/// <summary>Un choix compté : combien de fois et quand pour la dernière fois. Un identifiant seulement, aucun nom.</summary>
public sealed record UsageEntry(UsageKind Kind, Guid Id, int Uses, DateTimeOffset LastUsed);

/// <summary>Ce que l'utilisateur a choisi dans un dossier.</summary>
public sealed record DossierUsage(Guid DossierId, IReadOnlyList<UsageEntry> Entries)
{
    public DateTimeOffset LastUsed => Entries.Count == 0 ? DateTimeOffset.MinValue : Entries.Max(e => e.LastUsed);
}

/// <summary>
/// Préférences d'un utilisateur, rangées dans son compte (base Catalog) pour le suivre sur tous ses appareils : son
/// espace de travail, ses valeurs de saisie par dossier et ce qu'il choisit le plus souvent. Tout est borné (nombre
/// d'éléments, longueurs) et ne contient que des identifiants et des réglages : aucun nom de client, aucun montant.
/// </summary>
public sealed partial class UserPreferences : IVersioned
{
    public const int MaxNavEntries = 40;
    public const int MaxDossiers = 50;
    public const int MaxRecentDossiers = 10;
    public const int MaxChoicesPerKind = 30;
    public const int MaxDocumentsPerKind = 8;
    public const int MaxNameLength = 20;
    public const int MaxLinkLength = 100;
    public const int MaxCodeLength = 20;
    public const int MaxPaymentTermsLength = 500;
    public const int MaxNotesLength = 2000;
    public const int MaxQuoteValidityDays = 365;

    private UserPreferences(Guid userId, WorkspacePreferences workspace, IReadOnlyList<EntryDefaults> entryDefaults, IReadOnlyList<DossierUsage> usage, long version, DateTimeOffset updatedAt)
    {
        UserId = userId;
        Workspace = workspace;
        EntryDefaults = entryDefaults;
        Usage = usage;
        Version = version;
        UpdatedAt = updatedAt;
    }

    public Guid UserId { get; }

    public WorkspacePreferences Workspace { get; private set; }

    public IReadOnlyList<EntryDefaults> EntryDefaults { get; private set; }

    public IReadOnlyList<DossierUsage> Usage { get; private set; }

    /// <summary>0 tant que rien n'est enregistré ; la persistance l'avance à chaque écriture.</summary>
    public long Version { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Un compte qui n'a encore rien réglé.</summary>
    public static UserPreferences Empty(Guid userId) => new(userId, WorkspacePreferences.Empty, [], [], 0, DateTimeOffset.MinValue);

    /// <summary>Relit des préférences enregistrées ; ce qui ne respecte plus les bornes est écarté sans erreur.</summary>
    public static UserPreferences Restore(
        Guid userId,
        WorkspacePreferences? workspace,
        IEnumerable<EntryDefaults>? entryDefaults,
        IEnumerable<DossierUsage>? usage,
        long version,
        DateTimeOffset updatedAt)
    {
        UserPreferences restored = new(userId, WorkspacePreferences.Empty, [], [], version, updatedAt);
        if (workspace is not null && ValidateWorkspace(workspace).Count == 0)
        {
            restored.Workspace = Normalize(workspace);
        }

        restored.EntryDefaults = [.. (entryDefaults ?? []).Where(d => d is not null && ValidateDefaults(d, 0).Count == 0).Select(Normalize).Where(HasValues).DistinctBy(d => d.DossierId).Take(MaxDossiers)];
        restored.Usage = [.. (usage ?? []).Where(u => u is not null && u.DossierId != Guid.Empty).DistinctBy(u => u.DossierId).Select(Trim).Where(u => u.Entries.Count > 0).OrderByDescending(u => u.LastUsed).Take(MaxDossiers)];
        return restored;
    }

    /// <summary>
    /// Remplace l'espace de travail et les valeurs de saisie (ce que montre la page « Mes préférences »). Les choix
    /// comptés ne changent pas : ils ont leur propre écriture (<see cref="RecordUse"/>).
    /// </summary>
    public Result Change(WorkspacePreferences workspace, IReadOnlyList<EntryDefaults> entryDefaults, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(entryDefaults);
        List<Error> errors = [.. ValidateWorkspace(workspace)];
        if (entryDefaults.Count > MaxDossiers)
        {
            errors.Add(UserPreferencesErrors.TooMany(nameof(EntryDefaults)));
        }

        if (entryDefaults.Select(d => d.DossierId).Distinct().Count() != entryDefaults.Count)
        {
            errors.Add(UserPreferencesErrors.Invalid(nameof(EntryDefaults)));
        }

        for (int i = 0; i < entryDefaults.Count; i++)
        {
            errors.AddRange(ValidateDefaults(entryDefaults[i], i));
        }

        if (errors.Count > 0)
        {
            return Result.Failure(errors);
        }

        Workspace = Normalize(workspace);
        EntryDefaults = [.. entryDefaults.Select(Normalize).Where(HasValues)];
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Les valeurs de saisie d'un dossier, ou des valeurs vides.</summary>
    public EntryDefaults DefaultsFor(Guid dossierId) =>
        EntryDefaults.FirstOrDefault(d => d.DossierId == dossierId) ?? new EntryDefaults { DossierId = dossierId };

    /// <summary>
    /// Compte un choix dans un dossier. Les clients et articles gardent les <see cref="MaxChoicesPerKind"/> plus utilisés
    /// (à égalité, les plus récents) ; les pièces ouvertes, les <see cref="MaxDocumentsPerKind"/> dernières.
    /// </summary>
    public Result RecordUse(Guid dossierId, UsageKind kind, Guid id, DateTimeOffset now)
    {
        if (dossierId == Guid.Empty || id == Guid.Empty || !Enum.IsDefined(kind))
        {
            return UserPreferencesErrors.Invalid(nameof(Usage));
        }

        DossierUsage current = Usage.FirstOrDefault(u => u.DossierId == dossierId) ?? new DossierUsage(dossierId, []);
        UsageEntry? previous = current.Entries.FirstOrDefault(e => e.Kind == kind && e.Id == id);
        UsageEntry entry = new(kind, id, Math.Min((previous?.Uses ?? 0) + 1, int.MaxValue - 1), now);
        DossierUsage updated = Trim(current with { Entries = [.. current.Entries.Where(e => e != previous), entry] });
        Usage = [updated, .. Usage.Where(u => u.DossierId != dossierId).Take(MaxDossiers - 1)];
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Les choix comptés d'un genre dans un dossier, du plus utile au moins utile.</summary>
    public IReadOnlyList<UsageEntry> Ranked(Guid dossierId, UsageKind kind) =>
        [.. (Usage.FirstOrDefault(u => u.DossierId == dossierId)?.Entries ?? []).Where(e => e.Kind == kind).OrderBy(e => e, RankComparer.Instance)];

    /// <summary>Oublie les choix comptés : d'un dossier, ou de tous quand <paramref name="dossierId"/> est <c>null</c>.</summary>
    public void ForgetUsage(Guid? dossierId, DateTimeOffset now)
    {
        Usage = dossierId is Guid id ? [.. Usage.Where(u => u.DossierId != id)] : [];
        UpdatedAt = now;
    }

    /// <summary>Tout revient aux valeurs de départ (droit à l'effacement de ses propres réglages).</summary>
    public void Reset(DateTimeOffset now)
    {
        Workspace = WorkspacePreferences.Empty;
        EntryDefaults = [];
        Usage = [];
        UpdatedAt = now;
    }

    private static List<Error> ValidateWorkspace(WorkspacePreferences workspace)
    {
        List<Error> errors = [];
        CheckName(workspace.Theme, nameof(workspace.Theme), errors);
        CheckName(workspace.Density, nameof(workspace.Density), errors);
        if (workspace.Language is { } language && !LanguagePattern().IsMatch(language))
        {
            errors.Add(UserPreferencesErrors.Invalid(nameof(workspace.Language)));
        }

        if (workspace.HomePage is { } home && !IsLink(home))
        {
            errors.Add(UserPreferencesErrors.Invalid(nameof(workspace.HomePage)));
        }

        CheckList(workspace.NavFavorites, MaxNavEntries, IsLink, nameof(workspace.NavFavorites), errors);
        CheckList(workspace.CollapsedSections, MaxNavEntries, IsSectionKey, nameof(workspace.CollapsedSections), errors);
        CheckList(workspace.FavoriteDossiers, MaxDossiers, id => id != Guid.Empty, nameof(workspace.FavoriteDossiers), errors);
        CheckList(workspace.RecentDossiers, MaxRecentDossiers, id => id != Guid.Empty, nameof(workspace.RecentDossiers), errors);
        return errors;
    }

    private static List<Error> ValidateDefaults(EntryDefaults defaults, int index)
    {
        string prefix = $"{nameof(EntryDefaults)}[{index}].";
        List<Error> errors = [];
        if (defaults.DossierId == Guid.Empty)
        {
            errors.Add(UserPreferencesErrors.Invalid(prefix + nameof(defaults.DossierId)));
        }

        CheckText(defaults.VatRateCode, MaxCodeLength, prefix + nameof(defaults.VatRateCode), errors, singleLine: true);
        CheckText(defaults.Unit, MaxCodeLength, prefix + nameof(defaults.Unit), errors, singleLine: true);
        CheckText(defaults.PaymentTerms, MaxPaymentTermsLength, prefix + nameof(defaults.PaymentTerms), errors);
        CheckText(defaults.InvoiceNotes, MaxNotesLength, prefix + nameof(defaults.InvoiceNotes), errors);
        CheckText(defaults.QuoteNotes, MaxNotesLength, prefix + nameof(defaults.QuoteNotes), errors);
        if (defaults.QuoteValidityDays is < 1 or > MaxQuoteValidityDays)
        {
            errors.Add(UserPreferencesErrors.Invalid(prefix + nameof(defaults.QuoteValidityDays)));
        }

        return errors;
    }

    private static void CheckName(string? value, string target, List<Error> errors)
    {
        if (value is not null && !NamePattern().IsMatch(value))
        {
            errors.Add(UserPreferencesErrors.Invalid(target));
        }
    }

    private static void CheckText(string? value, int maxLength, string target, List<Error> errors, bool singleLine = false)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length > maxLength || (singleLine && value.Any(char.IsControl)))
        {
            errors.Add(UserPreferencesErrors.Invalid(target));
        }
    }

    private static void CheckList<T>(IReadOnlyList<T>? values, int max, Func<T, bool> isValid, string target, List<Error> errors)
    {
        if (values is null)
        {
            errors.Add(UserPreferencesErrors.Invalid(target));
        }
        else if (values.Count > max)
        {
            errors.Add(UserPreferencesErrors.TooMany(target));
        }
        else if (!values.All(isValid))
        {
            errors.Add(UserPreferencesErrors.Invalid(target));
        }
    }

    /// <summary>Un lien relatif de l'interface : minuscules, chiffres, « - » et « / », sans « // » ni schéma.</summary>
    private static bool IsLink(string value) =>
        value.Length <= MaxLinkLength && LinkPattern().IsMatch(value) && !value.Contains("//", StringComparison.Ordinal);

    private static bool IsSectionKey(string value) => value.Length <= MaxLinkLength && SectionPattern().IsMatch(value);

    private static WorkspacePreferences Normalize(WorkspacePreferences workspace) => workspace with
    {
        HomePage = string.IsNullOrEmpty(workspace.HomePage) ? null : workspace.HomePage,
        NavFavorites = [.. workspace.NavFavorites.Distinct(StringComparer.Ordinal)],
        CollapsedSections = [.. workspace.CollapsedSections.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
        FavoriteDossiers = [.. workspace.FavoriteDossiers.Distinct()],
        RecentDossiers = [.. workspace.RecentDossiers.Distinct()],
    };

    private static EntryDefaults Normalize(EntryDefaults defaults) => defaults with
    {
        VatRateCode = Clean(defaults.VatRateCode),
        Unit = Clean(defaults.Unit),
        PaymentTerms = Clean(defaults.PaymentTerms),
        InvoiceNotes = Clean(defaults.InvoiceNotes),
        QuoteNotes = Clean(defaults.QuoteNotes),
    };

    private static bool HasValues(EntryDefaults d) =>
        d.VatRateCode is not null || d.Unit is not null || d.PaymentTerms is not null || d.InvoiceNotes is not null || d.QuoteNotes is not null || d.QuoteValidityDays is not null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DossierUsage Trim(DossierUsage usage)
    {
        IEnumerable<UsageEntry> valid = (usage.Entries ?? []).Where(e => e is not null && e.Id != Guid.Empty && e.Uses > 0 && Enum.IsDefined(e.Kind)).DistinctBy(e => (e.Kind, e.Id));
        List<UsageEntry> kept = [];
        foreach (IGrouping<UsageKind, UsageEntry> group in valid.GroupBy(e => e.Kind))
        {
            kept.AddRange(IsDocument(group.Key)
                ? group.OrderByDescending(e => e.LastUsed).Take(MaxDocumentsPerKind)
                : group.OrderBy(e => e, RankComparer.Instance).Take(MaxChoicesPerKind));
        }

        return usage with { Entries = kept };
    }

    private static bool IsDocument(UsageKind kind) => kind is UsageKind.Invoice or UsageKind.Quote;

    [GeneratedRegex("^[A-Za-z]{1,20}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[a-z]{2}(-[A-Z]{2})?$")]
    private static partial Regex LanguagePattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9/-]*$")]
    private static partial Regex LinkPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9.]*$")]
    private static partial Regex SectionPattern();

    /// <summary>Plus utilisé d'abord ; à égalité, le plus récent ; puis l'identifiant, pour un ordre stable.</summary>
    private sealed class RankComparer : IComparer<UsageEntry>
    {
        public static readonly RankComparer Instance = new();

        public int Compare(UsageEntry? x, UsageEntry? y)
        {
            if (x is null || y is null)
            {
                return Comparer<UsageEntry?>.Default.Compare(x, y);
            }

            int byUses = y.Uses.CompareTo(x.Uses);
            if (byUses != 0)
            {
                return byUses;
            }

            int byDate = y.LastUsed.CompareTo(x.LastUsed);
            return byDate != 0 ? byDate : x.Id.CompareTo(y.Id);
        }
    }
}

/// <summary>Codes d'erreur des préférences de l'utilisateur.</summary>
public static class UserPreferencesErrors
{
    public const string InvalidCode = "Preferences.Invalid";
    public const string TooManyCode = "Preferences.TooMany";
    public const string UnavailableCode = "Preferences.Unavailable";

    public static Error Unavailable => Error.Validation(UnavailableCode);

    public static Error Invalid(string target) => Error.Validation(InvalidCode, target);

    public static Error TooMany(string target) => Error.Validation(TooManyCode, target);
}
