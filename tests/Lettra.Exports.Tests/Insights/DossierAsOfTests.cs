using Lettra.Application.Abstractions.Integrity;
using Lettra.Contracts.Insights;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Insights;

namespace Lettra.Exports.Tests.Insights;

/// <summary>
/// Le dossier qui remonte le temps : le rejeu du journal jusqu'à un instant donne le chiffre d'affaires de l'année, les
/// factures dues et les soldes des clients de ce moment-là ; ce qui vient après est seulement compté.
/// </summary>
public sealed class DossierAsOfTests
{
    private static readonly Guid Dupont = Guid.CreateVersion7();
    private static readonly Guid Martin = Guid.CreateVersion7();
    private static readonly DateTimeOffset Moment = new(2026, 12, 31, 18, 0, 0, TimeSpan.FromHours(1));

    private readonly List<JournalEntry> _entries = [];

    private void Append(
        JournalEntryKind kind, JournaledDocumentKind document, Guid id, string number, DateOnly issued, Guid customer, (decimal Ht, decimal Vat, decimal Ttc) totals, DateTimeOffset at, Guid? related = null)
    {
        string previous = _entries.Count == 0 ? new string('0', 64) : _entries[^1].Hash;
        _entries.Add(JournalEntry.ForDocument(
            new DocumentLifecycleEvent(kind, document, id, number.Split('-')[0], number, issued, customer, totals.Ht, totals.Vat, totals.Ttc, "EUR", new string('a', 64), related),
            _entries.Count + 1,
            previous,
            at).Value);
    }

    private Guid Invoice(string number, DateOnly issued, Guid customer, decimal ht, DateTimeOffset at, JournaledDocumentKind kind = JournaledDocumentKind.Invoice)
    {
        Guid id = Guid.CreateVersion7();
        Append(JournalEntryKind.InvoiceValidated, kind, id, number, issued, customer, (ht, ht * 0.2m, ht * 1.2m), at);
        return id;
    }

    private Guid Payment(string number, Guid customer, decimal amount, DateTimeOffset at)
    {
        Guid id = Guid.CreateVersion7();
        Append(JournalEntryKind.PaymentRecorded, JournaledDocumentKind.Payment, id, number, DateOnly.FromDateTime(at.UtcDateTime), customer, (0m, 0m, amount), at);
        return id;
    }

    private static DossierPresent Present(
        Dictionary<Guid, IReadOnlyList<PaymentShare>>? allocations = null, Dictionary<Guid, decimal>? now = null) =>
        new(new Dictionary<Guid, string> { [Dupont] = "Atelier Dupont", [Martin] = "Martin SARL" }, allocations ?? [], now ?? []);

    private DossierAsOfResponse Replay(DateTimeOffset at, DossierPresent present)
    {
        DossierReplay replay = new(at);
        foreach (JournalEntry entry in _entries.Where(e => e.OccurredAt <= at))
        {
            replay.Add(entry);
        }

        return replay.Complete(present, _entries.Count(e => e.OccurredAt > at), chainIntact: true, _entries.FirstOrDefault()?.OccurredAt, lastClosing: null);
    }

