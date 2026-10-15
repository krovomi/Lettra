using System.Globalization;
using Lettra.Calculations;
using Lettra.Calculations.Compliance;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Billing;

/// <summary>Nature d'une pièce de la série des factures (valeurs identiques à <c>InvoiceKindDto</c>).</summary>
public enum InvoiceKind
{
    Invoice = 1,
    DownPayment = 2,
    CreditNote = 3,
}

/// <summary>Cycle de vie (valeurs identiques à <c>InvoiceStatusDto</c>).</summary>
public enum InvoiceStatus
{
    Draft = 1,
    Validated = 2,
    Issued = 3,
}

/// <summary>En-tête saisi d'une facture brouillon.</summary>
public sealed record InvoiceHeader(
    DateOnly? DeliveryDate,
    DateOnly? DueDate,
    string? PaymentTerms,
    string? DeliveryAddress,
    string? PurchaseOrderReference,
    VatRegime VatRegime,
    string? Subject,
    string? Notes);

/// <summary>Conditions de l'émetteur copiées sur la pièce à la validation (fiche entreprise).</summary>
public sealed record SellerTerms(
    PartySnapshot Seller,
    bool VatFranchise,
    bool VatOnDebits,
    int PaymentDays,
    string? PaymentTerms,
    decimal? LatePenaltyRate,
    string? EarlyPaymentDiscount);

/// <summary>
/// Facture, facture d'acompte ou avoir : les trois partagent la série FAC (numérotation chronologique continue).
/// Invariants :
/// <list type="bullet">
/// <item>seul un brouillon se modifie ou se supprime ; la validation numérote, fige et calcule l'empreinte ;</item>
/// <item>une pièce validée ne se corrige que par un avoir, dont le total ne dépasse pas le solde non crédité ;</item>
/// <item>les lignes d'un acompte ou d'un avoir viennent de leur pièce d'origine et ne se saisissent pas ;</item>
/// <item>l'échéance ne précède pas la date d'émission.</item>
/// </list>
/// </summary>
public sealed class Invoice : BillingDocument<InvoiceId>
{
    public const string Series = "FAC";
    public const int MaxPaymentTermsLength = 500;
    public const int MaxDeliveryAddressLength = 500;
    public const int MaxPurchaseOrderLength = 100;
    public const int MaxCreditReasonLength = 500;

    // Constructeur réservé à la persistance.
    private Invoice(InvoiceId id)
        : base(id)
    {
    }

    public InvoiceKind Kind { get; private set; }

    public InvoiceStatus Status { get; private set; }

    public PartySnapshot? Seller { get; private set; }

