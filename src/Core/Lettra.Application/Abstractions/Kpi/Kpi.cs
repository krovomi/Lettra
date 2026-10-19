using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Kpi;

/// <summary>
/// Une valeur d'indicateur de pilotage, lue par le Prometheus de la cellule et affichée dans Grafana. Jamais un montant,
/// un nom de client, un SIREN ou une personne : des ratios, des délais en jours, des nombres de pièces ou d'opérations.
/// <paramref name="Dossier"/> est vide pour un indicateur de la cellule (comptes, alertes, collecte).
/// <paramref name="Label"/> est la seule étiquette propre à l'indicateur (<c>month</c>, <c>state</c>, <c>key</c>,
/// <c>severity</c>) : une valeur connue d'avance, jamais une saisie.
/// </summary>
public sealed record KpiSample(string Metric, double Value, DossierId? Dossier = null, KpiLabel? Label = null);

/// <summary>Étiquette propre à un indicateur : nom et valeur, tous deux techniques.</summary>
public sealed record KpiLabel(string Name, string Value);

/// <summary>
/// Indicateurs d'un dossier. S'exécute dans la portée de ce dossier (sa base est le dossier courant), comme les
/// sources de la vue cabinet. Chaque module enregistre la sienne : <c>services.AddScoped&lt;IDossierKpiSource, …&gt;()</c>.
/// Une source en échec n'empêche pas les autres. Le collecteur estampille le dossier : la source le laisse vide.
/// </summary>
public interface IDossierKpiSource
{
    Task<IReadOnlyList<KpiSample>> CollectAsync(DateOnly today, CancellationToken cancellationToken);
}

/// <summary>
/// Indicateurs lus hors de la base des dossiers (catalogue, supervision) : sur toute la cellule, ou pour les dossiers
/// donnés sans ouvrir leur base. Enregistrées en singleton ; ouvrent leur propre portée si besoin.
/// </summary>
public interface ICellKpiSource
{
    Task<IReadOnlyList<KpiSample>> CollectAsync(IReadOnlyCollection<DossierId> dossiers, DateOnly today, CancellationToken cancellationToken);
}

/// <summary>Nom d'un dossier et de son cabinet, pour que Grafana les affiche (ils sont déjà dans la base Catalog).</summary>
public sealed record KpiDossier(DossierId Id, string Name, string Cabinet);

/// <summary>Toutes les valeurs d'une collecte, et quand elle a eu lieu.</summary>
public sealed record KpiSnapshot(DateTimeOffset CollectedAt, IReadOnlyList<KpiDossier> Dossiers, IReadOnlyList<KpiSample> Samples);

/// <summary>
/// La dernière collecte, refaite quand elle est plus ancienne que l'intervalle réglé : Prometheus peut lire souvent sans
/// rouvrir chaque base de dossier.
/// </summary>
public interface IKpiSnapshotProvider
{
    Task<KpiSnapshot> GetAsync(CancellationToken cancellationToken);
}
