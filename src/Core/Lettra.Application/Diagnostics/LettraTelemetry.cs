using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Lettra.Application.Diagnostics;

/// <summary>
/// Conventions OpenTelemetry de Lettra (annexe G). Règle d'or : la télémétrie et les journaux
/// techniques ne portent <b>aucune donnée de dossier</b> (noms, SIREN, montants, adresses).
/// Seuls des identifiants techniques et des codes y figurent ; un test de CI le vérifie.
/// </summary>
public static class LettraTelemetry
{
    public const string SourceName = "Lettra";

    public static readonly ActivitySource ActivitySource = new(SourceName);

    public static readonly Meter Meter = new(SourceName);

    /// <summary>Nombre de cas d'usage exécutés, par nom et par issue.</summary>
    public static readonly Counter<long> UseCases =
        Meter.CreateCounter<long>("lettra.use_case.count", unit: "{use_case}", description: "Cas d'usage exécutés.");

    /// <summary>Durée des cas d'usage.</summary>
    public static readonly Histogram<double> UseCaseDuration =
        Meter.CreateHistogram<double>("lettra.use_case.duration", unit: "s", description: "Durée des cas d'usage.");

    /// <summary>Noms d'attributs autorisés. Tout nouvel attribut est revu : il ne doit jamais porter une donnée de dossier.</summary>
    public static class Attributes
    {
        /// <summary>Nom technique du cas d'usage (<c>CreateCustomerCommand</c>).</summary>
        public const string UseCase = "lettra.use_case";

        /// <summary>Issue : <c>success</c>, <c>failure</c>, <c>error</c>.</summary>
        public const string Outcome = "lettra.outcome";

        /// <summary>Premier code d'erreur (<c>Customer.SirenRequired</c>) : un code, jamais une valeur saisie.</summary>
        public const string ErrorCode = "lettra.error.code";

        /// <summary>Identifiant technique du dossier (Guid), utile au routage des alertes.</summary>
        public const string DossierId = "lettra.dossier.id";

        /// <summary>Mode de déploiement (1 à 5).</summary>
        public const string DeploymentMode = "lettra.deployment.mode";
    }

    public static class Outcomes
    {
        public const string Success = "success";
        public const string Failure = "failure";
        public const string Error = "error";
    }
}
