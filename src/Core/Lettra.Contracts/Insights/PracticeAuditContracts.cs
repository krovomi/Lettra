namespace Lettra.Contracts.Insights;

/// <summary>Ce que le contrôleur d'entraînement conclut d'un test (valeurs stables, ajout seulement).</summary>
public enum PracticeAuditSeverityDto
{
    /// <summary>Rien à relever.</summary>
    Passed = 0,

    /// <summary>Le vérificateur poserait la question : mieux vaut avoir la réponse prête.</summary>
    Watch = 1,

    /// <summary>Le vérificateur le relèverait : à corriger ou à justifier avant tout contrôle.</summary>
    Alert = 2,
}

/// <summary>
/// Un test de cohérence passé sur le FEC, comme un vérificateur le ferait avec ses outils d'analyse des fichiers.
/// <see cref="Code"/> est stable (clés de libellé <c>Audit.Check.{Code}</c>, <c>.Why</c>, <c>.Fix</c>).
/// </summary>
/// <param name="Code">Le test.</param>
/// <param name="Severity">Sa conclusion.</param>
/// <param name="Count">Nombre d'écritures, de lignes ou de comptes relevés.</param>
/// <param name="Value">Une valeur mesurée quand le test en a une (taux, écart, montant), sinon rien.</param>
/// <param name="Examples">Jusqu'à <see cref="PracticeAuditResponse.MaxExamples"/> exemples : numéros d'écriture, comptes, dates.</param>
public sealed record PracticeAuditCheckResponse(
    string Code,
    PracticeAuditSeverityDto Severity,
    int Count,
    decimal? Value,
    IReadOnlyList<string> Examples);

/// <summary>La loi de Benford : part observée et part attendue de chaque premier chiffre (1 à 9) des montants.</summary>
/// <param name="Digit">Le premier chiffre.</param>
/// <param name="Observed">Part observée, en pourcentage.</param>
/// <param name="Expected">Part attendue, en pourcentage (log10(1 + 1/chiffre)).</param>
public sealed record BenfordDigitResponse(int Digit, decimal Observed, decimal Expected);

/// <summary>
/// Le contrôle fiscal d'entraînement d'un FEC : les tests qu'un vérificateur passerait, du plus grave au plus anodin.
/// Rien n'est envoyé ni écrit ; c'est un entraînement, pas un avis fiscal.
/// </summary>
/// <param name="FileName">Le fichier contrôlé.</param>
/// <param name="From">Début de l'exercice, s'il est connu.</param>
/// <param name="To">Fin de l'exercice, s'il est connue.</param>
/// <param name="Entries">Écritures lues.</param>
/// <param name="Lines">Lignes lues.</param>
/// <param name="Readable">Faux si le fichier n'est pas un FEC lisible (en-tête, colonnes) : aucun test n'a pu passer.</param>
/// <param name="Checks">Les tests, alertes d'abord.</param>
/// <param name="Benford">La répartition des premiers chiffres, vide s'il y a trop peu de montants.</param>
public sealed record PracticeAuditResponse(
    string FileName,
    DateOnly? From,
    DateOnly? To,
    int Entries,
    int Lines,
    bool Readable,
    IReadOnlyList<PracticeAuditCheckResponse> Checks,
    IReadOnlyList<BenfordDigitResponse> Benford)
{
    public const int MaxExamples = 10;
}
