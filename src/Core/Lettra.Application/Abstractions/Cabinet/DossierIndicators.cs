using Lettra.Domain.Review;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Cabinet;

/// <summary>Urgence d'un indicateur (valeurs identiques à <c>IndicatorLevelDto</c>).</summary>
public enum IndicatorLevel
{
    Ok = 0,
    Info = 1,
    Warning = 2,
    Critical = 3,
}

/// <summary>
/// Un agrégat d'un dossier pour la vue cabinet : un compteur, une échéance éventuelle, une urgence. Jamais un montant ni
/// un nom de client : il quitte la base du dossier pour la base Catalog (ADR-0003, annexe F).
/// </summary>
public sealed record DossierIndicator(string Key, int Count, IndicatorLevel Level, DateOnly? DueOn = null);

/// <summary>
/// Calcule des indicateurs d'un dossier. S'exécute dans la portée de ce dossier (sa base est le dossier courant), à chaque
/// passe quotidienne et quand quelqu'un demande « Actualiser ». Chaque module enregistre la sienne :
/// <c>services.AddScoped&lt;IDossierIndicatorSource, …&gt;()</c>. Une source en échec n'empêche pas les autres.
/// </summary>
public interface IDossierIndicatorSource
{
    Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken);
}

/// <summary>Les indicateurs d'un dossier tels qu'ils ont été rangés, et quand ils ont été calculés.</summary>
public sealed record StoredDossierIndicators(DossierId Dossier, DateTimeOffset ComputedAt, IReadOnlyList<DossierIndicator> Indicators);

/// <summary>
/// Où les indicateurs sont rangés : la base Catalog (modes 2, 3 et 5), la mémoire du poste ailleurs (quelques dossiers,
/// recalculés à l'ouverture de la vue). Lire n'ouvre aucune base de dossier.
/// </summary>
public interface IDossierIndicatorStore
{
    /// <summary>Remplace tous les indicateurs du dossier (une clé absente disparaît).</summary>
    Task ReplaceAsync(DossierId dossier, IReadOnlyList<DossierIndicator> indicators, DateTimeOffset computedAt, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredDossierIndicators>> ReadAsync(IReadOnlyCollection<DossierId> dossiers, CancellationToken cancellationToken);
}

/// <summary>Recalcul : ouvre chaque dossier désigné, y fait tourner toutes les sources et range le résultat.</summary>
public interface IDossierIndicatorRefresher
{
    Task<IndicatorRefreshOutcome> RefreshAsync(IReadOnlyCollection<DossierId> dossiers, CancellationToken cancellationToken);
}

public sealed record IndicatorRefreshOutcome(int Refreshed, int Failed);

/// <summary>Les coches de la revue du dossier courant.</summary>
public interface IDossierReviewStore
{
    Task<IReadOnlyList<DossierReviewMark>> ListAsync(ReviewPeriod period, CancellationToken cancellationToken);

    /// <summary>Pose ou remplace la coche (même période, même point) ; enregistre aussitôt.</summary>
    Task SaveAsync(DossierReviewMark mark, CancellationToken cancellationToken);

    /// <summary>Retire la coche si elle existe ; enregistre aussitôt.</summary>
    Task RemoveAsync(ReviewPeriod period, string itemKey, CancellationToken cancellationToken);
}
