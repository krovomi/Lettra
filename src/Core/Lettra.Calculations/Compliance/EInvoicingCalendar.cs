namespace Lettra.Calculations.Compliance;

/// <summary>
/// Catégorie de l'entreprise au sens de l'article 51 de la loi de modernisation de l'économie (décret 2008-1354) : elle
/// fixe la date à laquelle l'entreprise doit émettre ses factures électroniques, faire son e-reporting et porter les
/// nouvelles mentions. Les valeurs sont stockées : ajout seulement.
/// </summary>
public enum CompanySize
{
    /// <summary>Non renseignée : Lettra applique la première échéance, la plus prudente.</summary>
    Unknown = 0,

    /// <summary>Grande entreprise (5 000 salariés ou plus, ou plus de 1,5 milliard d'euros de chiffre d'affaires).</summary>
    Large = 1,

    /// <summary>Entreprise de taille intermédiaire (ETI), y compris un membre d'un groupe TVA.</summary>
    Intermediate = 2,

    /// <summary>Petite ou moyenne entreprise (PME).</summary>
    SmallOrMedium = 3,

    /// <summary>Micro-entreprise (moins de 10 salariés et 2 millions d'euros de chiffre d'affaires ou de bilan).</summary>
    Micro = 4,
}

/// <summary>
/// Le calendrier de la réforme de la facturation électronique (art. 289 bis et 290 du CGI, décret 2024-266) : toutes
/// les entreprises reçoivent des factures électroniques au 1er septembre 2026 ; les grandes entreprises et les ETI
/// émettent, font leur e-reporting et portent les nouvelles mentions à cette date, les PME et les micro-entreprises
/// au 1er septembre 2027.
/// </summary>
public static class EInvoicingCalendar
{
    /// <summary>Première échéance : réception pour toutes, émission pour les grandes entreprises et les ETI.</summary>
    public static readonly DateOnly FirstWave = new(2026, 9, 1);

    /// <summary>Seconde échéance : émission et e-reporting des PME et des micro-entreprises.</summary>
    public static readonly DateOnly SecondWave = new(2027, 9, 1);

    /// <summary>Date à laquelle toute entreprise doit pouvoir recevoir ses factures fournisseurs électroniques.</summary>
    public static DateOnly ReceivingFrom => FirstWave;

    /// <summary>Date d'obligation d'émission, d'e-reporting et des nouvelles mentions pour cette catégorie.</summary>
    public static DateOnly IssuingFrom(CompanySize size) =>
        size is CompanySize.SmallOrMedium or CompanySize.Micro ? SecondWave : FirstWave;
}
