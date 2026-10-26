namespace Lettra.Contracts.Insights;

/// <summary>Les chiffres que « Pourquoi ce chiffre ? » sait expliquer (valeurs stables, ajout seulement).</summary>
public enum ExplainedFigureDto
{
    /// <summary>Le chiffre d'affaires hors taxes d'un mois, tel que l'accueil le montre.</summary>
    MonthRevenue = 0,

    /// <summary>Le montant en retard de l'accueil : ce qui reste dû sur les factures échues.</summary>
    Overdue = 1,
}

/// <summary>Le rôle d'une pièce dans le calcul d'un chiffre (valeurs stables, ajout seulement).</summary>
public enum ExplanationLineKindDto
{
    /// <summary>Une facture ou un acompte, qui s'ajoute.</summary>
    Invoice = 0,

    /// <summary>Un avoir, qui se retranche.</summary>
    CreditNote = 1,
}

/// <summary>
/// La chaîne d'un calcul jusqu'aux pièces : le chiffre, la règle qui le produit et chaque pièce avec ce qu'elle apporte.
/// Tout vient du code déterministe ; l'écran le met en mots simples.
/// </summary>
/// <param name="Figure">Le chiffre expliqué.</param>
/// <param name="Value">Sa valeur, la même que celle de l'écran d'où l'on vient.</param>
/// <param name="Today">Le jour légal (heure de Paris) du calcul.</param>
/// <param name="Month">Le premier jour du mois, pour <see cref="ExplainedFigureDto.MonthRevenue"/>.</param>
/// <param name="Lines">Les pièces qui le composent, dans l'ordre où elles comptent.</param>
public sealed record FigureExplanationResponse(
    ExplainedFigureDto Figure,
    decimal Value,
    DateOnly Today,
    DateOnly? Month,
    IReadOnlyList<ExplanationLineResponse> Lines);

/// <summary>
/// Une pièce dans un calcul, avec ses termes : pour un chiffre d'affaires, son montant hors taxes (négatif pour un
/// avoir) ; pour un retard, son total TTC, ses avoirs, ce qui est encaissé et le reste dû qui en sort.
/// </summary>
public sealed record ExplanationLineResponse(
    Guid DocumentId,
    string Number,
    string CustomerName,
    ExplanationLineKindDto Kind,
    DateOnly? Date,
    decimal Amount,
    decimal? TotalInclTax = null,
    decimal? CreditedInclTax = null,
    decimal? CollectedInclTax = null,
    int? DaysLate = null);
