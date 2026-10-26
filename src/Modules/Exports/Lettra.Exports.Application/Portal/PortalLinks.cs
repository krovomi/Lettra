using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Portal;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.Portal;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.Portal;

/// <summary>
/// Créer un lien d'espace client (côté cabinet) : le client y voit ses factures et ses devis, paie en ligne et signe,
/// sans compte. Un lien de devis peut demander un acompte à la signature. Le jeton n'est rendu qu'une fois.
/// </summary>
public sealed record CreatePortalLinkCommand(DossierId Dossier, CreatePortalLinkRequest Request)
    : ICommand<Result<PortalLinkResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>Les liens d'espace client d'un client, du plus récent au plus ancien.</summary>
public sealed record ListPortalLinksQuery(DossierId Dossier, Guid CustomerId)
    : IQuery<Result<IReadOnlyList<PortalLinkSummary>>>, IRequirePermission
{
    public Permission Permission => Permission.TiersConsulter;
}

/// <summary>Révoquer un lien : il cesse de marcher tout de suite.</summary>
public sealed record RevokePortalLinkCommand(DossierId Dossier, Guid LinkId) : ICommand<Result>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>La preuve de signature d'un devis signé en ligne (introuvable s'il ne l'a pas été).</summary>
public sealed record GetQuoteSignatureQuery(DossierId Dossier, Guid QuoteId)
    : IQuery<Result<QuoteSignatureResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

internal sealed class CreatePortalLinkHandler(
    ICustomerPortalSettings portal,
    ICustomerRepository customers,
    IQuoteRepository quotes,
    IPortalLinkRepository links,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<CreatePortalLinkCommand, Result<PortalLinkResponse>>
{
    public const string PathPrefix = "espace-client/";

    public async Task<Result<PortalLinkResponse>> HandleAsync(CreatePortalLinkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!portal.IsEnabled)
        {
            return PortalErrors.Disabled;
        }

        CreatePortalLinkRequest request = command.Request ?? new CreatePortalLinkRequest();
        CustomerId customer = new(request.CustomerId);
        if (await customers.FindAsync(customer, cancellationToken).ConfigureAwait(false) is null)
        {
            return CustomerErrors.NotFound;
        }

        QuoteId? quoteId = request.QuoteId is Guid q ? new QuoteId(q) : null;
        if (quoteId is QuoteId wanted)
        {
            Quote? quote = await quotes.FindAsync(wanted, cancellationToken).ConfigureAwait(false);
            if (quote is null || quote.CustomerId != customer || quote.Status != QuoteStatus.Validated || quote.IsExpired(LegalCalendar.Today(clock)))
            {
                return PortalErrors.QuoteNotSignable;
            }
        }

        (string token, string hash) = PortalToken.Create(command.Dossier);
        Result<PortalLink> created = PortalLink.Create(customer, hash, quoteId, request.DownPaymentPercent, request.ValidityDays, user.UserId, clock.GetUtcNow());
        if (created.IsFailure)
        {
            return Result<PortalLinkResponse>.Failure(created.Errors);
        }

        PortalLink link = created.Value;
        await links.AddAsync(link, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new PortalLinkResponse(link.Id, token, PathPrefix + token, link.ExpiresAt, link.QuoteId?.Value, link.DownPaymentPercent);
    }
}

internal sealed class ListPortalLinksHandler(IPortalLinkRepository links, TimeProvider clock)
    : IQueryHandler<ListPortalLinksQuery, Result<IReadOnlyList<PortalLinkSummary>>>
{
    public async Task<Result<IReadOnlyList<PortalLinkSummary>>> HandleAsync(ListPortalLinksQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        DateTimeOffset now = clock.GetUtcNow();
        IReadOnlyList<PortalLink> found = await links.ListByCustomerAsync(new CustomerId(query.CustomerId), cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<PortalLinkSummary>>.Success([.. found.OrderByDescending(l => l.CreatedAt).Select(l => PortalViews.Summary(l, now))]);
    }
}

internal sealed class RevokePortalLinkHandler(IPortalLinkRepository links, IUnitOfWork unitOfWork, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<RevokePortalLinkCommand, Result>
{
    public async Task<Result> HandleAsync(RevokePortalLinkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        PortalLink? link = await links.FindAsync(command.LinkId, cancellationToken).ConfigureAwait(false);
        if (link is null)
        {
            return PortalErrors.LinkNotFound;
        }

        Result revoked = link.Revoke(user.UserId, clock.GetUtcNow());
        if (revoked.IsFailure)
        {
            return revoked;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

internal sealed class GetQuoteSignatureHandler(IQuoteSignatureRepository signatures)
    : IQueryHandler<GetQuoteSignatureQuery, Result<QuoteSignatureResponse>>
{
    public async Task<Result<QuoteSignatureResponse>> HandleAsync(GetQuoteSignatureQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        QuoteSignature? signature = await signatures.FindByQuoteAsync(new QuoteId(query.QuoteId), cancellationToken).ConfigureAwait(false);
        return signature is null ? PortalErrors.SignatureNotFound : PortalViews.Signature(signature);
    }
}

/// <summary>Les vues de l'espace client tirées du domaine.</summary>
internal static class PortalViews
{
    public static PortalLinkSummary Summary(PortalLink link, DateTimeOffset now) =>
        new(link.Id, link.QuoteId?.Value, link.DownPaymentPercent, link.CreatedAt, link.ExpiresAt, link.LastOpenedAt, link.RevokedAt, link.IsActive(now));

    public static QuoteSignatureResponse Signature(QuoteSignature signature) =>
        new(signature.QuoteId.Value, signature.SignerName, signature.SignedAt, signature.DocumentHash, signature.Provider, signature.ProviderReference,
            signature.DownPaymentInvoiceId?.Value);
}
