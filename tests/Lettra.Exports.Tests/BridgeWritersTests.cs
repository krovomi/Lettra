using System.Text;
using Lettra.Contracts.Exports;
using Lettra.Exports.Application.Bridges;
using Lettra.Exports.Application.Fec;

namespace Lettra.Exports.Tests;

public sealed class BridgeWritersTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    private static IReadOnlyList<ExportLine> Lines => [.. FecWriter.Lines(FecSamples.Year2026)];

    private static string[] Rows(string text) => text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Les_lignes_communes_reprennent_celles_du_fec()
    {
        string[][] fec = [.. Rows(FecWriter.WriteText(FecSamples.Year2026)).Skip(1).Select(r => r.Split('\t'))];

        Lines.Should().HaveCount(fec.Length);
        Lines.Select(l => (l.Journal, l.Account, FecFormat.FormatAmount(l.Debit), FecFormat.FormatAmount(l.Credit), l.AuxiliaryAccount ?? string.Empty))
            .Should().Equal(fec.Select(r => (r[0], r[4], r[11], r[12], r[6])));
    }

    [Theory]
    [InlineData(AccountingSoftwareDto.Quadra, "552100554-quadra-20260101-20261231.txt", "text/plain; charset=iso-8859-15")]
    [InlineData(AccountingSoftwareDto.Sage, "552100554-sage-20260101-20261231.csv", "text/csv; charset=iso-8859-15")]
    [InlineData(AccountingSoftwareDto.Cegid, "552100554-cegid-20260101-20261231.csv", "text/csv; charset=utf-8")]
    [InlineData(AccountingSoftwareDto.Acd, "552100554-acd-20260101-20261231.csv", "text/csv; charset=iso-8859-15")]
    public void Le_fichier_porte_le_siren_le_logiciel_et_la_periode(AccountingSoftwareDto software, string fileName, string contentType)
    {
        BridgeFile file = BridgeWriters.Write(software, "552100554", From, To, FecSamples.Year2026);

        file.FileName.Should().Be(fileName);
        file.ContentType.Should().Be(contentType);
        file.Content.Should().NotBeEmpty();
    }

    [Fact]
    public void Un_logiciel_inconnu_est_refuse()
    {
        Action write = () => BridgeWriters.Write((AccountingSoftwareDto)99, "552100554", From, To, FecSamples.Year2026);

        write.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Quadra_ecrit_les_comptes_clients_puis_les_mouvements_a_positions_fixes()
    {
        string[] rows = Rows(BridgeWriters.Quadra(Lines));

        string[] accounts = [.. rows.Where(r => r[0] == 'C')];
        string[] moves = [.. rows.Where(r => r[0] == 'M')];
        accounts.Should().HaveCount(2, "deux clients dans le jeu fictif");
        accounts[0].Should().HaveLength(218);
        accounts[0].Substring(1, 8).Should().Be("C000A001");
        accounts[0].Substring(9, 30).TrimEnd().Should().Be("Atelier Durand");
        accounts[0].Substring(98, 8).Should().Be("41100000");
        accounts[0][217].Should().Be('C');

        moves.Should().HaveCount(Lines.Count).And.OnlyContain(r => r.Length == 251);
        string first = moves[0];
        first.Substring(1, 8).Should().Be("C000A001", "le client porte son compte auxiliaire");
        first.Substring(9, 2).Should().Be("VE");
        first.Substring(11, 3).Should().Be("000");
        first.Substring(14, 6).Should().Be("150126");
        first[41].Should().Be('D');
        first.Substring(42, 13).Should().Be(BridgeWriters.QuadraAmount(Lines[0].Debit));
        first.Substring(107, 3).Should().Be("EUR");
        first.Substring(110, 3).TrimEnd().Should().Be("VE");
        first.Substring(231, 20).TrimEnd().Should().Be("FAC-000001");
        moves.Skip(1).First().Substring(1, 8).Should().Be("70600000", "les comptes généraux sont complétés à huit caractères");
    }

    [Fact]
    public void Quadra_equilibre_les_debits_et_les_credits_en_centimes()
    {
        string[] moves = [.. Rows(BridgeWriters.Quadra(Lines)).Where(r => r[0] == 'M')];

        long Total(char sense) => moves.Where(r => r[41] == sense).Sum(r => long.Parse(r.AsSpan(42, 13), System.Globalization.CultureInfo.InvariantCulture));
        Total('D').Should().Be(Total('C')).And.BePositive();
    }

    [Theory]
    [InlineData(120.5, "+000000012050")]
    [InlineData(0.004, "+000000000000")]
    [InlineData(-3, "-000000000300")]
    public void Le_montant_quadra_est_en_centimes_signes(decimal amount, string expected) =>
        BridgeWriters.QuadraAmount(amount).Should().Be(expected);

    [Fact]
    public void Sage_ecrit_un_en_tete_puis_une_ligne_par_ligne_d_ecriture()
    {
        string[] rows = Rows(BridgeWriters.Sage(Lines));

        rows[0].Should().Be("Code journal;Date;N° compte général;N° compte tiers;N° pièce;Libellé écriture;Débit;Crédit;Lettrage");
        rows.Should().HaveCount(Lines.Count + 1);
        string[] first = rows[1].Split(';');
        first.Should().HaveCount(9);
        first[0..5].Should().Equal("VE", "150126", "411000", "C000000A001", "FAC-000001");
        first[6].Should().Be(FecFormat.FormatAmount(Lines[0].Debit));
        first[7].Should().BeEmpty();
    }

    [Fact]
    public void Cegid_ecrit_le_modele_ecritures_excel_sans_en_tete()
    {
        string[] rows = Rows(BridgeWriters.Cegid(Lines));

        rows.Should().HaveCount(Lines.Count);
        string[] first = rows[0].Split(';');
        first.Should().HaveCount(8);
        first[0..4].Should().Equal("VE", "15/01/2026", "411000", "C000000A001");
        first[6].Should().Be(Lines[0].Debit.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        rows.Select(r => r.Split(';')[5]).Should().OnlyContain(label => label.Length <= 35);
    }

    [Fact]
    public void Acd_ecrit_un_en_tete_et_le_libelle_du_client()
    {
        string[] rows = Rows(BridgeWriters.Acd(Lines));

        rows[0].Split(';').Should().HaveCount(10);
        string[] first = rows[1].Split(';');
        first[0..5].Should().Equal("VE", "15/01/2026", "411000", "C000000A001", "Atelier Durand");
        rows.Skip(1).Select(r => r.Split(';')[2]).Should().Contain(["706000", "445710"]);
    }

    [Fact]
    public void Un_point_virgule_dans_un_libelle_ne_casse_pas_les_colonnes()
    {
        ExportLine line = Lines[0] with { Label = "Facture; urgente" };

        string row = Rows(BridgeWriters.Acd([line]))[1];

        row.Split(';').Should().HaveCount(10);
        row.Should().Contain("Facture, urgente");
    }

    [Fact]
    public void Cegid_est_en_utf8_et_les_autres_en_latin_9()
    {
        byte[] cegid = BridgeWriters.Write(AccountingSoftwareDto.Cegid, "552100554", From, To, FecSamples.Year2026).Content;
        byte[] sage = BridgeWriters.Write(AccountingSoftwareDto.Sage, "552100554", From, To, FecSamples.Year2026).Content;

        cegid.Should().Equal(new UTF8Encoding(false).GetBytes(BridgeWriters.Cegid(Lines)));
        sage.Should().Contain(0xAB).And.NotContain(0xC2);
    }

    [Theory]
    [InlineData("706", 6, "706000")]
    [InlineData("44571", 6, "445710")]
    [InlineData("4191", 8, "41910000")]
    [InlineData("41100000", 6, "41100000")]
    public void Un_compte_general_est_complete_de_zeros(string account, int length, string expected) =>
        BridgeWriters.PadAccount(account, length).Should().Be(expected);
}
