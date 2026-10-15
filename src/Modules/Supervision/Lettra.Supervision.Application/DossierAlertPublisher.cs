using Lettra.Application.Abstractions.Integrity;
using Lettra.Domain.Common;
using Lettra.Supervision.Domain;

namespace Lettra.Supervision.Application;

/// <summary>
/// Les règles d'alerte de dossier de la chaîne anti-fraude (annexe G §5 et §6) : la chaîne compromise est une S1 de
/// conformité, un trou de numérotation une S2. Le titre est celui de la règle, la portée des identifiants : aucune
/// donnée du dossier ne quitte sa base.
/// </summary>
public sealed class DossierAlertPublisher(AlertIngestion ingestion) : IDossierAlertPublisher
{
    public const string ChainRuleId = "integrite-chaine";
    public const string NumberingRuleId = "integrite-numerotation";

    public async Task PublishAsync(DossierAlertSignal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        Result<AlertScope> scope = AlertScope.ForDossier(null, signal.Cabinet.Value, signal.Dossier.Value);
        if (scope.IsFailure)
        {
            return;
        }

        (string ruleId, string title, Severity severity) = signal.Rule switch
        {
            DossierAlertRule.IntegrityChainCompromised => (ChainRuleId, "Chaîne anti-fraude d'un dossier compromise", Severity.S1),
            DossierAlertRule.NumberingGap => (NumberingRuleId, "Trou de numérotation dans un dossier", Severity.S2),
            _ => throw new ArgumentOutOfRangeException(nameof(signal), signal.Rule, null),
        };

        // Un signal « levé » sans alerte ouverte n'a rien à résoudre : l'ingestion le dit, ce n'est pas une erreur ici.
        _ = await ingestion.IngestAsync(
            new AlertSignal(ruleId, title, severity, AlertCategory.Compliance, scope.Value, signal.Firing, signal.At),
            cancellationToken).ConfigureAwait(false);
    }
}
