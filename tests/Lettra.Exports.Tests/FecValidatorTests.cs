using System.Text;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Exports.Application.Fec;

namespace Lettra.Exports.Tests;

/// <summary>Les contrôles du validateur intégré, chacun sur un FEC valide altéré en un seul point.</summary>
public sealed class FecValidatorTests
{
    private const string FileName = "552100554FEC20261231.txt";
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End = new(2026, 12, 31);

    private static string Header => string.Join('\t', FecFormat.Columns);

    private static string Row(
        string number = "1", string date = "20260115", string account = "411", string debit = "120,00", string credit = "0,00",
        string aux = "C1", string auxLabel = "Client", string validDate = "20260115", string currencyAmount = "", string currency = "",
        string lettering = "", string letteringDate = "") =>
        string.Join('\t', "VE", "Ventes", number, date, account, "Libellé", aux, auxLabel, "FAC-1", date, "Facture FAC-1", debit, credit,
            lettering, letteringDate, validDate, currencyAmount, currency);

    private static string Balanced(string number = "1", string date = "20260115") =>
        Row(number, date, validDate: date) + "\n"
        + Row(number, date, "706", "0,00", "100,00", "", "", date) + "\n"
        + Row(number, date, "44571", "0,00", "20,00", "", "", date);

    private static FecValidationResponse Validate(string text, string fileName = FileName, Encoding? encoding = null) =>
        FecValidator.Validate(fileName, (encoding ?? Encoding.UTF8).GetBytes(text), Start, End);

    private static IEnumerable<string> Errors(FecValidationResponse report) =>
        report.Findings.Where(f => f.Severity == FecFindingSeverityDto.Error).Select(f => f.Code);

    [Fact]
    public void Un_fec_equilibre_est_valide()
    {
        FecValidationResponse report = Validate(Header + "\n" + Balanced() + "\n" + Balanced("2", "20260116") + "\n");

        report.IsValid.Should().BeTrue();
        report.Findings.Should().BeEmpty();
        report.EntryCount.Should().Be(2);
        report.TotalDebit.Should().Be(240m);
    }

    [Fact]
    public void La_barre_verticale_et_l_iso_8859_15_sont_acceptes()
    {
        string text = (Header + "\n" + Balanced()).Replace('\t', '|');

        Validate(text, encoding: FecFormat.Encoding).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("FEC20261231.txt")]
    [InlineData("552100554FEC20261332.txt")]
    [InlineData("55210055FEC20261231.txt")]
    [InlineData("552100554_FEC_20261231.csv")]
    public void Un_nom_de_fichier_non_reglementaire_est_refuse(string fileName) =>
        Errors(Validate(Header + "\n" + Balanced(), fileName)).Should().Equal(ErrorCodes.Fec.FileNameInvalid);

    [Fact]
    public void Un_nom_qui_ne_porte_pas_la_date_de_cloture_est_signale() =>
        Validate(Header + "\n" + Balanced(), "552100554FEC20261130.txt").Findings
            .Should().ContainSingle(f => f.Code == ErrorCodes.Fec.FileNameInvalid && f.Severity == FecFindingSeverityDto.Warning);

    [Fact]
    public void Un_fichier_vide_est_refuse() =>
        Errors(Validate(string.Empty)).Should().Equal(ErrorCodes.Fec.Empty);

    [Fact]
    public void Un_en_tete_incomplet_est_refuse() =>
        Errors(Validate(Header.Replace("\tIdevise", string.Empty, StringComparison.Ordinal) + "\n" + Balanced())).Should().Equal(ErrorCodes.Fec.HeaderInvalid);

    [Fact]
    public void Un_separateur_inconnu_est_refuse() =>
        Errors(Validate(Header.Replace('\t', ';') + "\n")).Should().Equal(ErrorCodes.Fec.SeparatorInvalid);

    [Fact]
    public void Un_fichier_sans_ecriture_est_recevable_mais_signale()
    {
        FecValidationResponse report = Validate(Header + "\r\n");

        report.IsValid.Should().BeTrue();
        report.Findings.Select(f => f.Code).Should().Equal(ErrorCodes.Fec.NoEntries);
    }

    [Fact]
    public void Une_ligne_sans_ses_18_colonnes_est_refusee()
    {
        FecValidationResponse report = Validate(Header + "\n" + Balanced() + "\nVE\tVentes\t2");

        report.Findings.Should().ContainSingle(f => f.Code == ErrorCodes.Fec.ColumnCountInvalid && f.Line == 5);
    }

    [Fact]
    public void Un_champ_obligatoire_vide_est_refuse() =>
        Validate(Header + "\n" + Balanced().Replace("Facture FAC-1", string.Empty, StringComparison.Ordinal)).Findings
            .Should().Contain(f => f.Code == ErrorCodes.Fec.FieldRequired && f.Column == FecFormat.EcritureLib && f.Line == 2);