    public DateOnly? DeliveryDate { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public string? PaymentTerms { get; private set; }

    public string? DeliveryAddress { get; private set; }

    public string? PurchaseOrderReference { get; private set; }

    public decimal? LatePenaltyRate { get; private set; }

    public string? EarlyPaymentDiscount { get; private set; }

    public QuoteId? QuoteId { get; private set; }

    public string? QuoteNumber { get; private set; }

    public InvoiceId? CreditedInvoiceId { get; private set; }

    public string? CreditedInvoiceNumber { get; private set; }

    public string? CreditReason { get; private set; }

    /// <summary>Total TTC des avoirs validés sur cette pièce.</summary>
    public decimal CreditedInclTax { get; private set; }

    /// <summary>Empreinte SHA-256 de la forme canonique (<c>Lettra.Contracts.Billing.CanonicalDocument</c>), figée à la validation.</summary>
    public string? ContentHash { get; private set; }

    public Guid? IssuedBy { get; private set; }

    public DateTimeOffset? IssuedAt { get; private set; }

    /// <summary>Manager qui a approuvé cet avoir (séparation des tâches, au-delà du seuil du cabinet).</summary>
    public Guid? CreditApprovedBy { get; private set; }

    public DateTimeOffset? CreditApprovedAt { get; private set; }

    public override bool IsDraft => Status == InvoiceStatus.Draft;

    public bool IsDerived => Kind != InvoiceKind.Invoice;

    public decimal CreditableBalance => TotalInclTax - CreditedInclTax;

    /// <summary>Facture saisie librement.</summary>
    public static Result<Invoice> Create(
        InvoiceId id, CustomerId customerId, PartySnapshot customer, InvoiceHeader header, IReadOnlyList<LineDraft> lines, Guid userId, DateTimeOffset now)
    {
        Invoice invoice = new(id) { Kind = InvoiceKind.Invoice, Status = InvoiceStatus.Draft, SeriesCode = Series };
        Result applied = invoice.Apply(customerId, customer, header, lines, natureOf: null);
        if (applied.IsFailure)
        {
            return Result<Invoice>.Failure(applied.Errors);
        }

        invoice.Created(userId, now);
        return invoice;
    }

    /// <summary>Facture d'acompte brouillon sur un devis accepté.</summary>
    public static Result<Invoice> CreateDownPayment(
        InvoiceId id, Quote quote, decimal ratio, DateOnly? dueDate, string designation, Guid userId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(quote);
        IReadOnlyList<LineDraft> lines = quote.DownPaymentLines(ratio, designation);
        Invoice invoice = new(id)
        {
            Kind = InvoiceKind.DownPayment,
            Status = InvoiceStatus.Draft,
            SeriesCode = Series,
            QuoteId = quote.Id,
            QuoteNumber = quote.Number,
        };
        InvoiceHeader header = new(null, dueDate, null, null, null, quote.VatRegime, quote.Subject, null);
        Result applied = invoice.Apply(quote.CustomerId, quote.Customer, header, lines, _ => RevenueNature.DownPayment);
        if (applied.IsFailure)
        {
            return Result<Invoice>.Failure(applied.Errors);
        }

        invoice.Created(userId, now);
        return invoice;
    }

    /// <summary>
    /// Facture finale brouillon d'un devis : ses lignes, puis une ligne négative par ligne de chaque acompte validé.
    /// </summary>
    public static Result<Invoice> CreateFinal(
        InvoiceId id, Quote quote, IReadOnlyList<Invoice> downPayments, Func<Invoice, string> deductionDesignation, Guid userId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(downPayments);
        ArgumentNullException.ThrowIfNull(deductionDesignation);
        List<LineDraft> lines = [.. quote.Lines.Select(l => l.ToDraft())];
        int firstDeduction = lines.Count;
        foreach (Invoice downPayment in downPayments.Where(d => !d.IsDraft && d.Kind == InvoiceKind.DownPayment))
        {
            lines.AddRange(downPayment.Lines.Select(l => l.ToDraft(-l.Quantity) with { Designation = deductionDesignation(downPayment) }));
        }

        Invoice invoice = new(id)
        {
            Kind = InvoiceKind.Invoice,
            Status = InvoiceStatus.Draft,
            SeriesCode = Series,
            QuoteId = quote.Id,
            QuoteNumber = quote.Number,
        };
        InvoiceHeader header = new(null, null, null, null, null, quote.VatRegime, quote.Subject, quote.Notes);
        Result applied = invoice.Apply(quote.CustomerId, quote.Customer, header, lines, i => i >= firstDeduction ? RevenueNature.DownPayment : null);
        if (applied.IsFailure)
        {
            return Result<Invoice>.Failure(applied.Errors);
        }

        invoice.Created(userId, now);
        return invoice;
    }

    /// <summary>
    /// Avoir brouillon sur une pièce validée. <paramref name="quantities"/> : quantité à créditer par rang de ligne ;
    /// <c>null</c> pour toute la pièce (refusé si elle est déjà en partie créditée).
    /// </summary>
    public static Result<Invoice> CreateCreditNote(
        InvoiceId id, Invoice original, IReadOnlyDictionary<int, decimal>? quantities, string? reason, Guid userId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.Kind == InvoiceKind.CreditNote)
        {
            return InvoiceErrors.CannotCreditCreditNote;
        }

        if (original.IsDraft)
        {
            return InvoiceErrors.NotValidated;
        }

        if (original.CreditableBalance <= 0m)
        {
            return InvoiceErrors.NothingToCredit;
        }

        string? cleanReason = Clean(reason);
        if (cleanReason is { Length: > MaxCreditReasonLength })
        {
            return BillingErrors.CreditReasonTooLong;
        }

        List<LineDraft> lines = [];
        List<RevenueNature> natures = [];
        if (quantities is null)
        {
            if (original.CreditedInclTax > 0m)
            {
                return InvoiceErrors.CreditExceedsBalance;
            }

            lines.AddRange(original.Lines.Select(l => l.ToDraft()));
            natures.AddRange(original.Lines.Select(l => l.Nature));
        }
        else
        {
            foreach ((int position, decimal quantity) in quantities.OrderBy(q => q.Key))
            {
                DocumentLine? line = original.Lines.FirstOrDefault(l => l.Position == position);
                if (line is null || quantity <= 0m || quantity > Math.Abs(line.Quantity))
                {
                    return InvoiceErrors.CreditLineUnknown;
                }

                lines.Add(line.ToDraft(Math.Sign(line.Quantity) * quantity));
                natures.Add(line.Nature);
            }
        }

        Invoice credit = new(id)
        {
            Kind = InvoiceKind.CreditNote,
            Status = InvoiceStatus.Draft,
            SeriesCode = Series,
            CreditedInvoiceId = original.Id,
            CreditedInvoiceNumber = original.Number,
            CreditReason = cleanReason,
            QuoteId = original.QuoteId,
            QuoteNumber = original.QuoteNumber,
        };
        InvoiceHeader header = new(null, null, null, null, original.PurchaseOrderReference, original.VatRegime, original.Subject, null);
        Result applied = credit.Apply(original.CustomerId, original.Customer, header, lines, i => natures[i]);
        if (applied.IsFailure)
        {
            return Result<Invoice>.Failure(applied.Errors);
        }

        if (credit.TotalInclTax > original.CreditableBalance)
        {
            return InvoiceErrors.CreditExceedsBalance;
        }

        credit.Created(userId, now);
        return credit;
    }

