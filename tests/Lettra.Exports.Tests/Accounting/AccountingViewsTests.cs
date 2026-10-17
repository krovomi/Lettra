using Lettra.Calculations;
using Lettra.Contracts.Accounting;
using Lettra.Contracts.Common;
using Lettra.Exports.Application;
using Lettra.Exports.Application.Accounting;

namespace Lettra.Exports.Tests.Accounting;

/// <summary>
/// Consultation de la comptabilité (journal, balance, grand livre) calculée sur le jeu du golden file FEC : une
/// facture à deux taux, un acompte, la facture finale qui le déduit et un avoir.
/// </summary>
public sealed class AccountingViewsTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    private static IReadOnlyList<SalesJournalExportEntry> Entries => FecSamples.Year2026;

    [Fact]
    public void Le_journal_montre_la_piece_la_plus_recente_d_abord_et_s_equilibre()
    {
        SalesJournalResponse journal = AccountingViews.Journal(Entries, From, To, null, null, null, 1, 50);

        journal.Items.Select(e => e.PieceNumber).Should().Equal("FAC-000004", "FAC-000003", "FAC-000002", "FAC-000001");
        journal.TotalCount.Should().Be(4);
        journal.TotalDebit.Should().Be(journal.TotalCredit).And.BeGreaterThan(0m);
        journal.Items.Should().OnlyContain(e => e.Lines.Sum(l => l.Debit) == e.Lines.Sum(l => l.Credit), "chaque écriture est équilibrée");
    }

    [Fact]
    public void La_recherche_ignore_les_accents_et_la_casse_et_les_totaux_portent_sur_toute_la_selection()
    {
        SalesJournalResponse page1 = AccountingViews.Journal(Entries, From, To, "  boulangérie  ", null, null, 1, 2);

        page1.TotalCount.Should().Be(3);
        page1.Items.Should().HaveCount(2);
        decimal all = Entries.Where(e => e.CustomerId == FecSamples.Martin).SelectMany(e => e.Lines).Sum(l => l.Debit);
        page1.TotalDebit.Should().Be(all);

        AccountingViews.Journal(Entries, From, To, "000001", null, null, 1, 50).Items.Should().ContainSingle()
            .Which.CustomerName.Should().Be("Atelier Durand");
    }

    [Fact]
    public void Le_filtre_de_compte_et_de_client_garde_les_ecritures_qui_le_mouvementent()
    {
        AccountingViews.Journal(Entries, From, To, null, SalesAccounts.DownPaymentsReceived, null, 1, 50).Items
            .Select(e => e.PieceNumber).Should().BeEquivalentTo(["FAC-000002", "FAC-000003"]);
        AccountingViews.Journal(Entries, From, To, null, null, FecSamples.Durand, 1, 50).Items
            .Should().ContainSingle().Which.PieceNumber.Should().Be("FAC-000001");
    }

    [Fact]
    public void La_balance_regroupe_par_compte_et_detaille_les_clients()
    {
        TrialBalanceResponse balance = AccountingViews.Balance(Entries, From, To);

        balance.Accounts.Select(a => a.Account).Should().BeInAscendingOrder(StringComparer.Ordinal)
            .And.Contain([SalesAccounts.Customers, SalesAccounts.ServicesRevenue, SalesAccounts.GoodsRevenue, SalesAccounts.VatCollected, SalesAccounts.DownPaymentsReceived]);
        balance.TotalDebit.Should().Be(balance.TotalCredit);
        balance.Accounts.Single(a => a.Account == SalesAccounts.DownPaymentsReceived).Balance.Should().Be(0m, "l'acompte est repris par la facture finale");

        AccountBalanceResponse receivable = balance.Accounts.Single(a => a.Account == SalesAccounts.Customers);
        balance.Customers.Sum(c => c.Balance).Should().Be(receivable.Balance, "la balance auxiliaire détaille le compte 411");
        balance.Customers.Select(c => c.Balance).Should().BeInDescendingOrder();
        balance.Customers.Single(c => c.CustomerId == FecSamples.Martin).EntryCount.Should().Be(3);
    }

    [Fact]
    public void Le_grand_livre_part_du_solde_d_ouverture_et_cumule_dans_l_ordre()
    {
        DateOnly april = new(2026, 3, 1);
        List<SalesJournalExportEntry> before = Entries.Where(e => e.Date < april).ToList();
        List<SalesJournalExportEntry> period = Entries.Where(e => e.Date >= april).ToList();

        AccountLedgerResponse ledger = AccountingViews.Ledger(SalesAccounts.Customers, FecSamples.Martin, before, period, april, To);

        decimal downPayment = before.Single(e => e.PieceNumber == "FAC-000002").Lines.Single(l => l.Account == SalesAccounts.Customers).Debit;
        ledger.OpeningBalance.Should().Be(downPayment, "seul l'acompte de Martin précède la période");
        ledger.Lines.Select(l => l.PieceNumber).Should().Equal("FAC-000003", "FAC-000004");
        ledger.Lines[0].RunningBalance.Should().Be(downPayment + ledger.Lines[0].Debit - ledger.Lines[0].Credit);
        ledger.Lines[1].Credit.Should().BeGreaterThan(0m, "l'avoir crédite le compte client");
        ledger.ClosingBalance.Should().Be(ledger.OpeningBalance + ledger.TotalDebit - ledger.TotalCredit).And.Be(ledger.Lines[^1].RunningBalance);
    }

    [Theory]
    [InlineData("411", true)]
    [InlineData("44571", true)]
    [InlineData("41", false)]
    [InlineData("911", false)]
    [InlineData("41A", false)]
    [InlineData(null, false)]
    public void Un_numero_de_compte_suit_le_plan_comptable(string? account, bool valid) =>
        AccountingViews.IsAccountNumber(account).Should().Be(valid);

    [Fact]
    public void Une_periode_inversee_ou_trop_longue_est_refusee()
    {
        AccountingViews.ValidatePeriod(To, From).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Accounting.PeriodInvalid);
        AccountingViews.ValidatePeriod(From, From.AddMonths(AccountingViews.MaxPeriodMonths)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ErrorCodes.Accounting.PeriodTooLong);
        AccountingViews.ValidatePeriod(From, From.AddMonths(AccountingViews.MaxPeriodMonths).AddDays(-1)).IsSuccess.Should().BeTrue();
    }
}
