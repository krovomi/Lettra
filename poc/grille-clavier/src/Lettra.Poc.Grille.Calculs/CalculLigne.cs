namespace Lettra.Poc.Grille.Calculs;

/// <summary>
/// Calculs partagés client/serveur (préfigure Lettra.Calculations) : même code en WASM, MAUI et API.
/// Arrondi commercial à 2 décimales, demi vers le haut.
/// </summary>
public static class CalculLigne
{
    public static decimal Arrondir(decimal montant) => Math.Round(montant, 2, MidpointRounding.AwayFromZero);

    public static decimal MontantHt(LigneFacture l) =>
        Arrondir(l.Quantite * l.PrixUnitaireHt * (1m - l.RemisePourcent / 100m));

    public static decimal MontantTtc(LigneFacture l) =>
        Arrondir(MontantHt(l) * (1m + l.TauxTva / 100m));
}

public sealed record TotalTaux(decimal Taux, decimal BaseHt, decimal Tva);

public sealed record TotauxFacture(decimal TotalHt, IReadOnlyList<TotalTaux> ParTaux, decimal TotalTva, decimal TotalTtc)
{
    /// <summary>TVA calculée par taux sur la base cumulée (et non ligne à ligne), comme sur une facture.</summary>
    public static TotauxFacture Calculer(IEnumerable<LigneFacture> lignes) => CumulParTaux.Depuis(lignes).Totaux();
}

/// <summary>
/// Bases HT cumulées par taux de TVA, tenues à jour ligne par ligne : une saisie coûte O(1) au lieu de
/// re-sommer toute la facture. Les bases sont des sommes de montants déjà arrondis, donc additives.
/// </summary>
public sealed class CumulParTaux
{
    private readonly SortedDictionary<decimal, (decimal Base, int Lignes)> _parTaux = new();

    public static CumulParTaux Depuis(IEnumerable<LigneFacture> lignes)
    {
        var cumul = new CumulParTaux();
        foreach (var ligne in lignes) cumul.Ajouter(ligne);
        return cumul;
    }

    public void Ajouter(LigneFacture ligne) => Modifier(ligne.TauxTva, CalculLigne.MontantHt(ligne), +1);

    public void Retirer(LigneFacture ligne) => Modifier(ligne.TauxTva, -CalculLigne.MontantHt(ligne), -1);

    public void Remplacer(LigneFacture avant, LigneFacture apres)
    {
        Retirer(avant);
        Ajouter(apres);
    }

    public TotauxFacture Totaux()
    {
        var parTaux = new List<TotalTaux>(_parTaux.Count);
        decimal ht = 0m, tva = 0m;
        foreach (var (taux, (baseHt, _)) in _parTaux)
        {
            var montantTva = CalculLigne.Arrondir(baseHt * taux / 100m);
            parTaux.Add(new TotalTaux(taux, baseHt, montantTva));
            ht += baseHt;
            tva += montantTva;
        }
        return new TotauxFacture(ht, parTaux, tva, ht + tva);
    }

    private void Modifier(decimal taux, decimal montant, int lignes)
    {
        _parTaux.TryGetValue(taux, out var courant);
        var suivant = (courant.Base + montant, courant.Lignes + lignes);
        if (suivant.Item2 == 0) _parTaux.Remove(taux);
        else _parTaux[taux] = suivant;
    }
}
