using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Features.Cabinet;
using Lettra.Contracts.Insights;

namespace Lettra.Application.Tests;

/// <summary>Le rapprochement intra-groupe : couples, virements reconnus par numéro puis par montant, écarts.</summary>
public sealed class IntercompanyReconciliationTests
{
    private static readonly GroupCompany Mere = new(Guid.NewGuid(), "Holding Éole", "552100554", "FR7630006000011234567890189");
    private static readonly GroupCompany Fille = new(Guid.NewGuid(), "Éole Services", "732829320", "FR7610107001011234567890129");
    private static readonly DateOnly Day = new(2026, 6, 1);

    private static IntercompanySale Sale(string number, decimal net, decimal paid, int day = 0) =>
        new(Guid.NewGuid(), number, Day.AddDays(day), Fille.Siren, net, paid);

    private static IntercompanyDebit Debit(decimal amount, string text, int day = 5) => new(Mere.Siren, Day.AddDays(day), amount, text);

    private static IntercompanyFacts Sold(params IntercompanySale[] sales) => new(sales, [], [], HasBankLines: true);

    private static IntercompanyFacts Bought(IntercompanySupplier[] suppliers, bool bank, params IntercompanyDebit[] debits) => new([], suppliers, debits, bank);

    private static IntercompanyPairResponse Single(IntercompanyFacts seller, IntercompanyFacts? buyer) =>
        IntercompanyReconciliation.Pairs([Mere, Fille], buyer is null
            ? new Dictionary<Guid, IntercompanyFacts> { [Mere.Dossier] = seller }
            : new Dictionary<Guid, IntercompanyFacts> { [Mere.Dossier] = seller, [Fille.Dossier] = buyer }).Should().ContainSingle().Subject;

    [Fact]
    public void Des_comptes_concordants_ne_montrent_aucun_ecart()
    {
        IntercompanyPairResponse pair = Single(
            Sold(Sale("F-1", 1_200m, 1_200m), Sale("F-2", 600m, 600m, day: 10)),
            Bought([new(Mere.Siren, Mere.Iban)], true, Debit(1_200m, "VIR F-1"), Debit(600m, "VIREMENT GROUPE", day: 20)));

        pair.Gaps.Should().BeEmpty();
        pair.Invoices.Should().OnlyContain(i => i.SeenAtBuyer && i.Gap == null);
        (pair.InvoicedInclTax, pair.PaidBySeller, pair.PaidByBuyer, pair.Difference).Should().Be((1_800m, 1_800m, 1_800m, 0m));
        (pair.SellerName, pair.BuyerName).Should().Be((Mere.Name, Fille.Name));
    }

    [Fact]
    public void Le_numero_dans_le_libelle_passe_avant_le_montant_et_un_virement_ne_paie_qu_une_facture()
    {
        IntercompanyPairResponse pair = Single(
            Sold(Sale("F-1", 500m, 500m), Sale("F-2", 500m, 500m, day: 1)),
            Bought([new(Mere.Siren, Mere.Iban)], true, Debit(500m, "REGLEMENT f-2")));

        pair.Invoices.Single(i => i.Number == "F-2").SeenAtBuyer.Should().BeTrue();
        pair.Invoices.Single(i => i.Number == "F-1").Gap.Should().Be(IntercompanyGapDto.PaidNotSeen);
        pair.Gaps.Should().Equal(IntercompanyGapDto.PaidNotSeen, IntercompanyGapDto.TotalsDiffer);
    }

    [Fact]
    public void Fournisseur_absent_iban_different_et_virement_sans_encaissement_sont_signales()
    {
        IntercompanyPairResponse missing = Single(Sold(Sale("F-1", 300m, 0m)), Bought([], true, Debit(300m, "F-1")));
        IntercompanyPairResponse otherIban = Single(Sold(Sale("F-1", 300m, 300m)), Bought([new(Mere.Siren, "FR76 1234 5678 9012 3456 7890 123")], true, Debit(300m, "F-1")));
        IntercompanyPairResponse sameIbanSpaced = Single(Sold(Sale("F-1", 300m, 300m)), Bought([new(Mere.Siren, "fr76 3000 6000 0112 3456 7890 189")], true, Debit(300m, "F-1")));

        missing.Gaps.Should().Equal(IntercompanyGapDto.SupplierMissing, IntercompanyGapDto.SeenNotPaid, IntercompanyGapDto.TotalsDiffer);
        otherIban.Gaps.Should().Equal(IntercompanyGapDto.SupplierIbanDiffers);
        sameIbanSpaced.Gaps.Should().BeEmpty();
    }

    [Fact]
    public void Sans_banque_chez_l_acheteur_les_paiements_ne_sont_pas_compares()
    {
        IntercompanyPairResponse noBank = Single(Sold(Sale("F-1", 300m, 300m)), Bought([new(Mere.Siren, null)], false));
        IntercompanyPairResponse unreadable = Single(Sold(Sale("F-1", 300m, 300m)), null);

        noBank.BuyerBankLinked.Should().BeFalse();
        noBank.Gaps.Should().BeEmpty();
        unreadable.Gaps.Should().BeEmpty("le dossier de l'acheteur n'a pas pu être lu");
    }

    [Fact]
    public void Une_facture_entierement_avoiree_n_attend_aucun_virement()
    {
        IntercompanyPairResponse pair = Single(Sold(Sale("F-1", 0m, 0m)), Bought([new(Mere.Siren, Mere.Iban)], true));

        pair.Gaps.Should().BeEmpty();
    }

    [Fact]
    public void Sans_vente_entre_elles_aucun_couple_et_les_couples_a_regler_viennent_d_abord()
    {
        IntercompanyReconciliation.Pairs([Mere, Fille], new Dictionary<Guid, IntercompanyFacts> { [Mere.Dossier] = Sold() }).Should().BeEmpty();

        IntercompanyFacts mere = new([Sale("F-1", 100m, 0m)], [new(Fille.Siren, Fille.Iban)], [], false);
        IntercompanyFacts fille = new([new(Guid.NewGuid(), "S-1", Day, Mere.Siren, 50m, 50m)], [], [], false);
        IReadOnlyList<IntercompanyPairResponse> pairs = IntercompanyReconciliation.Pairs(
            [Mere, Fille],
            new Dictionary<Guid, IntercompanyFacts> { [Mere.Dossier] = mere, [Fille.Dossier] = fille });

        pairs.Select(p => p.SellerName).Should().Equal(Mere.Name, Fille.Name);
        pairs[1].Gaps.Should().BeEmpty();
        pairs[0].Gaps.Should().Equal(IntercompanyGapDto.SupplierMissing);
    }
}
