using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.Integrity;

public sealed class ChainVerificationTests
{
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());
    private const int MaxEntries = IntegrityVerification.MaxFindings + 10;
    private static readonly DateTimeOffset At = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Sceau factice : le domaine ne connaît que le port, la vraie clé est dans l'infrastructure.</summary>
    private sealed class FakeKeys : IIntegrityKeyVerifier
    {
        public static string Seal(string hash) => "seal:" + hash;

        public bool VerifySeal(string entryHash, string seal) => seal == Seal(entryHash);

        public bool VerifyClosingSignature(string keyId, string publicKey, string closingHash, string signature) => true;
    }

    private static DocumentLifecycleEvent Invoice(long number, decimal ttc = 120m) => new(
        JournalEntryKind.InvoiceValidated, JournaledDocumentKind.Invoice, Guid.CreateVersion7(), "FA", $"FA-2026-{number:D5}",
        new DateOnly(2026, 10, 15), Guid.CreateVersion7(), ttc / 1.2m, ttc - (ttc / 1.2m), ttc, "EUR", new string('a', 64));

    private static List<JournalEntry> Chain(int count)
    {
        List<JournalEntry> entries = [];
        string previous = JournalEntry.GenesisHash(Dossier);
        for (int i = 1; i <= count; i++)
        {
            JournalEntry entry = JournalEntry.ForDocument(Invoice(i), i, previous, At.AddMinutes(i)).Value;
            entry.ApplySeal(FakeKeys.Seal(entry.Hash));
            entries.Add(entry);
            previous = entry.Hash;
        }

        return entries;
    }

    /// <summary>Ce qu'un UPDATE direct en base produit : mêmes colonnes de chaîne, contenu changé.</summary>
    private static JournalEntry Tampered(JournalEntry e, decimal? ttc = null, long? sequence = null, (string, string, string)? chain = null) =>
        JournalEntry.Restore(sequence ?? e.Sequence, e.OccurredAt, e.Kind, e.DocumentKind, e.DocumentId, e.SeriesCode, e.Number, e.IssueDate,
            e.CustomerId, (e.TotalExclTax, e.TotalVat, ttc ?? e.TotalInclTax), e.Currency, e.ContentHash, e.RelatedDocumentId,
            chain ?? (e.PreviousHash, e.Hash, e.Seal));

    private static IntegrityReport Verify(IEnumerable<JournalEntry> entries, long nextNumber, IReadOnlyDictionary<Guid, string>? documents = null)
    {
        IntegrityVerification verification = new(Dossier, [], new FakeKeys());
        foreach (JournalEntry entry in entries)
        {
            verification.Check(entry);
        }

        return verification.Complete(new Dictionary<string, long> { ["FA"] = nextNumber }, documents ?? new Dictionary<Guid, string>(),
            documentsAvailable: documents is not null, At.AddDays(1));
    }

    [Fact]
    public void Une_chaine_intacte_ne_donne_aucun_constat()
    {
        IntegrityReport report = Verify(Chain(3), nextNumber: 4);

        report.IsIntact.Should().BeTrue();
        report.EntriesChecked.Should().Be(3);
    }

    [Fact]
    public void Un_montant_modifie_en_base_est_detecte_sur_son_entree()
    {
        List<JournalEntry> entries = Chain(3);
        entries[1] = Tampered(entries[1], ttc: 12m);

        IntegrityReport report = Verify(entries, nextNumber: 4);

        report.Findings.Should().ContainSingle().Which.Should().Be(new IntegrityFinding(IntegrityFindingKind.EntryAltered, 2));
        report.ChainCompromised.Should().BeTrue();
    }

    [Fact]
    public void Une_chaine_recalculee_sans_la_cle_du_cabinet_a_un_sceau_faux()
    {
        List<JournalEntry> entries = Chain(1);
        JournalEntry recomputed = Tampered(entries[0], ttc: 12m);
        entries[0] = Tampered(recomputed, chain: (recomputed.PreviousHash, recomputed.ComputeHash(), entries[0].Seal));

        Verify(entries, nextNumber: 2).Findings.Should().ContainSingle()
            .Which.Kind.Should().Be(IntegrityFindingKind.SealInvalid);
    }

    [Fact]
    public void Une_entree_supprimee_casse_la_suite_la_chaine_et_la_numerotation()
    {
        List<JournalEntry> entries = Chain(3);
        entries.RemoveAt(1);

        IntegrityReport report = Verify(entries, nextNumber: 4);

        report.Findings.Select(f => f.Kind).Should().BeEquivalentTo(
            [IntegrityFindingKind.SequenceGap, IntegrityFindingKind.ChainBroken, IntegrityFindingKind.NumberingGap]);
    }

    [Fact]
    public void Un_numero_consomme_sans_piece_est_un_trou_de_numerotation_seul()
    {
        IntegrityReport report = Verify(Chain(2), nextNumber: 4);

        report.Findings.Should().ContainSingle().Which.Should().Be(new IntegrityFinding(IntegrityFindingKind.NumberingGap, SeriesCode: "FA"));
        report.ChainCompromised.Should().BeFalse();
        report.HasNumberingGap.Should().BeTrue();
    }

    [Fact]
    public void Une_piece_modifiee_ou_supprimee_apres_validation_est_detectee()
    {
        List<JournalEntry> entries = Chain(2);
        Dictionary<Guid, string> current = new() { [entries[0].DocumentId!.Value] = new string('b', 64) };

        IntegrityReport report = Verify(entries, nextNumber: 3, current);

        report.Findings.Select(f => f.Kind).Should().BeEquivalentTo([IntegrityFindingKind.DocumentAltered, IntegrityFindingKind.DocumentMissing]);
        report.DocumentsChecked.Should().Be(2);
    }

    [Fact]
    public void Sans_module_des_pieces_les_pieces_ne_sont_pas_comparees() =>
        Verify(Chain(2), nextNumber: 3).DocumentsChecked.Should().Be(0);

    [Fact]
    public void Une_base_saccagee_ne_liste_pas_des_millions_de_constats()
    {
        List<JournalEntry> entries = [.. Chain(MaxEntries).Select(e => Tampered(e, ttc: 1m))];

        IntegrityReport report = Verify(entries, nextNumber: MaxEntries + 1);

        report.Findings.Should().HaveCount(IntegrityVerification.MaxFindings);
        report.FindingsTruncated.Should().BeTrue();
    }


    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void Un_evenement_sans_empreinte_de_contenu_est_refuse(string contentHash)
    {
        Result<JournalEntry> entry = JournalEntry.ForDocument(Invoice(1) with { ContentHash = contentHash }, 1, JournalEntry.GenesisHash(Dossier), At);

        entry.Errors.Should().ContainSingle().Which.Should().Be(IntegrityErrors.EventInvalid);
    }

    [Fact]
    public void La_premiere_entree_part_d_une_empreinte_propre_au_dossier() =>
        JournalEntry.GenesisHash(Dossier).Should().NotBe(JournalEntry.GenesisHash(new DossierId(Guid.CreateVersion7()))).And.HaveLength(64);
}
