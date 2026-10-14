namespace Lettra.Poc.Grille.Calculs;

public enum StatutLigne { Brouillon, Validee }

/// <summary>Ligne de facture immuable : chaque saisie produit un nouvel enregistrement.</summary>
public sealed record LigneFacture(
    Guid Id,
    string Article,
    string Designation,
    decimal Quantite,
    string Unite,
    decimal PrixUnitaireHt,
    decimal RemisePourcent,
    decimal TauxTva,
    StatutLigne Statut = StatutLigne.Brouillon)
{
    public static LigneFacture Vide() => new(Guid.NewGuid(), "", "", 1m, "u", 0m, 0m, 20m);

    public decimal MontantHt => CalculLigne.MontantHt(this);
    public decimal MontantTtc => CalculLigne.MontantTtc(this);
}
