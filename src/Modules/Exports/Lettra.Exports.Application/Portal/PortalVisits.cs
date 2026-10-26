using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Features;
using Lettra.Application.Features.Billing;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Portal;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.Portal;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;

namespace Lettra.Exports.Application.Portal;

// Côté client : ces cas d'usage s'exécutent sous le compte de service de l'espace client, une fois le jeton du lien lu
// par l'hôte (seule l'empreinte du secret arrive ici). Le compte de service n'a que les droits que le cabinet lui donne
// au registre des droits ; chaque pièce créée (encaissement, acompte) porte son identité.

/// <summary>Ouvrir l'espace client d'un lien : l'entreprise, les factures du client et ses devis.</summary>
public sealed record OpenPortalCommand(DossierId Dossier, string SecretHash) : ICommand<Result<PortalView>>, IRequirePermission
{
    /// <summary>Devis montrés : les plus récents.</summary>
    public const int MaxQuotes = 20;

    /// <summary>Factures soldées montrées, en plus de toutes celles qui restent dues.</summary>
    public const int MaxPaidInvoices = 24;

    public Permission Permission => Permission.PortailConsulter;
}

/// <summary>
/// Signer un devis depuis l'espace client : signature électronique (prestataire), devis accepté, preuve gardée, puis la
/// facture d'acompte du lien créée et validée s'il en demande une.
/// </summary>
public sealed record SignQuoteOnPortalCommand(DossierId Dossier, string SecretHash, Guid QuoteId, SignQuoteRequest Request)
    : ICommand<Result<SignQuoteResult>>, IRequirePermission
{
    public Permission Permission => Permission.DevisAccepter;
}

/// <summary>Commencer le paiement en ligne d'une facture : la page du prestataire, pour le reste à payer.</summary>
public sealed record StartOnlinePaymentCommand(DossierId Dossier, string SecretHash, Guid InvoiceId, string ReturnUrl)
    : ICommand<Result<OnlineCheckoutResponse>>, IRequirePermission
{
    public Permission Permission => Permission.PortailConsulter;
}

/// <summary>
/// Retour du prestataire : un paiement accepté devient un encaissement par carte, imputé sur la facture (une seule fois,
/// même si le client revient deux fois).
/// </summary>
public sealed record CompleteOnlinePaymentCommand(DossierId Dossier, string SecretHash, string CheckoutId)
    : ICommand<Result<OnlinePaymentResult>>, IRequirePermission
{
    public Permission Permission => Permission.PortailConsulter;
}

/// <summary>Le lien actif d'une empreinte de secret.</summary>
internal static class PortalAccess
{
    public static async Task<Result<PortalLink>> ActiveLinkAsync(
        ICustomerPortalSettings portal, IPortalLinkRepository links, string secretHash, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!portal.IsEnabled)
        {
            return PortalErrors.Disabled;
        }

        PortalLink? link = string.IsNullOrEmpty(secretHash) ? null
            : await links.FindBySecretHashAsync(secretHash, cancellationToken).ConfigureAwait(false);
        return link is not null && link.IsActive(clock.GetUtcNow()) ? link : PortalErrors.LinkNotFound;
    }
}

