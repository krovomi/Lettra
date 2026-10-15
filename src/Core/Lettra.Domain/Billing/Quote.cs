using Lettra.Calculations;
using Lettra.Calculations.Compliance;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Billing;

/// <summary>Cycle de vie d'un devis (valeurs identiques à <c>QuoteStatusDto</c>).</summary>
public enum QuoteStatus
{
    Draft = 1,
    Validated = 2,
    Accepted = 3,
    Refused = 4,
    Invoiced = 5,
}

/// <summary>Contenu saisi d'un devis brouillon.</summary>
public sealed record QuoteDraft(
    CustomerId CustomerId,
    PartySnapshot Customer,
    DateOnly? ValidUntil,
    VatRegime VatRegime,
    string? Subject,
    string? Notes,
    IReadOnlyList<LineDraft> Lines);

/// <summary>
/// Devis. Invariants :
/// <list type="bullet">
/// <item>seul un brouillon se modifie ; la validation le numérote (série DEV) et le fige ;</item>
/// <item>la fin de validité ne précède pas la date du devis (30 jours par défaut) ;</item>
/// <item>acomptes et facture finale exigent un devis accepté ; les acomptes ne dépassent pas son TTC ;</item>
/// <item>un devis n'a qu'une facture finale.</item>
/// </list>
/// </summary>
public sealed class Quote : BillingDocument<QuoteId>
{
    public const string Series = "DEV";
    public const int DefaultValidityDays = 30;

    // Constructeur réservé à la persistance.
    private Quote(QuoteId id)
        : base(id)
    {
    }

    public QuoteStatus Status { get; private set; }

    public DateOnly? ValidUntil { get; private set; }

    /// <summary>Total TTC des factures d'acompte validées sur ce devis.</summary>
    public decimal DownPaymentsInclTax { get; private set; }

    public InvoiceId? FinalInvoiceId { get; private set; }

    public override bool IsDraft => Status == QuoteStatus.Draft;

    public bool IsExpired(DateOnly today) => Status == QuoteStatus.Validated && ValidUntil is { } until && until < today;

    public static Result<Quote> Create(QuoteId id, QuoteDraft draft, Guid userId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Quote quote = new(id) { Status = QuoteStatus.Draft, SeriesCode = Series };
        Result applied = quote.Apply(draft);
        if (applied.IsFailure)
        {
            return Result<Quote>.Failure(applied.Errors);
        }

        quote.Created(userId, now);
        return quote;
    }

    public Result Update(QuoteDraft draft, Guid userId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!IsDraft)
        {
            return BillingErrors.NotDraft;
        }

        Result applied = Apply(draft);
        if (applied.IsFailure)
        {
            return applied;
        }

