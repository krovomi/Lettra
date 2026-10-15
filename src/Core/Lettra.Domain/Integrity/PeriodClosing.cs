using System.Globalization;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Integrity;

/// <summary>Périodicité d'une clôture. Ajout seulement.</summary>
public enum ClosingPeriodKind
{
    Month = 1,
    Year = 2,
}

/// <summary>
/// Clôture périodique signée d'un dossier : elle fige une plage de la chaîne (de <see cref="FirstSequence"/> à
/// <see cref="LastSequence"/>), ses totaux et le total perpétuel, et chaîne les clôtures entre elles. La signature
/// (ECDSA P-256) est faite avec une clé du cabinet qui n'est jamais en base : la clé publique jointe permet à un
/// auditeur de vérifier une archive hors de Lettra, et la vérification compare son identifiant à la clé du cabinet.
/// </summary>
public sealed class PeriodClosing
{
    private PeriodClosing()
    {
    }

    public int Number { get; private set; }

    public ClosingPeriodKind PeriodKind { get; private set; }

    public DateOnly PeriodStart { get; private set; }

    /// <summary>Dernier jour inclus de la période.</summary>
    public DateOnly PeriodEnd { get; private set; }

    public long FirstSequence { get; private set; }

    /// <summary>Dernière entrée couverte ; égale à <c>FirstSequence - 1</c> pour une période sans entrée.</summary>
    public long LastSequence { get; private set; }

    /// <summary>Empreinte de l'entrée <see cref="LastSequence"/> (ou de la dernière entrée connue si la période est vide).</summary>
    public string LastEntryHash { get; private set; } = string.Empty;

    public decimal TotalExclTax { get; private set; }

    public decimal TotalVat { get; private set; }

    public decimal TotalInclTax { get; private set; }

    /// <summary>Total perpétuel TTC depuis l'ouverture du journal.</summary>
    public decimal CumulativeInclTax { get; private set; }

    public string PreviousClosingHash { get; private set; } = string.Empty;

    public DateTimeOffset ClosedAt { get; private set; }

    public string KeyId { get; private set; } = string.Empty;

    /// <summary>Clé publique de signature (SubjectPublicKeyInfo, base 64), pour la vérification hors ligne.</summary>
    public string PublicKey { get; private set; } = string.Empty;

    public string Hash { get; private set; } = string.Empty;

    /// <summary>Signature ECDSA P-256 / SHA-256 de <see cref="Hash"/>, en base 64.</summary>
    public string Signature { get; private set; } = string.Empty;

    public long EntryCount => LastSequence - FirstSequence + 1;

    public static string FormatNumber(int number) => "CLO-" + number.ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>Empreinte de départ de la chaîne des clôtures, propre au dossier.</summary>
    public static string GenesisHash(DossierId dossier) => IntegrityHash.Of("lettra-clotures-v1", IntegrityHash.Field(dossier.Value));

    /// <summary>Prépare la clôture suivante ; elle n'est valable qu'une fois signée (<see cref="ApplySignature"/>).</summary>
    public static PeriodClosing Prepare(
        DossierId dossier,
        PeriodClosing? previous,
        ClosingPeriodKind kind,
        DateOnly start,
        DateOnly end,
        IReadOnlyList<JournalEntry> entries,
        string lastKnownHash,
        DateTimeOffset at,
        string keyId,
        string publicKey)
    {
        ArgumentNullException.ThrowIfNull(entries);
        long first = previous is null ? 1 : previous.LastSequence + 1;
        decimal ht = 0m, vat = 0m, ttc = 0m;
        foreach (JournalEntry entry in entries)
        {
            (decimal eHt, decimal eVat, decimal eTtc) = entry.RevenueContribution();
            ht += eHt;
            vat += eVat;
            ttc += eTtc;
        }

        PeriodClosing closing = new()
        {
            Number = (previous?.Number ?? 0) + 1,
            PeriodKind = kind,
            PeriodStart = start,
            PeriodEnd = end,
            FirstSequence = first,
            LastSequence = first + entries.Count - 1,
            LastEntryHash = entries.Count > 0 ? entries[^1].Hash : lastKnownHash,
            TotalExclTax = ht,
            TotalVat = vat,
            TotalInclTax = ttc,
            CumulativeInclTax = (previous?.CumulativeInclTax ?? 0m) + ttc,
            PreviousClosingHash = previous?.Hash ?? GenesisHash(dossier),
            ClosedAt = IntegrityHash.Truncate(at),
            KeyId = keyId,
            PublicKey = publicKey,
        };
        closing.Hash = closing.ComputeHash(dossier);
        return closing;
    }

    /// <summary>Champs d'une clôture lue (archive scellée), sans recalcul.</summary>
    public sealed record Snapshot(
        int Number,
        ClosingPeriodKind PeriodKind,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        long FirstSequence,
        long LastSequence,
        string LastEntryHash,
        decimal TotalExclTax,
        decimal TotalVat,
        decimal TotalInclTax,
        decimal CumulativeInclTax,
        string PreviousClosingHash,
        DateTimeOffset ClosedAt,
        string KeyId,
        string PublicKey,
        string Hash,
        string Signature);

    public static PeriodClosing Restore(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new PeriodClosing
        {
            Number = snapshot.Number,
            PeriodKind = snapshot.PeriodKind,
            PeriodStart = snapshot.PeriodStart,
            PeriodEnd = snapshot.PeriodEnd,
            FirstSequence = snapshot.FirstSequence,
            LastSequence = snapshot.LastSequence,
            LastEntryHash = snapshot.LastEntryHash,
            TotalExclTax = snapshot.TotalExclTax,
            TotalVat = snapshot.TotalVat,
            TotalInclTax = snapshot.TotalInclTax,
            CumulativeInclTax = snapshot.CumulativeInclTax,
            PreviousClosingHash = snapshot.PreviousClosingHash,
            ClosedAt = snapshot.ClosedAt,
            KeyId = snapshot.KeyId,
            PublicKey = snapshot.PublicKey,
            Hash = snapshot.Hash,
            Signature = snapshot.Signature,
        };
    }

    public Snapshot ToSnapshot() => new(
        Number, PeriodKind, PeriodStart, PeriodEnd, FirstSequence, LastSequence, LastEntryHash, TotalExclTax, TotalVat, TotalInclTax,
        CumulativeInclTax, PreviousClosingHash, ClosedAt, KeyId, PublicKey, Hash, Signature);

    public string ComputeHash(DossierId dossier) => IntegrityHash.Of(
        "closing-v1",
        IntegrityHash.Field(dossier.Value),
        IntegrityHash.Field(Number),
        IntegrityHash.Field((int)PeriodKind),
        IntegrityHash.Field(PeriodStart),
        IntegrityHash.Field(PeriodEnd),
        IntegrityHash.Field(FirstSequence),
        IntegrityHash.Field(LastSequence),
        LastEntryHash,
        IntegrityHash.Field(TotalExclTax),
        IntegrityHash.Field(TotalVat),
        IntegrityHash.Field(TotalInclTax),
        IntegrityHash.Field(CumulativeInclTax),
        PreviousClosingHash,
        IntegrityHash.Field(ClosedAt),
        KeyId,
        PublicKey);

    public void ApplySignature(string signature)
    {
        ArgumentException.ThrowIfNullOrEmpty(signature);
        Signature = signature;
    }
}
