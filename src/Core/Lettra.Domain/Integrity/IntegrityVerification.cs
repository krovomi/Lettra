using System.Globalization;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Integrity;

/// <summary>Ce que le contrôle d'intégrité peut constater. Ajout seulement (stocké dans les alertes).</summary>
public enum IntegrityFindingKind
{
    /// <summary>Une entrée manque ou est en trop : la suite des numéros d'entrée n'est pas continue.</summary>
    SequenceGap = 1,

    /// <summary>Le contenu d'une entrée ne correspond plus à son empreinte : ligne modifiée en base.</summary>
    EntryAltered = 2,

    /// <summary>Une entrée ne pointe pas vers l'empreinte de la précédente : insertion, suppression ou réordonnancement.</summary>
    ChainBroken = 3,

    /// <summary>Le sceau d'une entrée est faux : la chaîne a été recalculée sans la clé du cabinet.</summary>
    SealInvalid = 4,

    /// <summary>Une clôture ne correspond plus à la chaîne (plage, totaux, empreinte) ou a disparu.</summary>
    ClosingMismatch = 5,

    /// <summary>La signature d'une clôture est fausse, ou faite avec une clé qui n'est pas celle du cabinet.</summary>
    ClosingSignatureInvalid = 6,

    /// <summary>Une pièce en base n'a plus le contenu figé à sa validation.</summary>
    DocumentAltered = 7,

    /// <summary>Une pièce journalisée n'existe plus en base.</summary>
    DocumentMissing = 8,

    /// <summary>Un numéro de série manque dans le journal (numéro consommé sans pièce, ou entrée supprimée).</summary>
    NumberingGap = 9,
}

/// <summary>
/// Un constat, sans aucune donnée métier : seulement des numéros d'entrée, de clôture et le code de série. Il peut
/// donc figurer dans une alerte ou un journal technique.
/// </summary>
public sealed record IntegrityFinding(IntegrityFindingKind Kind, long? Sequence = null, int? ClosingNumber = null, string? SeriesCode = null)
{
    /// <summary>Atteinte à la chaîne (S1) ; un trou de numérotation seul est traité à part (S2).</summary>
    public bool CompromisesChain => Kind != IntegrityFindingKind.NumberingGap;
}

/// <summary>Résultat d'un contrôle d'intégrité.</summary>
public sealed record IntegrityReport(
    DossierId Dossier,
    DateTimeOffset CheckedAt,
    long EntriesChecked,
    int ClosingsChecked,
    int DocumentsChecked,
    IReadOnlyList<IntegrityFinding> Findings,
    bool FindingsTruncated)
{
    public bool IsIntact => Findings.Count == 0;

    public bool ChainCompromised => Findings.Any(f => f.CompromisesChain);

    public bool HasNumberingGap => Findings.Any(f => f.Kind == IntegrityFindingKind.NumberingGap);
}

/// <summary>
/// Vérification des sceaux et des signatures, avec les clés du cabinet du dossier (jamais en base). Le domaine ne
/// connaît pas les clés : seulement ce port.
/// </summary>
public interface IIntegrityKeyVerifier
{
    bool VerifySeal(string entryHash, string seal);

    /// <summary>Vrai si la signature est valable ET faite avec une clé du cabinet (identifiant connu).</summary>
    bool VerifyClosingSignature(string keyId, string publicKey, string closingHash, string signature);
}

/// <summary>
/// Contrôle d'intégrité d'un dossier, en un seul passage sur un journal qui peut être long : on lui donne les
/// clôtures, puis les entrées dans l'ordre, puis l'état des séries et l'empreinte recalculée des pièces.
/// Pur et déterministe ; les accès aux données sont faits par l'appelant.
/// </summary>
public sealed class IntegrityVerification
{
    /// <summary>Au-delà, on arrête de lister les constats (une base saccagée en donnerait des millions).</summary>
    public const int MaxFindings = 200;

    private readonly DossierId _dossier;
    private readonly IIntegrityKeyVerifier _keys;
    private readonly List<IntegrityFinding> _findings = [];
    private readonly List<PeriodClosing> _closingsByLast;
    private int _nextClosing;
    private readonly Dictionary<int, PeriodClosing> _closingsByNumber = [];
    private readonly HashSet<int> _closingsInChain = [];
    private readonly Dictionary<Guid, string> _documentHashes = [];
    private readonly Dictionary<string, HashSet<long>> _numbers = new(StringComparer.Ordinal);
    private long _expectedSequence = 1;
    private string _previousHash;
    private decimal _periodHt, _periodVat, _periodTtc, _cumulative;
    private int _closingsChecked;
    private bool _truncated;

