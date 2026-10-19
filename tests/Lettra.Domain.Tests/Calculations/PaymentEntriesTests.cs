using Lettra.Calculations;

namespace Lettra.Domain.Tests.Calculations;

/// <summary>Écriture d'un encaissement (journal de banque) et TVA sur les encaissements (vague 6).</summary>
public sealed class PaymentEntriesTests
{
    private static void Balanced(IReadOnlyList<AccountingEntryLine> entry) => entry.Sum(l => l.Debit).Should().Be(entry.Sum(l => l.Credit));

    [Fact]
    public void Un_reglement_exact_debite_la_banque_et_credite_le_client()
    {
        IReadOnlyList<AccountingEntryLine> entry = PaymentEntryCalculator.Compute(120m, 120m, []);

        entry.Should().Equal(new AccountingEntryLine("512", 120m, 0m), new AccountingEntryLine("411", 0m, 120m));
    }

    [Fact]
    public void Un_manque_sous_le_seuil_passe_en_658_et_un_trop_percu_en_758()
    {
        IReadOnlyList<AccountingEntryLine> short_ = PaymentEntryCalculator.Compute(99.60m, 100m, []);
        short_.Should().Contain(new AccountingEntryLine("658", 0.40m, 0m));
        Balanced(short_);

        IReadOnlyList<AccountingEntryLine> over = PaymentEntryCalculator.Compute(100.30m, 100m, []);
        over.Should().Contain(new AccountingEntryLine("758", 0m, 0.30m));
        Balanced(over);
    }

    [Fact]
    public void La_tva_sur_encaissements_passe_de_44574_a_44571_par_taux()
    {
        IReadOnlyList<AccountingEntryLine> entry = PaymentEntryCalculator.Compute(180m, 180m, [new(20m, 20m), new(10m, 5m), new(20m, 10m)]);

        entry.Should().ContainInOrder(
            new AccountingEntryLine("44574", 30m, 0m, 20m), new AccountingEntryLine("44571", 0m, 30m, 20m),
            new AccountingEntryLine("44574", 5m, 0m, 10m), new AccountingEntryLine("44571", 0m, 5m, 10m));
        Balanced(entry);
    }

    [Fact]
    public void La_contre_passation_inverse_tous_les_sens()
    {
        IReadOnlyList<AccountingEntryLine> forward = PaymentEntryCalculator.Compute(99.60m, 100m, [new(20m, 16.67m)]);
        IReadOnlyList<AccountingEntryLine> reversal = PaymentEntryCalculator.Compute(99.60m, 100m, [new(20m, 16.67m)], reversal: true);

        reversal.Should().Equal(forward.Select(l => l with { Debit = l.Credit, Credit = l.Debit }));
        Balanced(reversal);
    }

    [Fact]
    public void Un_montant_nul_est_refuse() =>
        FluentActions.Invoking(() => PaymentEntryCalculator.Compute(0m, 10m, [])).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Un_reglement_partiel_rend_exigible_la_tva_au_prorata_et_le_dernier_solde_le_reste()
    {
        Dictionary<decimal, decimal> pending = new() { [20m] = 33.33m, [10m] = 0m };

        VatOnReceipt.Share(pending, 200m, 100m).Should().Equal(new VatTransfer(20m, 16.67m));
        VatOnReceipt.Share(new Dictionary<decimal, decimal> { [20m] = 16.66m }, 100m, 100m).Should().Equal(new VatTransfer(20m, 16.66m));
        VatOnReceipt.Share(pending, 200m, 0m).Should().BeEmpty();
    }
}
