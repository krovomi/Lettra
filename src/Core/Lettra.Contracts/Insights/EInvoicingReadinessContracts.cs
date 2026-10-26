using System.Text.Json.Serialization;

namespace Lettra.Contracts.Insights;

/// <summary>Un contrôle de préparation à la facture électronique, dans l'ordre où il faut les traiter.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EInvoicingCheckDto>))]
public enum EInvoicingCheckDto
{
    /// <summary>La fiche entreprise porte le SIREN et le SIRET : sans eux, aucune pièce ne part à la plateforme.</summary>
    CompanyIdentity = 1,

    /// <summary>La fiche entreprise porte le numéro de TVA intracommunautaire, sauf en franchise en base.</summary>
    CompanyVat = 2,

    /// <summary>La fiche entreprise porte l'IBAN qui figure sur les factures.</summary>
    CompanyIban = 3,

    /// <summary>Les clients professionnels établis en France ont un SIREN : c'est l'adresse de leur facture électronique.</summary>
    FrenchCustomers = 4,

    /// <summary>Les clients professionnels établis ailleurs dans l'Union ont un numéro de TVA (e-reporting).</summary>
    EuropeanCustomers = 5,

    /// <summary>Les fournisseurs établis en France ont un SIREN : leurs factures arriveront par la plateforme.</summary>
    Suppliers = 6,

    /// <summary>Les factures récentes à des professionnels français sont parties à la plateforme et n'y sont pas rejetées.</summary>
    Transmissions = 7,
}

/// <summary>Un élément à corriger : un client, un fournisseur ou une facture, avec de quoi l'ouvrir.</summary>
/// <param name="Id">Son identifiant.</param>
/// <param name="Label">Son nom ou son numéro.</param>
public sealed record EInvoicingGapResponse(Guid Id, string Label);

/// <summary>Le résultat d'un contrôle.</summary>
/// <param name="Check">Le contrôle.</param>
/// <param name="Total">Le nombre d'éléments contrôlés (1 pour la fiche entreprise, 0 quand rien n'est concerné).</param>
/// <param name="Passing">Ceux qui sont prêts.</param>
/// <param name="Percent">La part prête, de 0 à 100 ; 100 quand rien n'est concerné.</param>
/// <param name="Gaps">Les premiers éléments à corriger, au plus <see cref="EInvoicingReadinessResponse.MaxGaps"/>.</param>
public sealed record EInvoicingCheckResponse(EInvoicingCheckDto Check, int Total, int Passing, int Percent, IReadOnlyList<EInvoicingGapResponse> Gaps)
{
    /// <summary>Vrai quand tout est prêt.</summary>
    public bool Ok => Passing == Total;
}

/// <summary>
/// Le score de préparation du dossier à la facture électronique : la moyenne des contrôles, de 0 à 100, et le détail de
/// chacun avec ce qu'il reste à corriger. Calculé à chaque lecture, sans rien écrire.
/// </summary>
/// <param name="Today">Date du jour (Paris).</param>
/// <param name="Since">Début de la période des factures contrôlées.</param>
/// <param name="Score">Le score, de 0 à 100.</param>
/// <param name="Checks">Les contrôles, dans l'ordre où il faut les traiter.</param>
public sealed record EInvoicingReadinessResponse(DateOnly Today, DateOnly Since, int Score, IReadOnlyList<EInvoicingCheckResponse> Checks)
{
    /// <summary>Nombre d'éléments à corriger montrés par contrôle.</summary>
    public const int MaxGaps = 5;

    /// <summary>La période des factures contrôlées : les 90 derniers jours.</summary>
    public const int RecentDays = 90;

    /// <summary>Le nombre de contrôles qui ne sont pas encore satisfaits.</summary>
    public int Failing => Checks.Count(c => !c.Ok);
}
