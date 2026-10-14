using Lettra.Poc.Grille.Calculs;

namespace Lettra.Poc.Grille.Tests;

public class CalculsTests
{
    private static LigneFacture Ligne(decimal qte, decimal pu, decimal remise, decimal tva) =>
        LigneFacture.Vide() with { Quantite = qte, PrixUnitaireHt = pu, RemisePourcent = remise, TauxTva = tva };

    [Fact]
    public void Montants_de_ligne_arrondis_au_centime()
    {
        var ligne = Ligne(3m, 19.99m, 5m, 20m);
        Assert.Equal(56.97m, ligne.MontantHt);       // 56,9715
        Assert.Equal(68.36m, ligne.MontantTtc);      // 68,364
    }

    [Fact]
    public void TVA_calculee_par_taux_sur_la_base_cumulee()
    {
        var totaux = TotauxFacture.Calculer([Ligne(1m, 0.05m, 0m, 20m), Ligne(1m, 0.05m, 0m, 20m), Ligne(1m, 10m, 0m, 5.5m)]);
        Assert.Equal([5.5m, 20m], totaux.ParTaux.Select(t => t.Taux));
        Assert.Equal(0.02m, totaux.ParTaux[1].Tva);  // 0,10 × 20 % (ligne à ligne on aurait 0,01 + 0,01)
        Assert.Equal(10.10m, totaux.TotalHt);
        Assert.Equal(10.67m, totaux.TotalTtc);
    }

    [Fact]
    public void Le_cumul_incremental_donne_le_meme_resultat_que_le_recalcul_complet()
    {
        var random = new Random(3);
        var lignes = Enumerable.Range(0, 500).Select(_ => Ligne(random.Next(1, 50), random.Next(1, 50_000) / 100m, random.Next(3) * 5m, new[] { 0m, 5.5m, 10m, 20m }[random.Next(4)])).ToList();
        var cumul = CumulParTaux.Depuis(lignes);

        for (var i = 0; i < 300; i++)
        {
            var index = random.Next(lignes.Count);
            var avant = lignes[index];
            var apres = avant with { Quantite = random.Next(0, 30), TauxTva = new[] { 0m, 2.1m, 20m }[random.Next(3)] };
            lignes[index] = apres;
            cumul.Remplacer(avant, apres);
        }
        cumul.Retirer(lignes[0]);
        lignes.RemoveAt(0);

        Assert.Equal(TotauxFacture.Calculer(lignes), cumul.Totaux(), new TotauxComparer());
    }

    private sealed class TotauxComparer : IEqualityComparer<TotauxFacture>
    {
        public bool Equals(TotauxFacture? x, TotauxFacture? y) =>
            x!.TotalHt == y!.TotalHt && x.TotalTva == y.TotalTva && x.TotalTtc == y.TotalTtc && x.ParTaux.SequenceEqual(y.ParTaux);

        public int GetHashCode(TotauxFacture obj) => obj.TotalTtc.GetHashCode();
    }
}