internal sealed class OpenPortalHandler(
    ICustomerPortalSettings portal,
    IPortalLinkRepository links,
    ICustomerRepository customers,
    ICompanyProfileRepository profiles,
    IReceivableDocumentReader receivables,
    IQuoteReadStore quoteList,
    IQuoteRepository quotes,
    IQuoteSignatureRepository signatures,
    IOnlinePaymentProvider payments,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<OpenPortalCommand, Result<PortalView>>
{
    public async Task<Result<PortalView>> HandleAsync(OpenPortalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<PortalLink> access = await PortalAccess.ActiveLinkAsync(portal, links, command.SecretHash, clock, cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
        {
            return Result<PortalView>.Failure(access.Errors);
        }

        PortalLink link = access.Value;
        Customer? customer = await customers.FindAsync(link.CustomerId, cancellationToken).ConfigureAwait(false);
        CompanyProfile? company = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        if (customer is null || company is null)
        {
            return PortalErrors.LinkNotFound;
        }

        DateOnly today = LegalCalendar.Today(clock);
        IReadOnlyList<ReceivableDocument> documents = await receivables.ListAsync(link.CustomerId, cancellationToken).ConfigureAwait(false);
        List<ReceivableDocument> billed = [.. documents.Where(d => d.Kind != InvoiceKind.CreditNote)];
        List<PortalInvoice> invoices =
        [
            .. billed.Where(d => d.Outstanding > 0m).OrderBy(d => d.DueDate ?? d.IssueDate).ThenBy(d => d.Number, StringComparer.Ordinal)
                .Concat(billed.Where(d => d.Outstanding <= 0m).OrderByDescending(d => d.IssueDate).ThenByDescending(d => d.Number, StringComparer.Ordinal)
                    .Take(OpenPortalCommand.MaxPaidInvoices))
                .Select(d => new PortalInvoice(d.InvoiceId.Value, d.Number, (InvoiceKindDto)(int)d.Kind, d.IssueDate, d.DueDate, d.TotalInclTax,
                    Math.Max(0m, d.Outstanding), d.Outstanding > 0m && d.DueDate is DateOnly due && due < today)),
        ];

        List<PortalQuote> portalQuotes = await QuotesAsync(link, today, cancellationToken).ConfigureAwait(false);

        // Le cabinet voit que le lien a été ouvert, et quand.
        link.Opened(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new PortalView(company.Name, company.Address.City, customer.Name, company.Iban?.Value, company.Bic,
            customer.PreferredLanguage, invoices, portalQuotes, link.QuoteId?.Value, link.DownPaymentPercent, payments.IsAvailable, link.ExpiresAt);
    }

    private async Task<List<PortalQuote>> QuotesAsync(PortalLink link, DateOnly today, CancellationToken cancellationToken)
    {
        PagedResponse<QuoteSummary> page = await quoteList.ListAsync(
            new BillingListFilter(null, link.CustomerId, null, null, 1, Paging.MaxPageSize), null, today, cancellationToken).ConfigureAwait(false);
        List<QuoteId> ids =
        [
            .. page.Items.Where(q => q.Status != QuoteStatusDto.Draft && q.Number is not null)
                .OrderByDescending(q => link.QuoteId?.Value == q.Id).ThenByDescending(q => q.IssueDate)
                .Take(OpenPortalCommand.MaxQuotes).Select(q => new QuoteId(q.Id)),
        ];
        IReadOnlyDictionary<QuoteId, QuoteSignature> signed = await signatures.FindByQuotesAsync(ids, cancellationToken).ConfigureAwait(false);
        List<PortalQuote> result = [];
        foreach (QuoteId id in ids)
        {
            Quote? quote = await quotes.FindAsync(id, cancellationToken).ConfigureAwait(false);
            if (quote is null || quote.CustomerId != link.CustomerId || quote.Number is null || quote.IssueDate is null)
            {
                continue;
            }

            QuoteSignature? signature = signed.GetValueOrDefault(id);
            result.Add(new PortalQuote(
                quote.Id.Value, quote.Number, quote.IssueDate.Value, quote.ValidUntil, quote.Subject, (QuoteStatusDto)(int)quote.Status, quote.IsExpired(today),
                quote.TotalExclTax, quote.TotalVat, quote.TotalInclTax,
                PortalSigning.CanSign(quote, link, signature, today),
                [.. quote.Lines.Select(l => new PortalQuoteLine(l.Designation, l.Quantity, l.UnitCode, l.UnitPriceExclTax, l.TotalExclTax))],
                signature is null ? null : PortalViews.Signature(signature)));
        }

        return result;
    }
}

internal static class PortalSigning
{
    /// <summary>Un devis validé, encore valable, pas encore signé ; et celui du lien si le lien en vise un.</summary>
    public static bool CanSign(Quote quote, PortalLink link, QuoteSignature? signature, DateOnly today) =>
        signature is null
        && quote.Status == QuoteStatus.Validated
        && !quote.IsExpired(today)
        && (link.QuoteId is null || link.QuoteId == quote.Id);

    /// <summary>Empreinte du devis tel que signé : celle du journal inaltérable, sinon celle de son numéro et de ses totaux.</summary>
    public static async Task<string> DocumentHashAsync(IJournaledDocumentReader journal, Quote quote, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, string> hashes = await journal.ComputeContentHashesAsync([quote.Id.Value], cancellationToken).ConfigureAwait(false);
        if (hashes.TryGetValue(quote.Id.Value, out string? hash))
        {
            return hash;
        }

        string content = string.Create(CultureInfo.InvariantCulture, $"{quote.Number}|{quote.IssueDate:yyyy-MM-dd}|{quote.TotalExclTax}|{quote.TotalVat}|{quote.TotalInclTax}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }
}

internal sealed class SignQuoteOnPortalHandler(
    ICustomerPortalSettings portal,
    IPortalLinkRepository links,
    IQuoteRepository quotes,
    IQuoteSignatureRepository signatures,
    IJournaledDocumentReader journal,
    IElectronicSignatureProvider signer,
    ICommandDispatcher commands,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SignQuoteOnPortalCommand, Result<SignQuoteResult>>
{
    public async Task<Result<SignQuoteResult>> HandleAsync(SignQuoteOnPortalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<PortalLink> access = await PortalAccess.ActiveLinkAsync(portal, links, command.SecretHash, clock, cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
        {
            return Result<SignQuoteResult>.Failure(access.Errors);
        }

        PortalLink link = access.Value;
        Guid linkId = link.Id;
        decimal? downPayment = link.DownPaymentPercent;
        QuoteId quoteId = new(command.QuoteId);
        Quote? quote = await quotes.FindAsync(quoteId, cancellationToken).ConfigureAwait(false);
        if (quote is null || quote.CustomerId != link.CustomerId)
        {
            return PortalErrors.QuoteNotSignable;
        }

        QuoteSignature? existing = await signatures.FindByQuoteAsync(quoteId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return PortalErrors.AlreadySigned;
        }

        if (!PortalSigning.CanSign(quote, link, null, LegalCalendar.Today(clock)))
        {
            return PortalErrors.QuoteNotSignable;
        }

        if (command.Request is not { Consent: true })
        {
            return PortalErrors.ConsentRequired;
        }

        string number = quote.Number!;
        string hash = await PortalSigning.DocumentHashAsync(journal, quote, cancellationToken).ConfigureAwait(false);
        Result<string> signerName = QuoteSignature.SignerNameOf(command.Request.SignerName);
        if (signerName.IsFailure)
        {
            return Result<SignQuoteResult>.Failure(signerName.Errors);
        }

        Result<SignatureEvidence> evidence = await signer.SignAsync(new SignatureRequest(number, hash, signerName.Value), cancellationToken)
            .ConfigureAwait(false);
        if (evidence.IsFailure)
        {
            return Result<SignQuoteResult>.Failure(evidence.Errors);
        }

        Result accepted = await commands.SendAsync(new SetQuoteOutcomeCommand(command.Dossier, quoteId, true, null), cancellationToken).ConfigureAwait(false);
        if (accepted.IsFailure)
        {
            return Result<SignQuoteResult>.Failure(accepted.Errors);
        }

        QuoteSignature signature = QuoteSignature.Record(
            quoteId, linkId, signerName.Value, hash, evidence.Value.Provider, evidence.Value.Reference, evidence.Value.SignedAt).Value;
        await signatures.AddAsync(signature, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        InvoiceId? invoice = downPayment is decimal percent
            ? await DownPaymentAsync(command.Dossier, quoteId, percent, cancellationToken).ConfigureAwait(false)
            : null;
        if (invoice is InvoiceId created)
        {
            // Relue : la validation de l'acompte vide le suivi des entités (numérotation).
            QuoteSignature stored = (await signatures.FindByQuoteAsync(quoteId, cancellationToken).ConfigureAwait(false))!;
            stored.AttachDownPayment(created);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            signature = stored;
        }

        return new SignQuoteResult(number, PortalViews.Signature(signature), invoice?.Value);
    }

    /// <summary>
    /// La facture d'acompte demandée par le lien, créée et validée sous le compte de service. Un échec (droits, fiche
    /// entreprise incomplète) laisse le devis signé : le cabinet fait l'acompte depuis l'écran du devis.
    /// </summary>
    private async Task<InvoiceId?> DownPaymentAsync(DossierId dossier, QuoteId quote, decimal percent, CancellationToken cancellationToken)
    {
        Result<InvoiceId> created = await commands.SendAsync(
            new CreateDownPaymentCommand(dossier, quote, new CreateDownPaymentRequest { Percent = percent, DueDate = LegalCalendar.Today(clock) }),
            cancellationToken).ConfigureAwait(false);
        if (created.IsFailure)
        {
            return null;
        }

        Result validated = await commands.SendAsync(new ValidateInvoiceCommand(dossier, created.Value, null), cancellationToken).ConfigureAwait(false);
        return validated.IsSuccess ? created.Value : null;
    }
}

internal sealed class StartOnlinePaymentHandler(
    ICustomerPortalSettings portal,
    IPortalLinkRepository links,
    IReceivableDocumentReader receivables,
    ICompanyProfileRepository profiles,
    IOnlinePaymentProvider payments,
    TimeProvider clock) : ICommandHandler<StartOnlinePaymentCommand, Result<OnlineCheckoutResponse>>
{
    public async Task<Result<OnlineCheckoutResponse>> HandleAsync(StartOnlinePaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<PortalLink> access = await PortalAccess.ActiveLinkAsync(portal, links, command.SecretHash, clock, cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
        {
            return Result<OnlineCheckoutResponse>.Failure(access.Errors);
        }

        PortalLink link = access.Value;
        IReadOnlyList<ReceivableDocument> documents = await receivables.ListAsync(link.CustomerId, cancellationToken).ConfigureAwait(false);
        ReceivableDocument? invoice = documents.FirstOrDefault(d => d.InvoiceId.Value == command.InvoiceId && d.Kind != InvoiceKind.CreditNote);
        if (invoice is null || invoice.Outstanding <= 0m)
        {
            return PortalErrors.NothingToPay;
        }

        CompanyProfile? company = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        Result<Checkout> checkout = await payments.CreateCheckoutAsync(
            new CheckoutRequest(command.Dossier, link.Id, invoice.InvoiceId, invoice.Number, invoice.Outstanding, Money.Euro, company?.Name ?? "", command.ReturnUrl),
            cancellationToken).ConfigureAwait(false);
        return checkout.IsFailure
            ? Result<OnlineCheckoutResponse>.Failure(checkout.Errors)
            : new OnlineCheckoutResponse(checkout.Value.Id, checkout.Value.RedirectUrl, invoice.Outstanding);
    }
}

internal sealed class CompleteOnlinePaymentHandler(
    ICustomerPortalSettings portal,
    IPortalLinkRepository links,
    IOnlinePaymentProvider payments,
    ICommandDispatcher commands,
    TimeProvider clock) : ICommandHandler<CompleteOnlinePaymentCommand, Result<OnlinePaymentResult>>
{
    public async Task<Result<OnlinePaymentResult>> HandleAsync(CompleteOnlinePaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<PortalLink> access = await PortalAccess.ActiveLinkAsync(portal, links, command.SecretHash, clock, cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
        {
            return Result<OnlinePaymentResult>.Failure(access.Errors);
        }

        PortalLink link = access.Value;
        Checkout? checkout = await payments.FindAsync(command.CheckoutId, cancellationToken).ConfigureAwait(false);
        if (checkout is null || checkout.Request.Dossier != command.Dossier || checkout.Request.PortalLinkId != link.Id)
        {
            return PortalErrors.CheckoutNotFound;
        }

        CheckoutRequest request = checkout.Request;
        switch (checkout.State)
        {
            case CheckoutState.Recorded:
                return new OnlinePaymentResult(true, request.InvoiceNumber, request.Amount, checkout.ProviderReference, null);
            case CheckoutState.Declined:
                return PortalErrors.PaymentDeclined;
            case CheckoutState.Pending:
                return new OnlinePaymentResult(false, request.InvoiceNumber, request.Amount, null, null);
        }

        // Un seul retour enregistre l'encaissement ; un retour simultané le voit déjà pris en charge.
        if (!await payments.TryClaimAsync(checkout.Id, cancellationToken).ConfigureAwait(false))
        {
            return new OnlinePaymentResult(true, request.InvoiceNumber, request.Amount, checkout.ProviderReference, null);
        }

        string reference = $"{payments.Name} {checkout.ProviderReference}";
        Result<PaymentResponse> recorded = await commands.SendAsync(new RecordPaymentCommand(command.Dossier, new RecordPaymentRequest
        {
            CustomerId = link.CustomerId.Value,
            Date = LegalCalendar.Today(clock),
            Amount = request.Amount,
            Method = PaymentMethodDto.Card,
            Reference = reference[..Math.Min(reference.Length, 140)],
            Allocations = [new PaymentAllocationRequest(request.InvoiceId.Value, request.Amount)],
        }), cancellationToken).ConfigureAwait(false);
        if (recorded.IsFailure)
        {
            await payments.ReleaseAsync(checkout.Id, cancellationToken).ConfigureAwait(false);
            return Result<OnlinePaymentResult>.Failure(recorded.Errors);
        }

        return new OnlinePaymentResult(true, request.InvoiceNumber, request.Amount, checkout.ProviderReference, recorded.Value.Number);
    }
}