    [Fact]
    public void Le_chiffre_d_affaires_est_celui_de_l_annee_de_l_instant_avoirs_deduits_mois_par_mois()
    {
        Invoice("FA-2025-0099", new DateOnly(2025, 12, 20), Dupont, 1_000m, new DateTimeOffset(2025, 12, 20, 10, 0, 0, TimeSpan.Zero));
        Guid march = Invoice("FA-2026-0001", new DateOnly(2026, 3, 10), Dupont, 500m, new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero));
        Invoice("FA-2026-0002", new DateOnly(2026, 12, 2), Martin, 200m, new DateTimeOffset(2026, 12, 2, 10, 0, 0, TimeSpan.Zero));
        Append(JournalEntryKind.CreditNoteValidated, JournaledDocumentKind.CreditNote, Guid.CreateVersion7(), "AV-2026-0001", new DateOnly(2026, 3, 15), Dupont, (100m, 20m, 120m),
            new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero), march);

        DossierAsOfResponse view = Replay(Moment, Present());

        view.LegalDate.Should().Be(new DateOnly(2026, 12, 31));
        view.Year.Should().Be(new DossierAsOfTotalsResponse(600m, 120m, 720m));
        view.Months.Should().HaveCount(12);
        view.Months[2].Should().Be(new DossierAsOfMonthResponse(3, 400m, 80m, 480m, 2));
        view.Months[11].ExclTax.Should().Be(200m);
        view.Counts.Should().Be(new DossierAsOfCountsResponse(0, 3, 1, 0, 0));
        view.OpenDocuments.Select(d => d.Number).Should().Equal(["FA-2025-0099", "FA-2026-0001", "FA-2026-0002"]);
        view.OpenDocuments[1].OutstandingInclTax.Should().Be(480m, "l'avoir vient en déduction de la facture qu'il corrige");
    }

    [Fact]
    public void Ce_qui_arrive_apres_l_instant_n_y_est_pas_et_est_compte()
    {
        Invoice("FA-2026-0001", new DateOnly(2026, 12, 30), Dupont, 100m, new DateTimeOffset(2026, 12, 30, 9, 0, 0, TimeSpan.Zero));
        Invoice("FA-2026-0002", new DateOnly(2026, 12, 31), Dupont, 50m, Moment.AddMinutes(1));

        DossierAsOfResponse view = Replay(Moment, Present());

        view.EntriesReplayed.Should().Be(1);
        view.EntriesAfter.Should().Be(1);
        view.OpenDocuments.Should().ContainSingle().Which.Number.Should().Be("FA-2026-0001");
        view.ReceivablesInclTax.Should().Be(120m);
    }

    [Fact]
    public void Un_encaissement_reduit_la_facture_qu_il_paie_et_le_solde_du_client_meme_sans_imputation()
    {
        DateTimeOffset day = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
        Guid invoice = Invoice("FA-2026-0001", new DateOnly(2026, 12, 1), Dupont, 1_000m, day);
        Guid allocated = Payment("ENC-2026-0001", Dupont, 200m, day.AddDays(1));
        Guid free = Payment("ENC-2026-0002", Dupont, 300m, day.AddDays(2));
        DossierPresent present = Present(
            new() { [allocated] = [new PaymentShare(invoice, 200m)], [free] = [] },
            new() { [invoice] = 0m });

        DossierAsOfResponse view = Replay(Moment, present);

        view.Counts.Payments.Should().Be(2);
        DossierAsOfDocumentResponse open = view.OpenDocuments.Should().ContainSingle().Subject;
        open.OutstandingInclTax.Should().Be(1_000m, "seule la part imputée réduit la facture");
        open.OutstandingNowInclTax.Should().Be(0m);
        view.Customers.Should().ContainSingle().Which.Should().Be(new DossierAsOfCustomerResponse(Dupont, "Atelier Dupont", 1_200m, 0m, 500m, 700m, 0m));
        view.ReceivablesInclTax.Should().Be(700m);
    }

    [Fact]
    public void Un_encaissement_contre_passe_avant_l_instant_ne_compte_plus_mais_compte_encore_avant_la_contre_passation()
    {
        DateTimeOffset day = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
        Guid invoice = Invoice("FA-2026-0001", new DateOnly(2026, 12, 1), Dupont, 100m, day);
        Guid payment = Payment("ENC-2026-0001", Dupont, 120m, day.AddDays(1));
        Append(JournalEntryKind.PaymentReversed, JournaledDocumentKind.Payment, payment, "ENC-2026-0001", new DateOnly(2026, 12, 3), Dupont, (0m, 0m, 120m), day.AddDays(2));
        DossierPresent present = Present(new() { [payment] = [new PaymentShare(invoice, 120m)] }, new() { [invoice] = 120m });

        DossierAsOfResponse paid = Replay(day.AddDays(1).AddHours(1), present);
        DossierAsOfResponse reversed = Replay(Moment, present);

        paid.OpenDocuments.Should().BeEmpty();
        paid.Customers.Should().ContainSingle().Which.Should().Be(new DossierAsOfCustomerResponse(Dupont, "Atelier Dupont", 120m, 0m, 120m, 0m, 120m));
        reversed.OpenDocuments.Should().ContainSingle().Which.OutstandingInclTax.Should().Be(120m);
        reversed.Counts.Payments.Should().Be(0);
        reversed.LastEvents.Select(e => e.Kind).Should().Equal(
            [DossierAsOfEventKindDto.PaymentReversed, DossierAsOfEventKindDto.PaymentRecorded, DossierAsOfEventKindDto.InvoiceValidated]);
    }

    [Fact]
    public void Un_acompte_est_dit_comme_tel_et_les_clients_soldes_partout_sont_omis()
    {
        DateTimeOffset day = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
        Guid deposit = Invoice("FA-2026-0001", new DateOnly(2026, 11, 2), Dupont, 100m, day, JournaledDocumentKind.DepositInvoice);
        Guid settled = Invoice("FA-2026-0002", new DateOnly(2026, 11, 3), Martin, 100m, day.AddDays(1));
        Guid payment = Payment("ENC-2026-0001", Martin, 120m, day.AddDays(2));

        DossierAsOfResponse view = Replay(Moment, Present(new() { [payment] = [new PaymentShare(settled, 120m)] }, new() { [deposit] = 120m, [settled] = 0m }));

        view.OpenDocuments.Should().ContainSingle().Which.Kind.Should().Be(DossierAsOfDocumentKindDto.DownPaymentInvoice);
        view.Customers.Should().ContainSingle().Which.CustomerId.Should().Be(Dupont);
        view.ReceivablesNowInclTax.Should().Be(120m);
    }

    [Fact]
    public void Les_derniers_evenements_sont_les_plus_recents_d_abord_et_en_nombre_limite()
    {
        DateTimeOffset day = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
        for (int i = 1; i <= DossierAsOfResponse.LastEventCount + 3; i++)
        {
            Append(JournalEntryKind.QuoteValidated, JournaledDocumentKind.Quote, Guid.CreateVersion7(), $"DE-2026-{i:D4}", new DateOnly(2026, 1, 5), Dupont, (10m, 2m, 12m), day.AddMinutes(i));
        }

        DossierAsOfResponse view = Replay(Moment, Present());

        view.Counts.Quotes.Should().Be(DossierAsOfResponse.LastEventCount + 3);
        view.LastEvents.Should().HaveCount(DossierAsOfResponse.LastEventCount);
        view.LastEvents[0].Number.Should().Be($"DE-2026-{DossierAsOfResponse.LastEventCount + 3:D4}");
        view.LastEvents[0].AmountInclTax.Should().Be(12m);
        view.Year.ExclTax.Should().Be(0m, "un devis n'est pas du chiffre d'affaires");
        view.OpenDocuments.Should().BeEmpty();
    }

    [Fact]
    public async Task Le_rejeu_verifie_la_chaine_et_la_dit_rompue_sans_cle_pour_la_verifier()
    {
        DossierId dossier = DossierId.From(Guid.CreateVersion7());
        Invoice("FA-2026-0001", new DateOnly(2026, 12, 1), Dupont, 100m, new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
        Invoice("FA-2026-0002", new DateOnly(2026, 12, 31), Dupont, 100m, Moment.AddHours(1));

        Result<DossierAsOfResponse> withoutKey = await new GetDossierAsOfHandler(new Store(_entries), new Keyring(), new Reader())
            .HandleAsync(new GetDossierAsOfQuery(dossier, Moment), default);
        Result<DossierAsOfResponse> beforeAnything = await new GetDossierAsOfHandler(new Store(_entries), new Keyring(), new Reader())
            .HandleAsync(new GetDossierAsOfQuery(dossier, Moment.AddYears(-1)), default);

        withoutKey.Value.ChainIntact.Should().BeFalse();
        withoutKey.Value.EntriesReplayed.Should().Be(1);
        withoutKey.Value.EntriesAfter.Should().Be(1);
        withoutKey.Value.FirstEntryAt.Should().Be(_entries[0].OccurredAt);
        beforeAnything.Value.ChainIntact.Should().BeTrue("rien n'est rejoué, rien n'est à vérifier");
        beforeAnything.Value.Customers.Should().BeEmpty();
    }

    private sealed class Store(IReadOnlyList<JournalEntry> entries) : IIntegrityStore
    {
        public Task<IReadOnlyList<PeriodClosing>> ListClosingsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PeriodClosing>>([]);

        public async IAsyncEnumerable<JournalEntry> ReadEntriesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (JournalEntry entry in entries)
            {
                await Task.Yield();
                yield return entry;
            }
        }

        public Task<DateTimeOffset?> FirstEntryAtAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, long>> NextNumbersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<PeriodClosing>> CloseAsync(ClosingPeriod period, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<IntegrityKeyVersion>> RotateKeysAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Keyring : IIntegrityKeyring
    {
        public Task<Result<IIntegrityKeyVerifier>> VerifierAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result<IIntegrityKeyVerifier>.Failure(IntegrityErrors.KeyUnavailable));

        public Task<IReadOnlyList<IntegrityKeyVersion>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Reader : IDossierPresentReader
    {
        public Task<DossierPresent> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(DossierPresent.Empty);
    }
}