    public IntegrityVerification(DossierId dossier, IReadOnlyCollection<PeriodClosing> closings, IIntegrityKeyVerifier keys)
    {
        ArgumentNullException.ThrowIfNull(closings);
        ArgumentNullException.ThrowIfNull(keys);
        _dossier = dossier;
        _keys = keys;
        _previousHash = JournalEntry.GenesisHash(dossier);

        string previousClosingHash = PeriodClosing.GenesisHash(dossier);
        int expectedNumber = 1;
        foreach (PeriodClosing closing in closings.OrderBy(c => c.Number))
        {
            if (closing.Number != expectedNumber
                || closing.PreviousClosingHash != previousClosingHash
                || closing.ComputeHash(dossier) != closing.Hash)
            {
                Add(new(IntegrityFindingKind.ClosingMismatch, ClosingNumber: closing.Number));
            }

            if (!keys.VerifyClosingSignature(closing.KeyId, closing.PublicKey, closing.Hash, closing.Signature))
            {
                Add(new(IntegrityFindingKind.ClosingSignatureInvalid, ClosingNumber: closing.Number));
            }

            _closingsByNumber[closing.Number] = closing;
            previousClosingHash = closing.Hash;
            expectedNumber = closing.Number + 1;
        }

        _closingsByLast = [.. closings.OrderBy(c => c.LastSequence).ThenBy(c => c.Number)];

        // Une clôture de période vide se vérifie avant toute entrée si elle couvre le tout début du journal.
        CheckClosingsEndingAt(0, _previousHash);
    }

    public long EntriesChecked { get; private set; }

    /// <summary>L'entrée suivante, dans l'ordre des numéros d'entrée.</summary>
    public void Check(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        EntriesChecked++;

        if (entry.Sequence != _expectedSequence)
        {
            Add(new(IntegrityFindingKind.SequenceGap, entry.Sequence));
        }

        if (entry.ComputeHash() != entry.Hash)
        {
            Add(new(IntegrityFindingKind.EntryAltered, entry.Sequence));
        }

        if (entry.PreviousHash != _previousHash)
        {
            Add(new(IntegrityFindingKind.ChainBroken, entry.Sequence));
        }

        if (!_keys.VerifySeal(entry.Hash, entry.Seal))
        {
            Add(new(IntegrityFindingKind.SealInvalid, entry.Sequence));
        }

        if (entry.Kind == JournalEntryKind.PeriodClosed)
        {
            CheckClosingEntry(entry);
        }
        else
        {
            if (entry.DocumentId is Guid document)
            {
                _documentHashes[document] = entry.ContentHash;
            }

            if (entry.AssignsNumber && entry.SeriesCode is { } series && TrailingNumber(entry.Number) is long number)
            {
                if (!_numbers.TryGetValue(series, out HashSet<long>? seen))
                {
                    _numbers[series] = seen = [];
                }

                seen.Add(number);
            }
        }

        (decimal ht, decimal vat, decimal ttc) = entry.RevenueContribution();
        _periodHt += ht;
        _periodVat += vat;
        _periodTtc += ttc;

        _expectedSequence = entry.Sequence + 1;
        _previousHash = entry.Hash;
        CheckClosingsEndingAt(entry.Sequence, entry.Hash);
    }

    /// <summary>Pièces que le journal connaît, avec l'empreinte de leur dernier état journalisé.</summary>
    public IReadOnlyDictionary<Guid, string> JournaledDocuments => _documentHashes;

