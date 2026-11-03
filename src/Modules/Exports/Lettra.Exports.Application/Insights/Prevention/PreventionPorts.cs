using Lettra.Domain.Prevention;

namespace Lettra.Exports.Application.Insights.Prevention;

/// <summary>Les seuils du radar dans la base du dossier (une seule ligne, absente tant que rien n'est réglé).</summary>
public interface IPreventionSettingsStore
{
    /// <summary>Les seuils enregistrés (suivis : une modification est enregistrée par l'unité de travail), ou <c>null</c>.</summary>
    Task<PreventionSettings?> FindAsync(CancellationToken cancellationToken);

    Task AddAsync(PreventionSettings settings, CancellationToken cancellationToken);
}

/// <summary>
/// Les factures fournisseurs reçues par la plateforme agréée qui restent à payer : ni avoirs, ni refusées, ni marquées
/// payées. Lecture seule, sans le fichier de la facture.
/// </summary>
public interface IPreventionPayablesReader
{
    Task<IReadOnlyList<PreventionPayable>> ReadAsync(CancellationToken cancellationToken);
}
