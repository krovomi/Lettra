using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Billing;

/// <summary>
/// Une facture, un acompte ou un avoir vient d'être validé : numéroté, figé, inscrit au journal inaltérable. Un avoir
/// porte <paramref name="CreditedInvoiceId"/>.
/// </summary>
public sealed record InvoiceValidated(
    InvoiceId InvoiceId,
    InvoiceKind Kind,
    string Number,
    string SeriesCode,
    DateOnly IssueDate,
    CustomerId CustomerId,
    decimal TotalExclTax,
    decimal TotalVat,
    decimal TotalInclTax,
    string ContentHash,
    Guid ValidatedBy,
    InvoiceId? CreditedInvoiceId,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Une pièce validée a été émise (envoyée au client).</summary>
public sealed record InvoiceIssued(InvoiceId InvoiceId, string Number, Guid IssuedBy, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Un devis a été validé (numéroté dans la série DEV).</summary>
public sealed record QuoteValidated(QuoteId QuoteId, string Number, DateOnly IssueDate, decimal TotalInclTax, Guid ValidatedBy, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Le client a accepté ou refusé le devis.</summary>
public sealed record QuoteOutcomeRecorded(QuoteId QuoteId, bool Accepted, Guid RecordedBy, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Identifiant d'un devis. Guid v7 : triable, sans collision entre postes hors ligne.</summary>
public readonly record struct QuoteId(Guid Value)
{
    public static QuoteId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Identifiant d'une facture, d'un acompte ou d'un avoir.</summary>
public readonly record struct InvoiceId(Guid Value)
{
    public static InvoiceId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