    /// <summary>Modifie une facture brouillon. Les lignes d'un acompte ou d'un avoir ne se saisissent pas.</summary>
    public Result Update(CustomerId customerId, PartySnapshot customer, InvoiceHeader header, IReadOnlyList<LineDraft> lines, Guid userId, DateTimeOffset now)
    {
        if (!IsDraft)
        {
            return BillingErrors.NotDraft;
        }

        if (IsDerived || QuoteId is not null && customerId != CustomerId)
        {
            return InvoiceErrors.DerivedDocumentLocked;
        }

        List<DocumentLine> previous = [.. Lines];
        Result applied = Apply(customerId, customer, header, lines, i => i < previous.Count && previous[i].Nature == RevenueNature.DownPayment ? RevenueNature.DownPayment : null);
        if (applied.IsFailure)
        {
            return applied;
        }

        Touch(userId, now);
        return Result.Success();
    }

    /// <summary>
    /// Numérote et fige la pièce : copie de l'émetteur et du client, échéance et conditions de paiement par défaut,
    /// mentions légales. L'empreinte se pose ensuite par <see cref="Seal"/>, sur la forme canonique de ce résultat.
    /// </summary>
    public Result Validate(string number, DateOnly issueDate, PartySnapshot customer, SellerTerms terms, Guid userId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentNullException.ThrowIfNull(terms);
        if (!IsDraft)
        {
            return BillingErrors.NotDraft;
        }

        DateOnly? dueDate = Kind == InvoiceKind.CreditNote ? null : DueDate ?? issueDate.AddDays(terms.PaymentDays);
        if (dueDate < issueDate)
        {
            return BillingErrors.DueDateBeforeIssueDate;
        }

        RefreshCustomer(customer);
        Seller = terms.Seller;
        Number = number;
        IssueDate = issueDate;
        DueDate = dueDate;
        PaymentTerms ??= Kind == InvoiceKind.CreditNote ? null : terms.PaymentTerms;
        LatePenaltyRate = Kind == InvoiceKind.CreditNote ? null : terms.LatePenaltyRate;
        EarlyPaymentDiscount = Kind == InvoiceKind.CreditNote ? null : terms.EarlyPaymentDiscount;
        LegalMentions = string.Join('\n', BuildLegalMentions(terms));
        Status = InvoiceStatus.Validated;
        ValidatedBy = userId;
        ValidatedAt = now;
        Touch(userId, now);
        return Result.Success();
    }

