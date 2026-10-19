using System.Text;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Banking;

/// <summary>Ce qu'un lettrage rapproche : l'écriture d'une pièce de vente, d'un encaissement ou de sa contre-passation.</summary>
public enum LetteredEntryKind
{
    /// <summary>Écriture de vente d'une facture, d'un acompte ou d'un avoir (journal VE).</summary>
    Invoice = 1,

    /// <summary>Écriture d'un encaissement (journal BQ).</summary>
    Payment = 2,

    /// <summary>Écriture de contre-passation d'un encaissement (journal BQ).</summary>
    PaymentReversal = 3,
}

/// <summary>Une écriture lettrée.</summary>
public sealed record LetteringItem(LetteredEntryKind Kind, Guid DocumentId);

/// <summary>
/// Un lettrage du compte d'un client : un code (A, B, … Z, AA, AB…) posé sur des lignes du 411 dont les débits et les
/// crédits s'annulent. Un lettrage ne se supprime pas : il se défait avec trace (qui, quand, pourquoi) et son code n'est
/// jamais réutilisé pour ce client.
/// </summary>
public sealed class Lettering : AggregateRoot<Guid>
{
    public const int MaxCodeLength = 8;

    private readonly List<LetteringItem> _items = [];

    private Lettering(Guid id)
        : base(id)
    {
        Code = string.Empty;
    }

    public CustomerId CustomerId { get; private set; }

    public string Code { get; private set; }

    /// <summary>Date du lettrage : celle de la dernière écriture du groupe.</summary>
    public DateOnly Date { get; private set; }

    public IReadOnlyList<LetteringItem> Items => _items;

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CancelledBy { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancelReason { get; private set; }

    public bool IsActive => CancelledAt is null;

    public static Lettering Create(Guid id, CustomerId customer, int index, DateOnly date, IEnumerable<LetteringItem> items, Guid userId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(items);
        Lettering lettering = new(id)
        {
            CustomerId = customer,
            Code = LetteringCodes.FromIndex(index),
            Date = date,
            CreatedBy = userId,
            CreatedAt = now,
        };
        lettering._items.AddRange(items.Distinct());
        if (lettering._items.Count < 2)
        {
            throw new ArgumentException("Un lettrage rapproche au moins deux écritures.", nameof(items));
        }

        return lettering;
    }

    public bool Contains(LetteredEntryKind kind, Guid documentId) => _items.Any(i => i.Kind == kind && i.DocumentId == documentId);

    /// <summary>Défait le lettrage (délettrage) : il reste en base, marqué avec son auteur et son motif.</summary>
    public void Cancel(string reason, Guid userId, DateTimeOffset now)
    {
        if (!IsActive)
        {
            return;
        }

        CancelledBy = userId;
        CancelledAt = now;
        CancelReason = reason.Length > Payment.MaxReasonLength ? reason[..Payment.MaxReasonLength] : reason;
    }
}

/// <summary>Codes de lettrage dans l'ordre des logiciels comptables : A … Z, AA … AZ, BA … ZZ, AAA…</summary>
public static class LetteringCodes
{
    public static string FromIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        StringBuilder code = new();
        int n = index + 1;
        while (n > 0)
        {
            n--;
            code.Insert(0, (char)('A' + (n % 26)));
            n /= 26;
        }

        return code.ToString();
    }
}

/// <summary>
/// Réglages du rapprochement d'un dossier (une seule ligne par base de dossier) : le seuil sous lequel un écart de
/// règlement est passé en 658 ou 758 au lieu de laisser la facture partiellement due.
/// </summary>
public sealed class BankingSettings : Entity<Guid>, IVersioned
{
    public static readonly Guid SingletonId = new("6c657474-7261-0000-0000-62616e717565");

    /// <summary>Un euro par défaut : arrondis, frais de virement retenus par la banque du client.</summary>
    public const decimal DefaultWriteOffThreshold = 1m;

    public const decimal MaxWriteOffThreshold = 100m;

    private BankingSettings(Guid id)
        : base(id)
    {
    }

    public decimal WriteOffThreshold { get; private set; }

    public long Version { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static BankingSettings Default() => new(SingletonId) { WriteOffThreshold = DefaultWriteOffThreshold, Version = 0 };

    public Result Update(decimal threshold, Guid userId, DateTimeOffset now)
    {
        if (threshold < 0m || threshold > MaxWriteOffThreshold || decimal.Round(threshold, 2) != threshold)
        {
            return BankingSettingsErrors.ThresholdInvalid;
        }

        WriteOffThreshold = threshold;
        UpdatedBy = userId;
        UpdatedAt = now;
        Version++;
        return Result.Success();
    }
}
