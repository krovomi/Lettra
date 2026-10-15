using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.Integrity;

public sealed class ChainVerificationTests
{
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());
    private const int MaxEntries = IntegrityVerification.MaxFindings + 10;
    private static readonly DateTimeOffset At = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private const string K1 = "k1";
    private const string K2 = "k2";

    /// <summary>Registre factice : le domaine ne connaît que le port, les vraies clés sont dans l'infrastructure.</summary>
    private sealed class FakeKeys(params string[] registry) : IIntegrityKeyVerifier
    {
        private readonly string[] _registry = registry.Length == 0 ? [K1] : registry;

        public string? InitialKeyId => _registry[0];

        public string? CurrentKeyId => _registry[^1];

        public static string Seal(string keyId, string hash) => $"seal:{keyId}:{hash}";

        public static string PublicKey(string keyId) => "pub:" + keyId;

        public string? PublicKeyOf(string keyId) => _registry.Contains(keyId) ? PublicKey(keyId) : null;

        public bool VerifySeal(string keyId, string entryHash, string seal) => _registry.Contains(keyId) && seal == Seal(keyId, entryHash);

        public bool VerifyClosingSignature(string keyId, string publicKey, string closingHash, string signature) => true;
    }

    private static DocumentLifecycleEvent Invoice(long number, decimal ttc = 120m) => new(
        JournalEntryKind.InvoiceValidated, JournaledDocumentKind.Invoice, Guid.CreateVersion7(), "FA", $"FA-2026-{number:D5}",
        new DateOnly(2026, 10, 15), Guid.CreateVersion7(), ttc / 1.2m, ttc - (ttc / 1.2m), ttc, "EUR", new string('a', 64));

    private static List<JournalEntry> Chain(int count)
    {
        List<JournalEntry> entries = [];
        for (int i = 1; i <= count; i++)
        {
            Append(entries, K1, (sequence, previous) => JournalEntry.ForDocument(Invoice(i), sequence, previous, At.AddMinutes(sequence)).Value);
        }

        return entries;
    }

    private static JournalEntry Append(List<JournalEntry> entries, string sealedWith, Func<long, string, JournalEntry> build)
    {
        JournalEntry entry = build(entries.Count + 1, entries.Count == 0 ? JournalEntry.GenesisHash(Dossier) : entries[^1].Hash);
        entry.ApplySeal(sealedWith, FakeKeys.Seal(sealedWith, entry.Hash));
        entries.Add(entry);
        return entry;
    }

    /// <summary>Deux factures sous K1, la rotation vers K2 (scellée par K1), puis deux factures sous K2.</summary>
    private static List<JournalEntry> RotatedChain(string announcedPublicKey = "pub:k2")
    {
        List<JournalEntry> entries = Chain(2);
        Append(entries, K1, (sequence, previous) => JournalEntry.ForKeyRotation(K2, announcedPublicKey, sequence, previous, At.AddHours(1)));
        Append(entries, K2, (sequence, previous) => JournalEntry.ForDocument(Invoice(3), sequence, previous, At.AddHours(2)).Value);
        Append(entries, K2, (sequence, previous) => JournalEntry.ForDocument(Invoice(4), sequence, previous, At.AddHours(3)).Value);
        return entries;
    }

    /// <summary>Ce qu'un UPDATE direct en base produit : mêmes colonnes de chaîne, contenu changé.</summary>
    private static JournalEntry Tampered(JournalEntry e, decimal? ttc = null, long? sequence = null, (string, string, string)? chain = null) =>
        JournalEntry.Restore(sequence ?? e.Sequence, e.OccurredAt, e.Kind, e.DocumentKind, e.DocumentId, e.SeriesCode, e.Number, e.IssueDate,
            e.CustomerId, (e.TotalExclTax, e.TotalVat, ttc ?? e.TotalInclTax), e.Currency, e.ContentHash, e.RelatedDocumentId,
            chain ?? (e.PreviousHash, e.Hash, e.Seal), e.KeyId);

    private static IntegrityReport Verify(
        IEnumerable<JournalEntry> entries, long nextNumber, IReadOnlyDictionary<Guid, string>? documents = null, FakeKeys? keys = null)
    {
        IntegrityVerification verification = new(Dossier, [], keys ?? new FakeKeys());
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


    [Fact]
    public void Apres_une_rotation_tout_l_historique_se_verifie_avec_les_cles_retirees() =>
        Verify(RotatedChain(), nextNumber: 5, keys: new FakeKeys(K1, K2)).IsIntact.Should().BeTrue();

    [Fact]
    public void Les_entrees_d_avant_le_versionnage_relevent_de_la_premiere_cle()
    {
        List<JournalEntry> legacy = [.. Chain(2).Select(e => JournalEntry.Restore(e.Sequence, e.OccurredAt, e.Kind, e.DocumentKind, e.DocumentId,
            e.SeriesCode, e.Number, e.IssueDate, e.CustomerId, (e.TotalExclTax, e.TotalVat, e.TotalInclTax), e.Currency, e.ContentHash,
            e.RelatedDocumentId, (e.PreviousHash, e.Hash, e.Seal), keyId: null))];

        Verify(legacy, nextNumber: 3).IsIntact.Should().BeTrue();
    }

    [Fact]
    public void Une_cle_retiree_reutilisee_apres_la_rotation_est_signalee()
    {
        List<JournalEntry> entries = RotatedChain();
        JournalEntry last = entries[^1];
        last.ApplySeal(K1, FakeKeys.Seal(K1, last.Hash));

        Verify(entries, nextNumber: 5, keys: new FakeKeys(K1, K2)).Findings.Should().ContainSingle()
            .Which.Should().Be(new IntegrityFinding(IntegrityFindingKind.KeyInvalid, 5));
    }

    [Fact]
    public void Une_rotation_vers_une_cle_absente_du_registre_ou_differente_est_signalee()
    {
        Verify(RotatedChain(announcedPublicKey: "pub:autre"), nextNumber: 5, keys: new FakeKeys(K1, K2)).Findings
            .Should().ContainSingle().Which.Should().Be(new IntegrityFinding(IntegrityFindingKind.KeyInvalid, 3));

        // K2 retirée du registre : l'annonce et les entrées qu'elle aurait scellées tombent.
        Verify(RotatedChain(), nextNumber: 5, keys: new FakeKeys(K1)).Findings.Select(f => f.Kind).Should().BeEquivalentTo(
            [IntegrityFindingKind.KeyInvalid, IntegrityFindingKind.SealInvalid, IntegrityFindingKind.SealInvalid, IntegrityFindingKind.KeyInvalid]);
    }

    [Fact]
    public void Une_version_ajoutee_au_registre_sans_annonce_dans_la_chaine_est_signalee() =>
        Verify(Chain(2), nextNumber: 3, keys: new FakeKeys(K1, K2)).Findings.Should().ContainSingle()
            .Which.Should().Be(new IntegrityFinding(IntegrityFindingKind.KeyInvalid));

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