    [Theory]
    [InlineData("2026-01-15")]
    [InlineData("20260230")]
    public void Une_date_mal_ecrite_est_refusee(string date) =>
        Errors(Validate(Header + "\n" + Row(validDate: date) + "\n" + Row(account: "706", debit: "0,00", credit: "120,00", aux: "", auxLabel: "")))
            .Should().Contain(ErrorCodes.Fec.DateInvalid);

    [Theory]
    [InlineData("120.00")]
    [InlineData("1 200,00")]
    [InlineData("120,001")]
    [InlineData("12O,00")]
    public void Un_montant_mal_ecrit_est_refuse(string amount) =>
        Errors(Validate(Header + "\n" + Row(debit: amount))).Should().Contain(ErrorCodes.Fec.AmountInvalid);

    [Fact]
    public void Un_debit_et_un_credit_sur_la_meme_ligne_sont_refuses() =>
        Errors(Validate(Header + "\n" + Row(debit: "10,00", credit: "10,00"))).Should().Equal(ErrorCodes.Fec.DebitAndCredit);

    [Fact]
    public void Une_ligne_a_zero_est_signalee()
    {
        FecValidationResponse report = Validate(Header + "\n" + Row(debit: "0,00", credit: "0,00"));

        report.IsValid.Should().BeTrue();
        report.Findings.Select(f => f.Code).Should().Equal(ErrorCodes.Fec.ZeroLine);
    }

    [Theory]
    [InlineData("41")]
    [InlineData("9110")]
    [InlineData("4-11")]
    public void Un_compte_hors_plan_est_refuse(string account) =>
        Errors(Validate(Header + "\n" + Row(account: account, debit: "0,00", credit: "0,00"))).Should().Contain(ErrorCodes.Fec.AccountInvalid);

    [Fact]
    public void Les_couples_de_colonnes_vont_ensemble()
    {
        IEnumerable<string> codes = Errors(Validate(Header + "\n"
            + Row(auxLabel: "", debit: "0,00", lettering: "AA", currencyAmount: "10,00") + "\n"));

        codes.Should().BeEquivalentTo([ErrorCodes.Fec.AuxiliaryIncomplete, ErrorCodes.Fec.LetteringIncomplete, ErrorCodes.Fec.CurrencyIncomplete]);
    }

    [Fact]
    public void Une_ecriture_desequilibree_est_refusee()
    {
        FecValidationResponse report = Validate(Header + "\n" + Balanced().Replace("\t100,00\t", "\t99,99\t", StringComparison.Ordinal));

        report.Findings.Select(f => (f.Code, f.EntryNumber)).Should().BeEquivalentTo(
            [(ErrorCodes.Fec.EntryUnbalanced, (string?)"1"), (ErrorCodes.Fec.FileUnbalanced, (string?)null)]);
    }

    [Fact]
    public void Les_lignes_d_une_ecriture_partagent_leurs_dates()
    {
        string text = Header + "\n" + Row() + "\n" + Row(date: "20260116", validDate: "20260116", account: "706", debit: "0,00", credit: "120,00", aux: "", auxLabel: "");

        Errors(Validate(text)).Should().Equal(ErrorCodes.Fec.EntryInconsistent);
    }

    [Fact]
    public void Les_ecritures_suivent_l_ordre_de_validation() =>
        Errors(Validate(Header + "\n" + Balanced("1", "20260116") + "\n" + Balanced("2", "20260115"))).Should().Equal(ErrorCodes.Fec.NotChronological);

    [Fact]
    public void Une_validation_anterieure_a_l_ecriture_est_signalee() =>
        Validate(Header + "\n" + Row(validDate: "20260110") + "\n" + Row(validDate: "20260110", account: "706", debit: "0,00", credit: "120,00", aux: "", auxLabel: ""))
            .Findings.Select(f => f.Code).Should().OnlyContain(c => c == ErrorCodes.Fec.ValidationBeforeEntry);

    [Fact]
    public void Une_ecriture_hors_exercice_est_refusee() =>
        Errors(Validate(Header + "\n" + Balanced("1", "20251231"))).Should().OnlyContain(c => c == ErrorCodes.Fec.DateOutsidePeriod);

    [Fact]
    public void Les_constats_sont_tronques()
    {
        string text = Header + "\n" + string.Join('\n', Enumerable.Range(1, FecValidator.MaxFindings + 50).Select(_ => "VE"));

        FecValidationResponse report = Validate(text);

        report.Findings.Should().HaveCount(FecValidator.MaxFindings);
        report.FindingsTruncated.Should().BeTrue();
        report.IsValid.Should().BeFalse();
    }
}
