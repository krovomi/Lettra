using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Cabinet;

/// <summary>
/// Les clés des indicateurs de la vue cabinet. Un module en ajoute une sans migration : la base Catalog range les
/// indicateurs par (dossier, clé). L'écran connaît le libellé des clés ci-dessous ; une clé inconnue s'affiche telle quelle.
/// </summary>
public static class DossierIndicatorKeys
{
    /// <summary>Factures et acomptes émis dont l'échéance est passée et qui ne sont pas encaissés.</summary>
    public const string OverdueInvoices = "factures.retard";

    /// <summary>Pièces dont la dernière transmission a été rejetée par la plateforme agréée (à corriger et retransmettre).</summary>
    public const string PlatformRejections = "pa.rejets";

    /// <summary>Alertes ouvertes du dossier (chaîne anti-fraude, trous de numérotation…).</summary>
    public const string OpenAlerts = "alertes.ouvertes";

    /// <summary>Points de la revue du mois écoulé pas encore cochés.</summary>
    public const string ReviewPending = "revue.a-faire";

    /// <summary>Déclarations de TVA à déposer (chantier « Brouillon de déclaration de TVA ») ; échéance = la prochaine.</summary>
    public const string VatToDeclare = "tva.a-declarer";

    /// <summary>Lignes bancaires non rapprochées (chantier « Encaissements et rapprochement ») ; échéance = la plus ancienne.</summary>
    public const string BankUnreconciled = "banque.non-rapprochee";

    /// <summary>Ordre des colonnes du tableau (les clés inconnues suivent, par ordre alphabétique).</summary>
    public static IReadOnlyList<string> Known { get; } =
        [OverdueInvoices, PlatformRejections, VatToDeclare, BankUnreconciled, OpenAlerts, ReviewPending];
}

/// <summary>Urgence d'un indicateur : décide de la couleur et de l'ordre des dossiers.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IndicatorLevelDto>))]
public enum IndicatorLevelDto
{
    Ok = 0,
    Info = 1,
    Warning = 2,
    Critical = 3,
}

/// <summary>Un agrégat d'un dossier : un compteur, une échéance éventuelle, une urgence. Jamais un montant ni un nom de client.</summary>
public sealed record DossierIndicatorDto(string Key, int Count, IndicatorLevelDto Level, DateOnly? DueOn);

/// <summary>
/// Une ligne de la vue cabinet. <paramref name="ComputedAt"/> est <c>null</c> tant que le dossier n'a jamais été
/// calculé ; <paramref name="Level"/> est la plus forte urgence de ses indicateurs.
/// </summary>
public sealed record DossierOverviewRow(
    Guid DossierId,
    string Name,
    DateTimeOffset? ComputedAt,
    IndicatorLevelDto Level,
    IReadOnlyList<DossierIndicatorDto> Indicators);

/// <summary>Les dossiers que l'utilisateur peut ouvrir, avec leurs indicateurs, les plus urgents d'abord.</summary>
public sealed record CabinetOverviewResponse(IReadOnlyList<DossierOverviewRow> Dossiers);

/// <summary>Résultat d'un recalcul : dossiers recalculés, et ceux dont le calcul a échoué (gardent leurs anciennes valeurs).</summary>
public sealed record CabinetOverviewRefreshResponse(int Refreshed, int Failed);

/// <summary>Un point de la revue de dossier, coché ou non, avec qui l'a coché et quand.</summary>
public sealed record DossierReviewItemDto(
    string Key,
    bool Checked,
    Guid? CheckedBy,
    string? CheckedByName,
    bool CheckedByMe,
    DateTimeOffset? CheckedAt,
    string? Note);

/// <summary>
/// La revue d'un dossier pour une période (<c>2026-09</c> pour un mois, <c>2026-T3</c> pour un trimestre). Les points
/// sont toujours rendus dans l'ordre de la liste de contrôle ; <paramref name="Done"/> compte les points cochés.
/// </summary>
public sealed record DossierReviewResponse(string Period, int Done, IReadOnlyList<DossierReviewItemDto> Items);

/// <summary>Cocher ou décocher un point de la revue. Mutable pour <c>EditForm</c>.</summary>
public sealed record SetDossierReviewItemRequest
{
    public const int MaxNoteLength = 500;

    public bool Checked { get; set; }

    [StringLength(MaxNoteLength, ErrorMessage = ErrorCodes.Review.NoteTooLong)]
    public string? Note { get; set; }
}
