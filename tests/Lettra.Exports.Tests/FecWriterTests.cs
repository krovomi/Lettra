using Lettra.Calculations;
using Lettra.Exports.Application;
using Lettra.Exports.Application.Fec;

namespace Lettra.Exports.Tests;

public sealed class FecWriterTests
{
    private const string GoldenName = "552100554FEC20261231.txt";

    /// <summary>
    /// Le golden file fige le FEC du jeu fictif ; il est relu par l'expert-comptable (jalon J2). Pour le régénérer après
    /// un changement voulu : <c>LETTRA_UPDATE_GOLDEN=1 dotnet test</c>, puis relire le diff.
    /// </summary>
    [Fact]
    public void Le_fec_du_jeu_fictif_est_identique_au_golden_file()
    {
        string actual = FecWriter.WriteText(FecSamples.Year2026);
        if (Environment.GetEnvironmentVariable("LETTRA_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(Path.Combine(SourceDirectory(), "Golden", GoldenName), actual.Replace("\r\n", "\n", StringComparison.Ordinal));
        }

        string golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", GoldenName));
        actual.Replace("\r\n", "\n", StringComparison.Ordinal).Should().Be(golden.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void Le_golden_file_passe_le_validateur()
    {
        byte[] content = FecWriter.Write(FecSamples.Year2026);

        var report = FecValidator.Validate(GoldenName, content, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        report.IsValid.Should().BeTrue();
        report.Findings.Should().BeEmpty();
        report.EntryCount.Should().Be(4);
        report.LineCount.Should().Be(1 + FecSamples.Year2026.Sum(e => e.Lines.Count));
        report.TotalDebit.Should().Be(report.TotalCredit).And.BePositive();
    }

    [Fact]
    public void Le_fichier_est_en_iso_8859_15_avec_des_fins_de_ligne_crlf()
    {
        byte[] content = FecWriter.Write(FecSamples.Year2026);

        // « et » de « Martin » : 0xAB et 0xBB en latin-9, pas de BOM ni d'octets UTF-8.
        content.Should().Contain(0xAB).And.Contain(0xBB).And.NotContain(0xC2);
        content[..3].Should().NotEqual([0xEF, 0xBB, 0xBF]);
        FecFormat.Encoding.GetString(content).Should().Contain("\r\n").And.NotContain("\r\r");
    }

    [Fact]
    public void Chaque_ecriture_reprend_les_lignes_enregistrees_sans_recalcul()
    {
        string[][] rows = [.. FecWriter.WriteText(FecSamples.Year2026).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.Split('\t'))];

        foreach ((SalesJournalExportEntry entry, int index) in FecSamples.Year2026.Select((e, i) => (e, i)))
        {
            string[][] lines = [.. rows.Where(r => r[2] == (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))];
            lines.Select(r => (r[4], r[11], r[12])).Should().Equal(entry.Lines.Select(l => (l.Account, FecFormat.FormatAmount(l.Debit), FecFormat.FormatAmount(l.Credit))));
            lines.Should().OnlyContain(r => r[8] == entry.PieceNumber && r[3] == FecFormat.FormatDate(entry.Date) && r[15] == r[3]);
        }
    }

    [Fact]
    public void Seul_le_compte_client_porte_le_compte_auxiliaire()
    {
        string[][] rows = [.. FecWriter.WriteText(FecSamples.Year2026).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.Split('\t'))];

        rows.Where(r => r[4] == SalesAccounts.Customers).Should().OnlyContain(r => r[6].Length == 11 && r[7].Length > 0);
        rows.Where(r => r[4] != SalesAccounts.Customers).Should().OnlyContain(r => r[6].Length == 0 && r[7].Length == 0);
        FecWriter.AuxiliaryAccountOf(FecSamples.Durand).Should().Be("C000000A001");
    }

    [Theory]
    [InlineData("Ligne\tavec\ttabulations", "Ligne avec tabulations")]
    [InlineData("L’atelier — « neuf »…", "L'atelier - « neuf ».")]
    [InlineData("A | B\r\nC", "A B C")]
    [InlineData("  ", "")]
    public void Les_libelles_sont_nettoyes(string raw, string expected) =>
        FecFormat.Clean(raw).Should().Be(expected);

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
