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

    /// <summary>
    /// Pièces de vente par type et par étape (devis créé, facture validée, avoir créé…) : des nombres, jamais un montant,
    /// un client ou un dossier. Alimenté par le pipeline des cas d'usage (<see cref="BusinessMetrics"/>).
    /// </summary>
    public static readonly Counter<long> Documents =
        Meter.CreateCounter<long>("lettra.document.count", unit: "{document}", description: "Pièces de vente créées, validées, émises, par type.");

    /// <summary>Transmissions à la plateforme agréée (dépôts et encaissements) par statut rendu.</summary>
    public static readonly Counter<long> PlatformTransmissions =
        Meter.CreateCounter<long>("lettra.einvoicing.transmission.count", unit: "{transmission}", description: "Transmissions à la plateforme agréée, par statut.");

    /// <summary>Exports FEC demandés, par issue.</summary>
    public static readonly Counter<long> FecExports =
        Meter.CreateCounter<long>("lettra.fec.export.count", unit: "{export}", description: "Exports FEC, par issue.");

    /// <summary>Questions posées à l'assistant IA, par issue et par code de refus.</summary>
    public static readonly Counter<long> AssistantCalls =
        Meter.CreateCounter<long>("lettra.assistant.call.count", unit: "{call}", description: "Appels à l'assistant IA, par issue.");

    /// <summary>Connexions au BFF (retour de Keycloak), réussies ou en échec.</summary>
    public static readonly Counter<long> SignIns =
        Meter.CreateCounter<long>("lettra.sign_in.count", unit: "{sign_in}", description: "Connexions par le navigateur, par issue.");

    /// <summary>Durée des échanges de synchronisation des postes (envoi des intentions, réception des changements).</summary>
    public static readonly Histogram<double> SyncDuration =
        Meter.CreateHistogram<double>("lettra.sync.duration", unit: "s", description: "Durée des échanges de synchronisation.");

    /// <summary>Messages de l'outbox traités par le répartiteur, par type et par résultat.</summary>
    public static readonly Counter<long> OutboxDispatches =
        Meter.CreateCounter<long>("lettra.outbox.dispatch.count", unit: "{message}", description: "Envois de l'outbox, par type et par résultat.");

    /// <summary>
    /// File de l'outbox, tous dossiers confondus, mesurée à chaque passe du répartiteur : messages en attente, mis de
    /// côté, âge du plus ancien en attente. Rien n'est publié tant qu'aucune passe n'a eu lieu dans ce processus.
    /// </summary>
    public static class Outbox
    {
        private static OutboxBacklogMeasure? _last;

        /// <summary>Messages pas encore partis ni mis de côté.</summary>
        public static readonly ObservableGauge<long> Pending = Meter.CreateObservableGauge(
            "lettra.outbox.pending", () => Observe(m => m.Pending), unit: "{message}", description: "Messages de l'outbox en attente d'envoi.");

        /// <summary>Messages mis de côté (ne repartiront qu'après correction).</summary>
        public static readonly ObservableGauge<long> Parked = Meter.CreateObservableGauge(
            "lettra.outbox.parked", () => Observe(m => m.Parked), unit: "{message}", description: "Messages de l'outbox mis de côté.");

        /// <summary>Âge du plus ancien message en attente (0 si la file est vide).</summary>
        public static readonly ObservableGauge<double> OldestAge = Meter.CreateObservableGauge(
            "lettra.outbox.oldest_age", () => Observe(m => m.OldestAgeSeconds), unit: "s", description: "Âge du plus ancien message de l'outbox en attente.");

        /// <summary>Bilan de la dernière passe du répartiteur, sur tous les dossiers actifs.</summary>
        public static void Report(long pending, long parked, double oldestAgeSeconds) =>
            Volatile.Write(ref _last, new OutboxBacklogMeasure(pending, parked, Math.Max(0, oldestAgeSeconds)));

        private static IEnumerable<Measurement<T>> Observe<T>(Func<OutboxBacklogMeasure, T> value)
            where T : struct =>
            Volatile.Read(ref _last) is { } last ? [new Measurement<T>(value(last))] : [];

        private sealed record OutboxBacklogMeasure(long Pending, long Parked, double OldestAgeSeconds);
    }

    /// <summary>
    /// Étiquettes que les métriques de Lettra peuvent porter. Toutes ont un petit nombre de valeurs connues d'avance
    /// (noms techniques, issues, codes) : jamais un identifiant de dossier ou de personne, un client ou un montant. Un
    /// test le vérifie sur un parcours complet (<c>DemoWalkthroughTests</c>).
    /// </summary>
    public static IReadOnlySet<string> MetricTags { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Attributes.UseCase,
        Attributes.Outcome,
        Attributes.ErrorCode,
        Attributes.DocumentType,
        Attributes.DocumentAction,
        Attributes.PlatformOperation,
        Attributes.PlatformStatus,
        Attributes.SyncDirection,
        Attributes.OutboxKind,
        Attributes.OutboxResult,
    };

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

        /// <summary>Type de pièce : <c>quote</c>, <c>invoice</c>, <c>down_payment</c>, <c>credit_note</c>.</summary>
        public const string DocumentType = "lettra.document.type";

        /// <summary>Étape de la pièce : <c>created</c>, <c>validated</c>, <c>accepted</c>, <c>refused</c>, <c>issued</c>…</summary>
        public const string DocumentAction = "lettra.document.action";

        /// <summary>Envoi à la plateforme agréée : <c>deposit</c> (pièce) ou <c>payment</c> (encaissement).</summary>
        public const string PlatformOperation = "lettra.einvoicing.operation";

        /// <summary>Statut rendu par la plateforme (<c>deposited</c>, <c>rejected</c>…), <c>queued</c> ou <c>refused</c>.</summary>
        public const string PlatformStatus = "lettra.einvoicing.status";

        /// <summary>Sens d'un échange de synchronisation : <c>push</c> ou <c>pull</c>.</summary>
        public const string SyncDirection = "lettra.sync.direction";

        /// <summary>Type de message de l'outbox (<c>pa.depot</c>) : un nom technique fixe.</summary>
        public const string OutboxKind = "lettra.outbox.kind";

        /// <summary>Résultat d'un envoi de l'outbox : <c>delivered</c>, <c>retry</c>, <c>parked</c>.</summary>
        public const string OutboxResult = "lettra.outbox.result";
    }

    public static class Outcomes
    {
        public const string Success = "success";
        public const string Failure = "failure";
        public const string Error = "error";
    }
}
