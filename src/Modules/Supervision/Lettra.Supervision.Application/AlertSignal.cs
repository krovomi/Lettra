using Lettra.Supervision.Domain;

namespace Lettra.Supervision.Application;

/// <summary>Un signal normalisé, quelle que soit sa source (Alertmanager, règle métier, sonde).</summary>
/// <param name="RuleId">Identifiant de la règle versionnée qui l'a produit.</param>
/// <param name="Title">Titre de la règle : jamais une donnée de dossier.</param>
/// <param name="Severity">Gravité donnée par la règle.</param>
/// <param name="Category">Catégorie de la règle.</param>
/// <param name="Scope">Portée, en identifiants techniques.</param>
/// <param name="Firing">Vrai si la condition est vraie, faux si elle vient de disparaître.</param>
/// <param name="At">Instant du signal.</param>
public sealed record AlertSignal(
    string RuleId,
    string Title,
    Severity Severity,
    AlertCategory Category,
    AlertScope Scope,
    bool Firing,
    DateTimeOffset At);

/// <summary>Où lire une alerte dans Lettra (centre d'alertes).</summary>
public sealed class SupervisionLinks(Uri baseAddress)
{
    public Uri For(AlertId id) => new(baseAddress, $"alertes/{id}");
}