    /// <summary>Pose l'empreinte de la forme canonique, une seule fois, juste après la validation.</summary>
    public void Seal(string contentHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        if (Status != InvoiceStatus.Validated || ContentHash is not null)
        {
            throw new InvalidOperationException("Seule une pièce tout juste validée reçoit son empreinte.");
        }

        ContentHash = contentHash;
        Raise(new InvoiceValidated(
            Id, Kind, Number!, SeriesCode, IssueDate!.Value, CustomerId, TotalExclTax, TotalVat, TotalInclTax, contentHash, ValidatedBy!.Value, CreditedInvoiceId, now));
    }

    /// <summary>Marque la pièce validée comme émise.</summary>
    public Result Issue(Guid userId, DateTimeOffset now)
    {
        if (Status == InvoiceStatus.Draft)
        {
            return InvoiceErrors.NotValidated;
        }

        if (Status == InvoiceStatus.Issued)
        {
            return InvoiceErrors.AlreadyIssued;
        }

        Status = InvoiceStatus.Issued;
        IssuedBy = userId;
        IssuedAt = now;
        Touch(userId, now);
        Raise(new InvoiceIssued(Id, Number!, userId, now));
        return Result.Success();
    }

    /// <summary>
    /// Approbation d'un avoir brouillon par un manager. Elle ne touche pas aux auteurs de la saisie
    /// (<see cref="BillingDocument{TId}.LastModifiedBy"/>) : l'approbateur peut ensuite le valider. Un avoir ne se
    /// modifie pas, l'approbation reste donc attachée au montant approuvé.
    /// </summary>
    public Result ApproveCredit(Guid userId, DateTimeOffset now)
    {
        if (Kind != InvoiceKind.CreditNote)
        {
            throw new InvalidOperationException("Seul un avoir s'approuve.");
        }

        if (!IsDraft)
        {
            return BillingErrors.NotDraft;
        }

        CreditApprovedBy ??= userId;
        CreditApprovedAt ??= now;
        return Result.Success();
    }

    /// <summary>Un avoir sur cette pièce vient d'être validé.</summary>
    public Result RegisterCredit(decimal amountInclTax, Guid userId, DateTimeOffset now)
    {
        if (amountInclTax > CreditableBalance)
        {
            return InvoiceErrors.CreditExceedsBalance;
        }

        CreditedInclTax += amountInclTax;
        Touch(userId, now);
        return Result.Success();
    }

    /// <summary>Lignes vues par la comptabilité (nature et montant HT).</summary>
    public IEnumerable<RevenueLine> RevenueLines => Lines.Select(l => new RevenueLine(l.Nature, l.TotalExclTax));

