using System.Text.Json;
using Lettra.Ai.Privacy;
using Lettra.Application.Abstractions.Ai;

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
        string text = Session().Pseudonymize(
            "Facture pour dupont sarl (Dupont), SIREN 987 654 321, 45 avenue de la  République, 75011 Paris, IBAN FR7630006000011234567890189.");

        text.Should().NotContain("upont").And.NotContain("987").And.NotContain("République").And.NotContain("FR76");
        text.Should().Contain("[CLIENT_1]").And.Contain("[SIREN_1]").And.Contain("[IBAN_1]").And.Contain("[ADRESSE_1]");
    }

    [Fact]
    public void Detecte_les_identifiants_inconnus_mais_garde_montants_taux_et_dates()
    {
        string text = Session().Pseudonymize(
            "Écrire à marc.leroy@exemple.fr ou au 06 12 34 56 78. SIRET 123 456 789 00012, TVA DE123456789, " +
            "IBAN DE89 3704 0044 0532 0130 00. Total 123456789,00 € HT au 15/09/2026, TVA 20 %, 2 400,00 EUR.");

        text.Should().NotContain("marc.leroy").And.NotContain("06 12 34 56 78").And.NotContain("00012")
            .And.NotContain("DE123456789").And.NotContain("DE89");

        // Un montant n'est pas un SIREN ; les dates et les taux restent lisibles.
        text.Should().Contain("123456789,00 €").And.Contain("15/09/2026").And.Contain("20 %").And.Contain("2 400,00 EUR");
    }

    [Fact]
    public void Retablit_les_valeurs_dans_le_texte_et_dans_les_parametres_d_outil()
    {
        PseudonymizationSession session = Session();
        string masked = session.Pseudonymize("Devis pour Dupont SARL, contact compta@dupont-sarl.fr");
        session.Restore(masked).Should().Be("Devis pour Dupont SARL, contact compta@dupont-sarl.fr");

        JsonElement input = JsonSerializer.SerializeToElement(new { nom = "[CLIENT_1]", lignes = new[] { new { designation = "Conseil [CLIENT_1]", quantite = 3 } } });
        JsonElement restored = session.RestoreJson(input);

        restored.GetProperty("nom").GetString().Should().Be("Dupont SARL");
        restored.GetProperty("lignes")[0].GetProperty("designation").GetString().Should().Be("Conseil Dupont SARL");
        restored.GetProperty("lignes")[0].GetProperty("quantite").GetInt32().Should().Be(3);
    }

    [Fact]
    public void Pseudonymise_les_textes_d_un_json_sans_toucher_aux_nombres()
    {
        string json = Session().PseudonymizeJson("""{"name":"WebCraft","siren":"987654321","capital":1000,"tax":{"tva_rate":0.2}}""");

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        root.GetProperty("name").GetString().Should().Be("[SOCIETE_1]");
        root.GetProperty("siren").GetString().Should().Be("[SIREN_1]");
        root.GetProperty("capital").GetInt32().Should().Be(1000);
        root.GetProperty("tax").GetProperty("tva_rate").GetDecimal().Should().Be(0.2m);
    }

    [Fact]
    public void Signale_les_jetons_inventes_par_le_moteur()
    {
        PseudonymizationSession session = Session();
        session.Pseudonymize("Dupont SARL");

        session.UnknownTokens("[CLIENT_1] et [CLIENT_7]").Should().Equal("[CLIENT_7]");
        session.Restore("[CLIENT_1] et [CLIENT_7]").Should().Be("Dupont SARL et [CLIENT_7]");
    }
}
