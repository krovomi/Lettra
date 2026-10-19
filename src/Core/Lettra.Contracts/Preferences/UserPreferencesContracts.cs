using System.Text.Json.Serialization;

namespace Lettra.Contracts.Preferences;

/// <summary>Ce que l'utilisateur choisit et que Lettra compte pour le lui reproposer en premier.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UsageKindDto>))]
public enum UsageKindDto
{
    Customer = 1,
    CatalogItem = 2,
    Invoice = 3,
    Quote = 4,
}

/// <summary>Apparence, page d'accueil, menu, dossiers favoris et récents de l'utilisateur.</summary>
public sealed record WorkspacePreferencesDto
{
    /// <summary>Nom du thème (<c>System</c>, <c>Light</c>, <c>Dark</c>, <c>Slate</c>, <c>Contrast</c>) ; <c>null</c> : celui de l'appareil.</summary>
    public string? Theme { get; init; }

    /// <summary><c>Expert</c> ou <c>Comfort</c> ; <c>null</c> : celle de l'appareil.</summary>
    public string? Density { get; init; }

    /// <summary>Langue de l'écran (« fr-FR », « en-US ») ; <c>null</c> : celle du navigateur ou du système.</summary>
    public string? Language { get; init; }

    /// <summary>Écran ouvert à la connexion, lien relatif du menu (« billing/invoices ») ; <c>null</c> : l'accueil.</summary>
    public string? HomePage { get; init; }

    public IReadOnlyList<string> NavFavorites { get; init; } = [];

    public IReadOnlyList<string> CollapsedSections { get; init; } = [];

    public IReadOnlyList<Guid> FavoriteDossiers { get; init; } = [];

    public IReadOnlyList<Guid> RecentDossiers { get; init; } = [];

    public bool RankByUsage { get; init; } = true;
}

/// <summary>Valeurs pré-remplies dans les nouvelles pièces d'un dossier ; un champ <c>null</c> garde la valeur habituelle.</summary>
public sealed record EntryDefaultsDto
{
    public Guid DossierId { get; init; }

    public string? VatRateCode { get; init; }

    public string? Unit { get; init; }

    public string? PaymentTerms { get; init; }

    public string? InvoiceNotes { get; init; }

    public string? QuoteNotes { get; init; }

    public int? QuoteValidityDays { get; init; }
}

/// <summary>Un choix compté : un identifiant, jamais un nom (le nom reste dans la base du dossier).</summary>
public sealed record UsageEntryDto(UsageKindDto Kind, Guid Id, int Uses, DateTimeOffset LastUsed);

public sealed record DossierUsageDto(Guid DossierId, IReadOnlyList<UsageEntryDto> Entries);

/// <summary>Les préférences de l'utilisateur connecté. <see cref="Version"/> <c>null</c> : rien n'est encore enregistré.</summary>
public sealed record UserPreferencesResponse(
    WorkspacePreferencesDto Workspace,
    IReadOnlyList<EntryDefaultsDto> EntryDefaults,
    IReadOnlyList<DossierUsageDto> Usage,
    long? Version,
    DateTimeOffset? UpdatedAt);

/// <summary>Remplace l'espace de travail et les valeurs de saisie ; les choix comptés ne changent pas.</summary>
public sealed record SaveUserPreferencesRequest
{
    public WorkspacePreferencesDto Workspace { get; set; } = new();

    public IList<EntryDefaultsDto> EntryDefaults { get; set; } = [];

    /// <summary>Version lue ; <c>null</c> pour un premier enregistrement. Un écart renvoie <c>Concurrency.VersionMismatch</c>.</summary>
    public long? Version { get; set; }
}

/// <summary>Compte un choix dans un dossier (client choisi, article ajouté, pièce ouverte).</summary>
public sealed record RecordUsageRequest(Guid DossierId, UsageKindDto Kind, Guid Id);