    /// <summary>Vue de la pièce pour le contrôle des mentions obligatoires.</summary>
    public ComplianceDocument ToComplianceDocument(SellerTerms? terms)
    {
        PartySnapshot? seller = Seller ?? terms?.Seller;
        bool franchise = VatRegime == VatRegime.Franchise;
        IReadOnlyList<string> mentions = IsDraft && terms is not null ? BuildLegalMentions(terms) : LegalMentionList;
        return new ComplianceDocument(
            Kind switch
            {
                InvoiceKind.DownPayment => CompliancePieceKind.DownPayment,
                InvoiceKind.CreditNote => CompliancePieceKind.CreditNote,
                _ => CompliancePieceKind.Invoice,
            },
            IsDraft,
            seller is null ? new ComplianceParty(null, null, null, null, null, null) : ToParty(seller),
            ToParty(Customer),
            franchise,
            terms?.VatOnDebits ?? LegalMentionList.Any(m => m.Contains("débits", StringComparison.Ordinal)),
            Number,
            IssueDate,
            DeliveryDate,
            DeliveryAddress,
            OperationCategory.ToString(),
            DueDate ?? (IsDraft && terms is not null ? DateOnly.MinValue : null),
            PaymentTerms ?? (IsDraft ? terms?.PaymentTerms : null),
            IsDraft ? terms?.LatePenaltyRate : LatePenaltyRate,
            IsDraft ? terms?.EarlyPaymentDiscount : EarlyPaymentDiscount,
            [.. Lines.Select(l => new ComplianceLine(l.Designation, l.Quantity, l.UnitPriceExclTax, l.VatCategory))],
            mentions,
            CreditedInvoiceNumber);
    }

    internal static ComplianceParty ToParty(PartySnapshot party) => new(
        party.Name, party.SingleLineAddress, party.Siren, party.Siret, party.VatNumber, party.CountryCode,
        party.LegalForm, party.ShareCapital, party.Rcs, party.IsBusiness);

    private List<string> BuildLegalMentions(SellerTerms terms)
    {
        List<string> mentions = [.. VatLegalTexts()];
        if (terms.VatOnDebits && Kind != InvoiceKind.CreditNote)
        {
            mentions.Add("Option pour le paiement de la TVA d'après les débits");
        }

        if (Kind == InvoiceKind.CreditNote)
        {
            mentions.Add($"Avoir sur la facture n° {CreditedInvoiceNumber}");
            return mentions;
        }

        if (terms.LatePenaltyRate is { } rate)
        {
            mentions.Add(string.Create(CultureInfo.GetCultureInfo("fr-FR"), $"Pénalités de retard : {rate:0.##} % par an"));
        }

        if (Customer.IsBusiness)
        {
            mentions.Add(string.Create(
                CultureInfo.GetCultureInfo("fr-FR"),
                $"Indemnité forfaitaire pour frais de recouvrement en cas de retard de paiement : {MandatoryMentions.RecoveryIndemnityAmount:0} €"));
        }

        if (!string.IsNullOrWhiteSpace(terms.EarlyPaymentDiscount))
        {
            mentions.Add(terms.EarlyPaymentDiscount.Trim());
        }

        return mentions;
    }

    private Result Apply(CustomerId customerId, PartySnapshot customer, InvoiceHeader header, IReadOnlyList<LineDraft> lines, Func<int, RevenueNature?>? natureOf)
    {
        ArgumentNullException.ThrowIfNull(header);
        List<Error> errors = [];
        string? terms = Clean(header.PaymentTerms);
        if (terms is { Length: > MaxPaymentTermsLength })
        {
            errors.Add(BillingErrors.PaymentTermsTooLong);
        }

        string? deliveryAddress = Clean(header.DeliveryAddress);
        if (deliveryAddress is { Length: > MaxDeliveryAddressLength })
        {
            errors.Add(BillingErrors.DeliveryAddressTooLong);
        }

        string? purchaseOrder = Clean(header.PurchaseOrderReference);
        if (purchaseOrder is { Length: > MaxPurchaseOrderLength })
        {
            errors.Add(BillingErrors.PurchaseOrderReferenceTooLong);
        }

        Result content = ApplyContent(customer, customerId, header.VatRegime, header.Subject, header.Notes, lines, natureOf, errors);
        if (content.IsFailure)
        {
            return content;
        }

        DeliveryDate = header.DeliveryDate;
        DueDate = header.DueDate;
        PaymentTerms = terms;
        DeliveryAddress = deliveryAddress;
        PurchaseOrderReference = purchaseOrder;
        return Result.Success();
    }
}