    /// <summary>
    /// Termine le contrôle : séries (prochain numéro de chaque série) et empreintes des pièces recalculées depuis la base.
    /// Une pièce absente de <paramref name="currentDocumentHashes"/> est réputée supprimée, sauf si
    /// <paramref name="documentsAvailable"/> est faux (le module des pièces n'est pas encore branché).
    /// </summary>
    public IntegrityReport Complete(
        IReadOnlyDictionary<string, long> nextNumberBySeries,
        IReadOnlyDictionary<Guid, string> currentDocumentHashes,
        bool documentsAvailable,
        DateTimeOffset checkedAt)
    {
        ArgumentNullException.ThrowIfNull(nextNumberBySeries);
        ArgumentNullException.ThrowIfNull(currentDocumentHashes);

        // Clôtures qui pointent au-delà de la fin du journal : des entrées ont disparu.
        foreach (PeriodClosing closing in _closingsByLast.Skip(_nextClosing))
        {
            Add(new(IntegrityFindingKind.ClosingMismatch, ClosingNumber: closing.Number));
        }

        // Une clôture qui n'a pas d'entrée dans la chaîne a été insérée en base, ou son entrée supprimée.
        foreach (PeriodClosing closing in _closingsByNumber.Values.Where(c => !_closingsInChain.Contains(c.Number)))
        {
            Add(new(IntegrityFindingKind.ClosingMismatch, ClosingNumber: closing.Number));
        }

        foreach ((string series, long next) in nextNumberBySeries.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            HashSet<long> seen = _numbers.GetValueOrDefault(series) ?? [];
            long expected = next - 1;
            bool continuous = seen.Count == expected && (expected == 0 || (seen.Min() == 1 && seen.Max() == expected));
            if (!continuous)
            {
                Add(new(IntegrityFindingKind.NumberingGap, SeriesCode: series));
            }
        }

        int documentsChecked = 0;
        if (documentsAvailable)
        {
            foreach ((Guid document, string journaled) in _documentHashes)
            {
                documentsChecked++;
                if (!currentDocumentHashes.TryGetValue(document, out string? current))
                {
                    Add(new(IntegrityFindingKind.DocumentMissing));
                }
                else if (!string.Equals(current, journaled, StringComparison.Ordinal))
                {
                    Add(new(IntegrityFindingKind.DocumentAltered));
                }
            }
        }

        return new IntegrityReport(_dossier, checkedAt, EntriesChecked, _closingsChecked, documentsChecked, [.. _findings], _truncated);
    }

    private void CheckClosingEntry(JournalEntry entry)
    {
        if (!TryClosingNumber(entry.Number, out int number) || !_closingsByNumber.TryGetValue(number, out PeriodClosing? closing))
        {
            Add(new(IntegrityFindingKind.ClosingMismatch, entry.Sequence));
            return;
        }

        _closingsInChain.Add(number);
        if (closing.Hash != entry.ContentHash || entry.Sequence <= closing.LastSequence)
        {
            Add(new(IntegrityFindingKind.ClosingMismatch, entry.Sequence, number));
        }
    }

    private void CheckClosingsEndingAt(long sequence, string hash)
    {
        while (_nextClosing < _closingsByLast.Count)
        {
            PeriodClosing closing = _closingsByLast[_nextClosing];
            if (closing.LastSequence > sequence)
            {
                return;
            }

            _nextClosing++;
            _closingsChecked++;
            _cumulative += _periodTtc;
            bool matches = closing.LastSequence == sequence
                && closing.LastEntryHash == hash
                && closing.TotalExclTax == _periodHt
                && closing.TotalVat == _periodVat
                && closing.TotalInclTax == _periodTtc
                && closing.CumulativeInclTax == _cumulative;
            if (!matches)
            {
                Add(new(IntegrityFindingKind.ClosingMismatch, ClosingNumber: closing.Number));
            }

            _periodHt = _periodVat = _periodTtc = 0m;
        }
    }

    private void Add(IntegrityFinding finding)
    {
        if (_findings.Count >= MaxFindings)
        {
            _truncated = true;
            return;
        }

        _findings.Add(finding);
    }

    private static long? TrailingNumber(string? number)
    {
        if (string.IsNullOrEmpty(number))
        {
            return null;
        }

        int start = number.Length;
        while (start > 0 && char.IsAsciiDigit(number[start - 1]))
        {
            start--;
        }

        return start < number.Length && long.TryParse(number.AsSpan(start), NumberStyles.None, CultureInfo.InvariantCulture, out long value)
            ? value
            : null;
    }

    private static bool TryClosingNumber(string? text, out int number)
    {
        number = 0;
        return text is not null && text.StartsWith("CLO-", StringComparison.Ordinal)
            && int.TryParse(text.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}
