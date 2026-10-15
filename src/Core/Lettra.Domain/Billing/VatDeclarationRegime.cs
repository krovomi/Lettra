namespace Lettra.Domain.Billing;

/// <summary>
/// Régime de déclaration de la TVA de l'entreprise : il fixe le rythme des déclarations (CA3, CA12) et celui de
/// l'e-reporting. Sans objet en franchise en base.
/// </summary>
public enum VatDeclarationRegime
{
    /// <summary>Réel normal : CA3 mensuelle (ou trimestrielle sous 4 000 € de TVA par an).</summary>
    RealNormal = 1,

    /// <summary>Réel simplifié : CA12 annuelle et deux acomptes.</summary>
    RealSimplified = 2,

    /// <summary>Mini-réel : TVA au réel normal, bénéfices au réel simplifié.</summary>
    MiniReal = 3,
}
