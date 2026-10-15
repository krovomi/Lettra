using System.Globalization;
using Lettra.Calculations;
using Lettra.Contracts.Billing;
using Lettra.Printing;
using Lettra.UI.Shared.Billing;
using Lettra.UI.Shared.Components.Palette;

namespace Lettra.UI.Tests.Billing;

public sealed class RunningTotalsTests
{
    private static LineInput Ligne(decimal quantite, decimal prix, decimal taux, decimal remise = 0m) => new(quantite, prix, taux, remise);

    [Fact]
    public void Les_totaux_tenus_ligne_par_ligne_egalent_toujours_le_calcul_complet()
    {
        Random hasard = new(20261015);
        decimal[] taux = [20m, 10m, 5.5m, 2.1m, 0m];
        List<LineInput> lignes = [];
        RunningTotals totaux = new();

        for (int tour = 0; tour < 2000; tour++)
        {
            LineInput nouvelle = Ligne(hasard.Next(1, 4000) / 1000m, hasard.Next(1, 1_000_000) / 100m, taux[hasard.Next(taux.Length)],
                hasard.Next(0, 4) == 0 ? hasard.Next(0, 10001) / 100m : 0m);
            switch (lignes.Count == 0 ? 0 : hasard.Next(3))
            {
                case 0:
                    lignes.Add(nouvelle);
                    totaux.Add(nouvelle);
                    break;
                case 1:
                    int remplacee = hasard.Next(lignes.Count);
                    totaux.Replace(lignes[remplacee], nouvelle);
                    lignes[remplacee] = nouvelle;
                    break;
                default:
                    int retiree = hasard.Next(lignes.Count);
                    totaux.Remove(lignes[retiree]);
                    lignes.RemoveAt(retiree);
                    break;
            }

            totaux.Totals.Should().BeEquivalentTo(DocumentCalculator.Compute(lignes), o => o.WithStrictOrdering(), $"au tour {tour}");
        }
    }

    [Fact]
    public void La_TVA_est_calculee_par_taux_sur_la_somme_des_bases_arrondies()
    {
        RunningTotals totaux = new();
        totaux.Reset([Ligne(1m, 0.07m, 20m), Ligne(1m, 0.07m, 20m), Ligne(1m, 0.07m, 20m)]);

        // Arrondie ligne par ligne, la TVA ferait 3 × 0,01 = 0,03 ; sur la base de 0,21 elle vaut 0,04.
        totaux.Totals.Breakdown.Should().ContainSingle().Which.Should().Be(new VatBreakdownLine(20m, 0.21m, 0.04m));
        totaux.Totals.TotalInclTax.Should().Be(0.25m);
    }

    [Fact]
    public void Un_taux_sans_ligne_disparait_de_la_ventilation()
    {
        RunningTotals totaux = new();
        LineInput reduite = Ligne(2m, 10m, 5.5m);
        totaux.Reset([Ligne(1m, 100m, 20m), reduite]);

        totaux.Remove(reduite);

        totaux.Totals.Breakdown.Select(b => b.VatRatePercent).Should().Equal(20m);
    }
}

public sealed class DocumentLineRowTests
{
    private static readonly DocumentLineRow Ligne = DocumentLineRow.New(new("FR-10", 10m)) with { Designation = "  Audit  ", Quantity = 2m, UnitPriceExclTax = 150m };

    [Fact]
    public void Hors_regime_normal_ou_exoneree_la_ligne_ne_porte_pas_de_TVA()
    {
        Ligne.EffectiveVatPercent(VatRegimeDto.Standard).Should().Be(10m);
        Ligne.EffectiveVatPercent(VatRegimeDto.Franchise).Should().Be(0m);
        Ligne.EffectiveVatPercent(VatRegimeDto.ReverseChargeIntraEu).Should().Be(0m);
        (Ligne with { VatExemptionCode = "VATEX-FR-261" }).EffectiveVatPercent(VatRegimeDto.Standard).Should().Be(0m);
    }

    [Fact]
    public void La_requete_nettoie_les_textes_et_garde_le_code_de_TVA()
    {
        DocumentLineRequest requete = (Ligne with { Reference = " " }).ToRequest();

        requete.Designation.Should().Be("Audit");
        requete.Reference.Should().BeNull();
        requete.VatRateCode.Should().Be("FR-10");
        requete.Quantity.Should().Be(2m);
    }

    [Fact]
    public void Une_remise_hors_bornes_n_affiche_pas_de_total_au_lieu_de_lever()
    {
        (Ligne with { DiscountPercent = 120m }).TotalExclTax.Should().Be(0m);
        Ligne.TotalExclTax.Should().Be(300m);
    }

    [Fact]
    public void Une_ligne_sans_designation_n_est_pas_encore_envoyee()
    {
        DocumentLineRow.New(new("FR-20", 20m)).IsIncomplete.Should().BeTrue();
        (Ligne with { Designation = " ", UnitPriceExclTax = 10m }).IsIncomplete.Should().BeTrue();
        Ligne.IsIncomplete.Should().BeFalse();
    }
}

