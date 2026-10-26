using System.Globalization;
using System.Text;
using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Fec;
using Lettra.Exports.Application.Insights;

namespace Lettra.Exports.Tests.Insights;

/// <summary>Le contrôleur fiscal d'entraînement : chaque test sur un FEC sain altéré en un seul point.</summary>
public sealed class PracticeAuditTests
{
    private const string FileName = "552100554FEC20261231.txt";

    private static string Header => string.Join('\t', FecFormat.Columns);

    private static string Row(
        string journal, string number, string date, string account, decimal debit, decimal credit,
        string aux = "", string piece = "FAC-1", string label = "Facture", string? validated = null) =>
        string.Join(
            '\t',
            journal, journal, number, date, account, "Compte", aux, aux.Length > 0 ? "Tiers" : string.Empty, piece, date, label,
            debit.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR")), credit.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR")),
            string.Empty, string.Empty, validated ?? date, string.Empty, string.Empty);

    /// <summary>Une vente de <paramref name="net"/> € hors taxe à 20 %, réglée par le client C1.</summary>
    private static string Sale(string number, string date = "20260115", decimal net = 100m, string? piece = null, string? validated = null, string label = "Facture")
    {
        string reference = piece ?? $"FAC-{number}";
        return string.Join(
            '\n',
            Row("VE", number, date, "411000", net * 1.2m, 0m, "C1", reference, label, validated),
            Row("VE", number, date, "706000", 0m, net, piece: reference, label: label, validated: validated),
            Row("VE", number, date, "445710", 0m, net * 0.2m, piece: reference, label: label, validated: validated));
    }

    private static PracticeAuditResponse Audit(params string[] rows) =>
        PracticeAudit.Run(FileName, Encoding.UTF8.GetBytes(Header + "\n" + string.Join('\n', rows) + "\n"), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

    private static PracticeAuditCheckResponse Check(PracticeAuditResponse audit, string code) => audit.Checks.Single(c => c.Code == code);

    /// <summary>Le client C1 règle chaque vente : son compte revient à zéro.</summary>
    private static string Settled(string number, string date, decimal amount) => string.Join(
        '\n',
        Row("BQ", number, date, "512000", amount, 0m, piece: $"REG-{number}", label: "Règlement"),
        Row("BQ", number, date, "411000", 0m, amount, "C1", $"REG-{number}", "Règlement"));

    [Fact]
    public void Un_fec_sain_passe_les_treize_tests_dans_l_ordre_du_verificateur()
    {
        PracticeAuditResponse audit = Audit(Sale("1"), Sale("2", "20260116", 230m), Settled("1", "20260120", 120m), Settled("2", "20260121", 276m));

        audit.Readable.Should().BeTrue();
        audit.Entries.Should().Be(4);
        audit.Lines.Should().Be(10);
        audit.Checks.Select(c => c.Code).Should().Equal(
            "Format", "EntryNumberGap", "PieceNumberGap", "SundayOrHoliday", "LateValidation", "NegativeCash", "CashOverLimit",
            "RoundAmounts", "DuplicateLines", "Benford", "CustomerCredit", "SupplierDebit", "VatRate");
        audit.Checks.Should().OnlyContain(c => c.Severity == PracticeAuditSeverityDto.Passed, string.Join(", ", audit.Checks.Where(c => c.Severity != PracticeAuditSeverityDto.Passed).Select(c => c.Code)));
        Check(audit, "VatRate").Value.Should().Be(20m);
        audit.Benford.Should().BeEmpty("trop peu de montants pour la loi de Benford");
    }

    [Fact]
    public void Un_fichier_qui_n_est_pas_un_fec_n_est_pas_lisible()
    {
        PracticeAuditResponse audit = PracticeAudit.Run("notes.txt", "Bonjour\n"u8);

        audit.Readable.Should().BeFalse();
        audit.Checks.Should().ContainSingle().Which.Severity.Should().Be(PracticeAuditSeverityDto.Alert);
        PracticeAudit.Run("vide.txt", []).Readable.Should().BeFalse();
    }

    [Fact]
    public void Les_trous_de_numerotation_sont_des_alertes_classees_en_tete()
    {
        PracticeAuditResponse audit = Audit(Sale("1", "20260112", piece: "FAC-1"), Sale("2", "20260113", piece: "FAC-2"), Sale("4", "20260114", piece: "FAC-5"), Sale("8", "20260115", piece: "FAC-6"));

        PracticeAuditCheckResponse entries = Check(audit, "EntryNumberGap");
        entries.Severity.Should().Be(PracticeAuditSeverityDto.Alert);
        entries.Count.Should().Be(4);
        entries.Examples.Should().Equal("VE 3", "VE 5 → VE 7");
        Check(audit, "PieceNumberGap").Examples.Should().Equal("FAC-3 → FAC-4");
        audit.Checks.Take(2).Select(c => c.Code).Should().Equal(["EntryNumberGap", "PieceNumberGap"], "les alertes passent avant les tests réussis");
        audit.Checks.Skip(2).Should().OnlyContain(c => c.Severity == PracticeAuditSeverityDto.Passed);
    }

    [Fact]
    public void Un_dimanche_un_jour_ferie_et_une_validation_tardive_sont_a_surveiller()
    {
        PracticeAuditResponse audit = Audit(Sale("1", "20260104"), Sale("2", "20260501"), Sale("3", "20260115", validated: "20260401"));

        Check(audit, "SundayOrHoliday").Examples.Should().Equal("VE 1 · 2026-01-04", "VE 2 · 2026-05-01");
        Check(audit, "SundayOrHoliday").Severity.Should().Be(PracticeAuditSeverityDto.Watch);
        Check(audit, "LateValidation").Examples.Should().Equal("VE 3");
    }

    [Fact]
    public void La_caisse_negative_et_les_especes_au_dela_du_plafond_sont_des_alertes()
    {
        PracticeAuditResponse audit = Audit(
            Row("CA", "1", "20260105", "530000", 50m, 0m, piece: "C1", label: "Apport"),
            Row("CA", "1", "20260105", "108000", 0m, 50m, piece: "C1", label: "Apport"),
            Row("CA", "2", "20260106", "607000", 1_200m, 0m, piece: "C2", label: "Achat comptant"),
            Row("CA", "2", "20260106", "530000", 0m, 1_200m, piece: "C2", label: "Achat comptant"));

        PracticeAuditCheckResponse cash = Check(audit, "NegativeCash");
        cash.Severity.Should().Be(PracticeAuditSeverityDto.Alert);
        cash.Examples.Should().Equal("530000 · 2026-01-06 · -1150.00");
        cash.Value.Should().Be(-1_150m);
        Check(audit, "CashOverLimit").Examples.Should().Equal("CA 2 · 1200.00");
        Check(audit, "CashOverLimit").Value.Should().Be(1_200m);
    }

    [Fact]
    public void Des_montants_ronds_frequents_et_les_lignes_en_double_sont_a_surveiller()
    {
        PracticeAuditResponse audit = Audit(Sale("1", net: 1_000m), Sale("2", net: 2_000m), Sale("3", net: 2_500m), Sale("4", net: 1_234.56m), Sale("5", label: "Doublon"), Sale("6", label: "Doublon"));

        PracticeAuditCheckResponse round = Check(audit, "RoundAmounts");
        round.Severity.Should().Be(PracticeAuditSeverityDto.Watch);
        round.Count.Should().Be(3);
        round.Value.Should().Be(75m);
        Check(audit, "DuplicateLines").Examples.Should().Contain("VE 5 / VE 6");
    }

    [Fact]
    public void Les_comptes_de_tiers_du_mauvais_cote_et_un_taux_de_tva_trop_haut_sont_releves()
    {
        PracticeAuditResponse audit = Audit(
            Row("VE", "1", "20260115", "411000", 0m, 130m, "C9", label: "Trop-perçu"),
            Row("VE", "1", "20260115", "706000", 0m, 100m, label: "Vente"),
            Row("VE", "1", "20260115", "445710", 0m, 30m, label: "Vente"),
            Row("VE", "1", "20260115", "512000", 260m, 0m, label: "Vente"),
            Row("AC", "1", "20260115", "401000", 40m, 0m, "F1", "ACH-1", "Avance fournisseur"),
            Row("AC", "1", "20260115", "512000", 0m, 40m, piece: "ACH-1", label: "Avance fournisseur"));

        Check(audit, "CustomerCredit").Examples.Should().Equal("C9 · 130.00");
        Check(audit, "SupplierDebit").Examples.Should().Equal("F1 · 40.00");
        PracticeAuditCheckResponse vat = Check(audit, "VatRate");
        vat.Severity.Should().Be(PracticeAuditSeverityDto.Alert);
        vat.Value.Should().Be(30m);
    }

    [Fact]
    public void La_loi_de_benford_reconnait_des_montants_reels_et_signale_une_serie_fabriquee()
    {
        // Montants répartis uniformément en logarithme, de 10 à 10 000 € : la loi de Benford à l'état pur.
        string[] natural = [.. Enumerable.Range(0, 300).Select(i => Sale((i + 1).ToString(CultureInfo.InvariantCulture), net: Math.Round(10m * (decimal)Math.Pow(1000, i / 300d), 2)))];
        PracticeAuditResponse real = Audit(natural);

        Check(real, "Benford").Severity.Should().Be(PracticeAuditSeverityDto.Passed, $"écart {Check(real, "Benford").Value}");
        real.Benford.Should().HaveCount(9);
        real.Benford[0].Expected.Should().Be(30.1m);

        string[] invented = [.. Enumerable.Range(0, 100).Select(i => Sale((i + 1).ToString(CultureInfo.InvariantCulture), net: 500m + i))];
        PracticeAuditCheckResponse benford = Check(Audit(invented), "Benford");
        benford.Severity.Should().Be(PracticeAuditSeverityDto.Watch);
        benford.Examples.Should().HaveCount(3);
    }

    [Fact]
    public async Task Le_controle_d_un_fichier_remis_refuse_une_periode_inversee()
    {
        RunPracticeAuditHandler handler = new();

        Result<PracticeAuditResponse> refused = await handler.HandleAsync(
            new RunPracticeAuditQuery(DossierId.From(Guid.CreateVersion7()), FileName, Encoding.UTF8.GetBytes(Header), new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 1)), default);
        Result<PracticeAuditResponse> run = await handler.HandleAsync(new RunPracticeAuditQuery(DossierId.From(Guid.CreateVersion7()), FileName, Encoding.UTF8.GetBytes(Header + "\n" + Sale("1"))), default);

        refused.IsFailure.Should().BeTrue();
        refused.Errors.Should().NotBeEmpty();
        run.Value.Entries.Should().Be(1);
    }

    [Fact]
    public async Task Un_fichier_trop_gros_est_refuse_avant_lecture()
    {
        Result<PracticeAuditResponse> refused = await new RunPracticeAuditHandler()
            .HandleAsync(new RunPracticeAuditQuery(DossierId.From(Guid.CreateVersion7()), FileName, new byte[ValidateFecQuery.MaxContentLength + 1]), default);

        refused.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Fec.FileTooLarge);
    }
}
