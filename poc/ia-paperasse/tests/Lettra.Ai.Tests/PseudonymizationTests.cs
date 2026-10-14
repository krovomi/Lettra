using System.Text.Json;
using Lettra.Ai.Abstractions;
using Lettra.Ai.Privacy;

namespace Lettra.Ai.Tests;

public sealed class PseudonymizationTests
{
    private static PseudonymizationSession Session() => new(
    [
        new(IdentityKind.Company, "WebCraft"),
        new(IdentityKind.Client, "Dupont SARL"),
        new(IdentityKind.Address, "45 avenue de la République, 75011 Paris"),
        new(IdentityKind.Siren, "987654321"),
        new(IdentityKind.Iban, "FR76 3000 6000 0112 3456 7890 189"),
        new(IdentityKind.Email, "compta@dupont-sarl.fr"),
    ]);

    [Fact]
    public void Remplace_les_identites_connues_y_compris_les_variantes_d_ecriture()
    {
        var s = Session();
        var text = s.Pseudonymize("Facture pour dupont sarl (Dupont), SIREN 987 654 321, 45 avenue de la  République, 75011 Paris, IBAN FR7630006000011234567890189.");

        Assert.DoesNotContain("upont", text);
        Assert.DoesNotContain("987", text);
        Assert.DoesNotContain("République", text);
        Assert.DoesNotContain("FR76", text);
        Assert.Contains("[CLIENT_1]", text);
        Assert.Contains("[SIREN_1]", text);
        Assert.Contains("[IBAN_1]", text);
        Assert.Contains("[ADRESSE_1]", text);
    }

    [Fact]
    public void Detecte_les_identifiants_inconnus_mais_garde_montants_taux_et_dates()
    {
        var s = Session();
        var text = s.Pseudonymize(
            "Écrire à marc.leroy@exemple.fr ou au 06 12 34 56 78. SIRET 123 456 789 00012, TVA DE123456789, " +
            "IBAN DE89 3704 0044 0532 0130 00. Total 123456789,00 € HT au 15/09/2026, TVA 20 %, 2 400,00 EUR.");

        Assert.DoesNotContain("marc.leroy", text);
        Assert.DoesNotContain("06 12 34 56 78", text);
        Assert.DoesNotContain("00012", text);
        Assert.DoesNotContain("DE123456789", text);
        Assert.DoesNotContain("DE89", text);
        Assert.Contains("123456789,00 €", text); // un montant n'est pas un SIREN
        Assert.Contains("15/09/2026", text);
        Assert.Contains("20 %", text);
        Assert.Contains("2 400,00 EUR", text);
    }

    [Fact]
    public void Retablit_les_valeurs_dans_le_texte_et_dans_les_parametres_d_outil()
    {
        var s = Session();
        var masked = s.Pseudonymize("Devis pour Dupont SARL, contact compta@dupont-sarl.fr");
        Assert.Equal("Devis pour Dupont SARL, contact compta@dupont-sarl.fr", s.Restore(masked));

        var input = JsonSerializer.SerializeToElement(new { nom = "[CLIENT_1]", lignes = new[] { new { designation = "Conseil [CLIENT_1]", quantite = 3 } } });
        var restored = s.RestoreJson(input);
        Assert.Equal("Dupont SARL", restored.GetProperty("nom").GetString());
        Assert.Equal("Conseil Dupont SARL", restored.GetProperty("lignes")[0].GetProperty("designation").GetString());
        Assert.Equal(3, restored.GetProperty("lignes")[0].GetProperty("quantite").GetInt32());
    }

    [Fact]
    public void Pseudonymise_les_textes_d_un_json_sans_toucher_aux_nombres()
    {
        var s = Session();
        var json = s.PseudonymizeJson("""{"name":"WebCraft","siren":"987654321","capital":1000,"tax":{"tva_rate":0.2}}""");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("[SOCIETE_1]", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("[SIREN_1]", doc.RootElement.GetProperty("siren").GetString());
        Assert.Equal(1000, doc.RootElement.GetProperty("capital").GetInt32());
        Assert.Equal(0.2m, doc.RootElement.GetProperty("tax").GetProperty("tva_rate").GetDecimal());
    }

    [Fact]
    public void Signale_les_jetons_inventes_par_le_moteur()
    {
        var s = Session();
        s.Pseudonymize("Dupont SARL");
        Assert.Equal(["[CLIENT_7]"], s.UnknownTokens("[CLIENT_1] et [CLIENT_7]"));
        Assert.Equal("Dupont SARL et [CLIENT_7]", s.Restore("[CLIENT_1] et [CLIENT_7]"));
    }
}