public sealed class DraftAutosaveTests
{
    [Fact]
    public async Task Une_saisie_continue_ne_declenche_qu_un_enregistrement()
    {
        int enregistrements = 0;
        using DraftAutosave autosave = new(() => { enregistrements++; return Task.CompletedTask; }, TimeSpan.FromMilliseconds(80));

        Task premiere = autosave.ScheduleAsync();
        Task deuxieme = autosave.ScheduleAsync();
        Task derniere = autosave.ScheduleAsync();
        await Task.WhenAll(premiere, deuxieme, derniere);

        enregistrements.Should().Be(1);
        autosave.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task Flush_enregistre_tout_de_suite_ce_qui_attend_et_rien_sinon()
    {
        int enregistrements = 0;
        using DraftAutosave autosave = new(() => { enregistrements++; return Task.CompletedTask; }, TimeSpan.FromMinutes(5));

        _ = autosave.ScheduleAsync();
        await autosave.FlushAsync();
        await autosave.FlushAsync();

        enregistrements.Should().Be(1);
    }

    [Fact]
    public async Task Discard_abandonne_l_enregistrement_en_attente()
    {
        int enregistrements = 0;
        using DraftAutosave autosave = new(() => { enregistrements++; return Task.CompletedTask; }, TimeSpan.FromMilliseconds(50));

        Task attente = autosave.ScheduleAsync();
        autosave.Discard();
        await attente;
        await autosave.FlushAsync();

        enregistrements.Should().Be(0);
    }
}

public sealed class PaletteMatcherTests
{
    [Theory]
    [InlineData("Créer une facture", "facture", true)]
    [InlineData("Créer une facture", "creer fact", true)]
    [InlineData("Écrans", "ecran", true)]
    [InlineData("Avoir sur FAC-2026-0042 · Dupont", "fac-2026-0042", true)]
    [InlineData("Nouveau devis", "facture", false)]
    public void La_recherche_ignore_accents_et_casse_et_exige_tous_les_mots(string libelle, string recherche, bool trouve) =>
        (PaletteMatcher.Score(libelle, recherche) > 0).Should().Be(trouve);

    [Fact]
    public void Un_debut_de_mot_compte_plus_qu_un_milieu_de_mot() =>
        PaletteMatcher.Score("Devis Dupont", "dup").Should().BeGreaterThan(PaletteMatcher.Score("Devis Sodupont", "dup"));
}

public sealed class PrintableMapperTests
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    private static readonly DocumentLineRequest Heures = new() { Designation = "Conseil", UnitCode = "HUR", Quantity = 3m, UnitPriceExclTax = 120m };

    [Fact]
    public void Les_unites_sont_dans_la_langue_du_document_et_non_celle_de_l_utilisateur()
    {
        InvoiceResponse facture = FakeInvoicesGateway.Build(InvoiceKindDto.Invoice, InvoiceStatusDto.Validated, "FAC-2026-0042", Heures);

        PrintableMapper.FromInvoice(facture, null, Fr).Lines.Single().UnitLabel.Should().Be("heure");
        PrintableMapper.FromInvoice(facture, null, En).Lines.Single().UnitLabel.Should().Be("hour");
        PrintableMapper.UnitLabel("XYZ", Fr).Should().Be("XYZ");
    }

    [Fact]
    public void Une_piece_validee_imprime_l_emetteur_fige_et_un_brouillon_la_fiche_entreprise()
    {
        InvoiceResponse validee = FakeInvoicesGateway.Build(InvoiceKindDto.Invoice, InvoiceStatusDto.Validated, "FAC-2026-0042", Heures)
            with { Seller = BillingFixtures.Cabinet with { Name = "Ancien nom" } };
        InvoiceResponse brouillon = FakeInvoicesGateway.Build(InvoiceKindDto.Invoice, InvoiceStatusDto.Draft, null, Heures);

        PrintableMapper.FromInvoice(validee, BillingFixtures.Company(), Fr).Seller.Name.Should().Be("Ancien nom");
        PrintableDocument imprime = PrintableMapper.FromInvoice(brouillon, BillingFixtures.Company(), Fr);
        imprime.Seller.Name.Should().Be("Cabinet Martin");
        imprime.Seller.AddressLines.Should().Equal("12 avenue Foch", "Bâtiment B", "75116 Paris");
        imprime.Seller.ShareCapital.Should().Be("50\u202f000\u00a0€");
        imprime.Number.Should().BeNull();
        imprime.Iban.Should().Be("FR7630006000011234567890189");
    }

    [Fact]
    public void Un_avoir_rappelle_la_facture_d_origine_et_son_motif()
    {
        InvoiceResponse avoir = FakeInvoicesGateway.Build(InvoiceKindDto.CreditNote, InvoiceStatusDto.Validated, "AV-2026-0003", Heures)
            with { CreditedInvoiceNumber = "FAC-2026-0042", CreditReason = "Remise commerciale", QuoteNumber = "DEV-2026-0001" };

        PrintableDocument imprime = PrintableMapper.FromInvoice(avoir, null, Fr);

        imprime.Kind.Should().Be(PrintableDocumentKind.CreditNote);
        imprime.RelatedDocumentNumber.Should().Be("FAC-2026-0042");
        imprime.Notes.Should().Contain("Remise commerciale");
    }

    [Fact]
    public void Un_devis_prend_sa_date_de_validite_comme_echeance()
    {
        FakeQuotesGateway devis = new();
        QuoteResponse quote = devis.Add(QuoteStatusDto.Validated, "DEV-2026-0007", Heures);

        PrintableDocument imprime = PrintableMapper.FromQuote(quote, BillingFixtures.Company(), Fr);

        imprime.Kind.Should().Be(PrintableDocumentKind.Quote);
        imprime.DueDate.Should().Be(new DateOnly(2026, 11, 15));
        imprime.Buyer.Name.Should().Be("Dupont SARL");
        imprime.Notes.Should().Be("Mission annuelle");
    }
}
