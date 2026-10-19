using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Banking;

/// <summary>Identifiant d'un encaissement. Guid v7 : triable.</summary>
public readonly record struct PaymentId(Guid Value)
{
    public static PaymentId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Mode de règlement (valeurs identiques à <c>PaymentMethodDto</c>, stockées : ajout seulement).</summary>
public enum PaymentMethod
{
    Transfer = 1,
    Cheque = 2,
    Card = 3,
    Cash = 4,
    DirectDebit = 5,
}

/// <summary>État d'un encaissement (valeurs identiques à <c>PaymentStatusDto</c>).</summary>
public enum PaymentStatus
{
    /// <summary>Comptabilisé (512 / 411) et imputé sur ses factures.</summary>
    Posted = 1,

    /// <summary>Contre-passé : une écriture inverse a été passée, les factures redeviennent dues. Rien n'est supprimé.</summary>
    Reversed = 2,
}

/// <summary>La part d'un encaissement imputée sur une facture ou un acompte.</summary>
public sealed record PaymentAllocation(InvoiceId InvoiceId, string InvoiceNumber, decimal Amount);

/// <summary>TVA en attente (44574) rendue exigible (44571) par cet encaissement, pour une facture et un taux.</summary>
public sealed record PaymentVatShare(InvoiceId InvoiceId, decimal VatRatePercent, decimal Amount);

/// <summary>
/// Un encaissement client (vague 6) : montant reçu, date, mode, référence, et son imputation sur une ou plusieurs
/// factures du même client, en tout ou partie. Invariants :
/// <list type="bullet">
/// <item>montant reçu positif, au moins une imputation, chacune positive, une seule par facture ;</item>
/// <item>l'écart entre le reçu et le total imputé (écart de règlement, 658 ou 758) ne dépasse pas le seuil du dossier ;</item>
/// <item>un encaissement ne se modifie ni ne se supprime : il se contre-passe, avec un motif, une seule fois.</item>
/// </list>
/// Il porte le numéro de la série <c>ENC</c>, attribué à l'enregistrement, et entre dans la chaîne d'intégrité.
/// </summary>
public sealed class Payment : AggregateRoot<PaymentId>, IVersioned
{
    public const string Series = "ENC";
    public const int MaxReferenceLength = 140;
    public const int MaxReasonLength = 500;
    public const int MaxAllocations = 50;

    private readonly List<PaymentAllocation> _allocations = [];
    private readonly List<PaymentVatShare> _vatShares = [];

    // Constructeur réservé à la persistance.
    private Payment(PaymentId id)
        : base(id)
    {
        Number = string.Empty;
        CustomerName = string.Empty;
    }

    public string Number { get; private set; }

    public CustomerId CustomerId { get; private set; }

    public string CustomerName { get; private set; }

    public DateOnly Date { get; private set; }

    /// <summary>Montant reçu en banque (débit du 512).</summary>
    public decimal Amount { get; private set; }

    public PaymentMethod Method { get; private set; }

    public string? Reference { get; private set; }

    /// <summary>Ligne du relevé bancaire d'où vient l'encaissement, s'il a été rapproché.</summary>
    public Guid? BankLineId { get; private set; }

    public PaymentStatus Status { get; private set; }

    public IReadOnlyList<PaymentAllocation> Allocations => _allocations;

    public IReadOnlyList<PaymentVatShare> VatShares => _vatShares;

    /// <summary>Total imputé sur les factures (crédit du 411).</summary>
    public decimal AllocatedAmount => _allocations.Sum(a => a.Amount);

    /// <summary>Écart de règlement : positif, le client a payé moins (658) ; négatif, il a payé plus (758).</summary>
    public decimal WriteOff => AllocatedAmount - Amount;

    public DateOnly? ReversalDate { get; private set; }

    public string? ReversalReason { get; private set; }

    public Guid? ReversedBy { get; private set; }

    public DateTimeOffset? ReversedAt { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public long Version { get; private set; }

    public bool IsReversed => Status == PaymentStatus.Reversed;

    /// <summary>
    /// Enregistre un encaissement. Les factures sont contrôlées par l'appelant (même client, reste dû) avec
    /// <see cref="Invoice.RegisterPayment"/>, dans la même transaction.
    /// </summary>
    public static Result<Payment> Record(
        PaymentId id,
        string number,
        CustomerId customerId,
        string customerName,
        DateOnly date,
        decimal amount,
        PaymentMethod method,
        string? reference,
        IReadOnlyList<PaymentAllocation> allocations,
        IReadOnlyList<PaymentVatShare> vatShares,
        decimal writeOffThreshold,
        Guid? bankLineId,
        Guid userId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentNullException.ThrowIfNull(allocations);
        ArgumentNullException.ThrowIfNull(vatShares);
        List<Error> errors = [];
        if (amount <= 0m || decimal.Round(amount, 2) != amount)
        {
            errors.Add(PaymentErrors.AmountInvalid);
        }

        if (!Enum.IsDefined(method))
        {
            errors.Add(PaymentErrors.MethodInvalid);
        }

        string? cleanReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        if (cleanReference is { Length: > MaxReferenceLength })
        {
            errors.Add(PaymentErrors.ReferenceTooLong);
        }

        errors.AddRange(CheckAllocations(allocations));
        if (errors.Count == 0 && Math.Abs(allocations.Sum(a => a.Amount) - amount) > Math.Max(0m, writeOffThreshold))
        {
            errors.Add(PaymentErrors.GapAboveThreshold);
        }

        if (errors.Count > 0)
        {
            return Result<Payment>.Failure(errors);
        }

        Payment payment = new(id)
        {
            Number = number,
            CustomerId = customerId,
            CustomerName = customerName,
            Date = date,
            Amount = amount,
            Method = method,
            Reference = cleanReference,
            BankLineId = bankLineId,
            Status = PaymentStatus.Posted,
            CreatedBy = userId,
            CreatedAt = now,
            Version = 1,
        };
        payment._allocations.AddRange(allocations);
        payment._vatShares.AddRange(vatShares.Where(s => s.Amount != 0m));
        return payment;
    }

    /// <summary>Contre-passe l'encaissement : ses factures redeviennent dues, son lettrage est défait avec trace.</summary>
    public Result Reverse(DateOnly date, string? reason, Guid userId, DateTimeOffset now)
    {
        if (IsReversed)
        {
            return PaymentErrors.AlreadyReversed;
        }

        string? clean = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (clean is null)
        {
            return PaymentErrors.ReasonRequired;
        }

        if (clean.Length > MaxReasonLength)
        {
            return PaymentErrors.ReasonTooLong;
        }

        Status = PaymentStatus.Reversed;
        ReversalDate = date < Date ? Date : date;
        ReversalReason = clean;
        ReversedBy = userId;
        ReversedAt = now;
        Version++;
        return Result.Success();
    }

    /// <summary>
    /// Empreinte SHA-256 (hexadécimal minuscule) de l'état figé de l'encaissement, inscrite au journal d'intégrité à
    /// l'enregistrement puis à la contre-passation. Format v1 : champs séparés par <c>|</c>, montants à deux
    /// décimales, imputations triées par facture. <b>Ne jamais modifier une version publiée.</b>
    /// </summary>
    public string ComputeFingerprint()
    {
        StringBuilder text = new();
        text.Append("v1|").Append(Id.Value.ToString("D")).Append('|').Append(Number)
            .Append('|').Append(CustomerId.Value.ToString("D"))
            .Append('|').Append(Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append('|').Append(Money(Amount))
            .Append('|').Append(Method.ToString())
            .Append('|').Append(Reference)
            .Append('|').Append(BankLineId?.ToString("D"));
        foreach (PaymentAllocation allocation in _allocations.OrderBy(a => a.InvoiceId.Value))
        {
            text.Append('|').Append(allocation.InvoiceId.Value.ToString("D")).Append('=').Append(Money(allocation.Amount));
        }

        text.Append('|').Append(Status.ToString())
            .Append('|').Append(ReversalDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append('|').Append(ReversalReason);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static List<Error> CheckAllocations(IReadOnlyList<PaymentAllocation> allocations)
    {
        List<Error> errors = [];
        if (allocations.Count == 0)
        {
            errors.Add(PaymentErrors.AllocationsRequired);
            return errors;
        }

        if (allocations.Count > MaxAllocations)
        {
            errors.Add(PaymentErrors.TooManyAllocations);
            return errors;
        }

        for (int i = 0; i < allocations.Count; i++)
        {
            if (allocations[i].Amount <= 0m || decimal.Round(allocations[i].Amount, 2) != allocations[i].Amount)
            {
                errors.Add(PaymentErrors.AllocationInvalid(i));
            }
        }

        if (allocations.Select(a => a.InvoiceId).Distinct().Count() != allocations.Count)
        {
            errors.Add(PaymentErrors.AllocationDuplicate);
        }

        return errors;
    }

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