        Touch(userId, now);
        return Result.Success();
    }

    /// <summary>Numérote et fige le devis. Le numéro vient de la série DEV, réservé par l'application.</summary>
    public Result Validate(string number, DateOnly issueDate, PartySnapshot customer, Guid userId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        if (!IsDraft)
        {
            return BillingErrors.NotDraft;
        }

        DateOnly validUntil = ValidUntil ?? issueDate.AddDays(DefaultValidityDays);
        if (validUntil < issueDate)
        {
            return QuoteErrors.ValidUntilBeforeIssueDate;
        }

        RefreshCustomer(customer);
        Number = number;
        IssueDate = issueDate;
        ValidUntil = validUntil;
        LegalMentions = string.Join('\n', VatLegalTexts());
        Status = QuoteStatus.Validated;
        ValidatedBy = userId;
        ValidatedAt = now;
        Touch(userId, now);
        Raise(new QuoteValidated(Id, number, issueDate, TotalInclTax, userId, now));
        return Result.Success();
    }

    /// <summary>Enregistre la réponse du client sur un devis validé (acceptation ou refus).</summary>
    public Result SetOutcome(bool accepted, Guid userId, DateTimeOffset now)
    {
        if (Status is not (QuoteStatus.Validated or QuoteStatus.Accepted or QuoteStatus.Refused))
        {
            return Status == QuoteStatus.Invoiced ? QuoteErrors.AlreadyInvoiced : QuoteErrors.NotValidated;
        }

        if (!accepted && DownPaymentsInclTax > 0m)
        {
            // Des acomptes ont été facturés : le refus passe par des avoirs, pas par un changement d'état.
            return QuoteErrors.AlreadyInvoiced;
        }

        Status = accepted ? QuoteStatus.Accepted : QuoteStatus.Refused;
        Touch(userId, now);
        Raise(new QuoteOutcomeRecorded(Id, accepted, userId, now));
        return Result.Success();
    }

    /// <summary>Vérifie qu'un acompte de ce montant TTC est encore possible.</summary>
    public Result CanTakeDownPayment(decimal amountInclTax)
    {
        if (Status != QuoteStatus.Accepted)
        {
            return Status == QuoteStatus.Invoiced ? QuoteErrors.AlreadyInvoiced : QuoteErrors.NotAccepted;
        }

        return amountInclTax <= 0m
            ? BillingErrors.DownPaymentInvalid
            : DownPaymentsInclTax + amountInclTax > TotalInclTax ? QuoteErrors.DownPaymentsExceedTotal : Result.Success();
    }

    /// <summary>Un acompte sur ce devis vient d'être validé.</summary>
    public void RegisterDownPayment(decimal amountInclTax, Guid userId, DateTimeOffset now)
    {
        DownPaymentsInclTax += amountInclTax;
        Touch(userId, now);
    }

    /// <summary>Rattache la facture finale (brouillon) : le devis passe à « facturé ».</summary>
    public Result AttachFinalInvoice(InvoiceId invoiceId, Guid userId, DateTimeOffset now)
    {
        if (Status != QuoteStatus.Accepted)
        {
            return Status == QuoteStatus.Invoiced ? QuoteErrors.AlreadyInvoiced : QuoteErrors.NotAccepted;
        }

        FinalInvoiceId = invoiceId;
        Status = QuoteStatus.Invoiced;
        Touch(userId, now);
        return Result.Success();
    }

    /// <summary>La facture finale brouillon a été supprimée : le devis redevient accepté.</summary>
    public void DetachFinalInvoice(InvoiceId invoiceId, Guid userId, DateTimeOffset now)
    {
        if (FinalInvoiceId == invoiceId)
        {
            FinalInvoiceId = null;
            Status = QuoteStatus.Accepted;
            Touch(userId, now);
        }
    }

    /// <summary>
    /// Lignes d'une facture d'acompte de <paramref name="ratio"/> (0 à 1) du devis : une ligne par catégorie et taux,
    /// au prorata de la base HT du devis, arrondie au centime.
    /// </summary>
    public IReadOnlyList<LineDraft> DownPaymentLines(decimal ratio, string designation) =>
        Lines
            .GroupBy(l => (l.VatCategory, l.VatRateCode, l.VatExemptionCode))
            .Select(g => new LineDraft(
                null,
                null,
                designation,
                null,
                Catalog.CatalogItemKind.Service,
                "LS",
                1m,
                Rounding.ToCents(g.Sum(l => l.TotalExclTax) * ratio),
                0m,
                g.Key.VatRateCode,
                g.Key.VatCategory == VatCategoryCodes.Exempt ? g.Key.VatExemptionCode : null))
            .Where(l => l.UnitPriceExclTax > 0m)
            .ToList();

    /// <summary>Vue du devis pour le contrôle des mentions (identité des parties, lignes, totaux, textes de TVA).</summary>
    public ComplianceDocument ToComplianceDocument(SellerTerms? terms)
    {
        PartySnapshot? seller = terms?.Seller;
        IReadOnlyList<string> mentions = IsDraft ? [.. VatLegalTexts()] : LegalMentionList;
        return new ComplianceDocument(
            CompliancePieceKind.Quote,
            IsDraft,
            seller is null ? new ComplianceParty(null, null, null, null, null, null) : Invoice.ToParty(seller),
            Invoice.ToParty(Customer),
            VatRegime == VatRegime.Franchise,
            terms?.VatOnDebits ?? false,
            Number,
            IssueDate,
            null,
            null,
            OperationCategory.ToString(),
            null,
            null,
            null,
            null,
            [.. Lines.Select(l => new ComplianceLine(l.Designation, l.Quantity, l.UnitPriceExclTax, l.VatCategory))],
            mentions);
    }

    private Result Apply(QuoteDraft draft)
    {
        List<Error> errors = [];
        Result content = ApplyContent(draft.Customer, draft.CustomerId, draft.VatRegime, draft.Subject, draft.Notes, draft.Lines, errors: errors);
        if (content.IsFailure)
        {
            return content;
        }

        ValidUntil = draft.ValidUntil;
        return Result.Success();
    }
}
