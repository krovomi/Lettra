using System.Buffers.Text;
using System.Security.Cryptography;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Portal;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.Portal;

/// <summary>
/// L'espace client est-il ouvert sur cette installation ? Il l'est sur un serveur qui a un compte de service pour agir au
/// nom du client (<c>Lettra:CustomerPortal:ServiceAccountId</c>) ; jamais sur un poste isolé.
/// </summary>
public interface ICustomerPortalSettings
{
    bool IsEnabled { get; }
}

/// <summary>Par défaut : fermé.</summary>
internal sealed class ClosedCustomerPortal : ICustomerPortalSettings
{
    public bool IsEnabled => false;
}

/// <summary>Les liens d'espace client du dossier.</summary>
public interface IPortalLinkRepository
{
    Task AddAsync(PortalLink link, CancellationToken cancellationToken);

    Task<PortalLink?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<PortalLink?> FindBySecretHashAsync(string secretHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<PortalLink>> ListByCustomerAsync(CustomerId customer, CancellationToken cancellationToken);
}

/// <summary>Les preuves de signature des devis signés en ligne.</summary>
public interface IQuoteSignatureRepository
{
    Task AddAsync(QuoteSignature signature, CancellationToken cancellationToken);

    Task<QuoteSignature?> FindByQuoteAsync(QuoteId quote, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<QuoteId, QuoteSignature>> FindByQuotesAsync(IReadOnlyCollection<QuoteId> quotes, CancellationToken cancellationToken);
}

/// <summary>Ce que Lettra demande au prestataire de paiement : encaisser ce montant pour cette facture.</summary>
public sealed record CheckoutRequest(
    DossierId Dossier,
    Guid PortalLinkId,
    InvoiceId InvoiceId,
    string InvoiceNumber,
    decimal Amount,
    string Currency,
    string PayeeName,
    string ReturnUrl);

/// <summary>État d'un paiement chez le prestataire.</summary>
public enum CheckoutState
{
    /// <summary>Le client n'a pas encore payé.</summary>
    Pending = 1,

    Paid = 2,

    Declined = 3,

    /// <summary>Payé, et l'encaissement est enregistré dans Lettra : on ne l'enregistre pas deux fois.</summary>
    Recorded = 4,
}

/// <summary>Un paiement chez le prestataire : sa demande, son état, la référence de la transaction.</summary>
public sealed record Checkout(string Id, CheckoutRequest Request, CheckoutState State, string? ProviderReference, string RedirectUrl);

/// <summary>
/// Prestataire de paiement en ligne (carte, virement instantané). Le client paie sur la page du prestataire : Lettra ne
/// voit jamais de numéro de carte. Seul l'adapter simulé existe pour l'instant ; un vrai prestataire (Stripe, Mollie,
/// PayPlug, Lyra…) attend la décision du cabinet.
/// </summary>
public interface IOnlinePaymentProvider
{
    /// <summary>Le nom affiché dans les preuves (« Paiement simulé »…).</summary>
    string Name { get; }

    /// <summary>Faux sans prestataire configuré : l'espace client ne propose alors que le virement (QR code).</summary>
    bool IsAvailable { get; }

    Task<Result<Checkout>> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken);

    Task<Checkout?> FindAsync(string checkoutId, CancellationToken cancellationToken);

    /// <summary>
    /// Réserve un paiement accepté pour l'enregistrer (il passe à <see cref="CheckoutState.Recorded"/>) : vrai pour un
    /// seul appelant, même si le client revient deux fois en même temps.
    /// </summary>
    Task<bool> TryClaimAsync(string checkoutId, CancellationToken cancellationToken);

    /// <summary>L'encaissement n'a pas pu être enregistré : le paiement redevient accepté, à reprendre au prochain retour.</summary>
    Task ReleaseAsync(string checkoutId, CancellationToken cancellationToken);
}

/// <summary>Ce qui est signé : le numéro du devis, l'empreinte de son contenu figé et le nom du signataire.</summary>
public sealed record SignatureRequest(string DocumentNumber, string DocumentHash, string SignerName);

/// <summary>La preuve rendue par le prestataire de signature.</summary>
public sealed record SignatureEvidence(string Provider, string Reference, DateTimeOffset SignedAt);

/// <summary>
/// Prestataire de signature électronique. L'adapter simulé donne une signature électronique simple (nom, accord,
/// horodatage, empreinte) ; un prestataire qualifié eIDAS (Yousign, Docusign…) attend la décision du cabinet.
/// </summary>
public interface IElectronicSignatureProvider
{
    Task<Result<SignatureEvidence>> SignAsync(SignatureRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Jeton d'un lien d'espace client : l'identifiant du dossier (16 octets) suivi d'un secret aléatoire (32 octets), en
/// base64url. Le dossier dit quelle base ouvrir ; le secret prouve le lien, et seule son empreinte SHA-256 est gardée.
/// </summary>
public static class PortalToken
{
    private const int SecretLength = 32;

    private const int TokenBytes = 16 + SecretLength;

    public static (string Token, string SecretHash) Create(DossierId dossier)
    {
        Span<byte> bytes = stackalloc byte[TokenBytes];
        if (!dossier.Value.TryWriteBytes(bytes[..16]))
        {
            throw new InvalidOperationException("Identifiant de dossier illisible.");
        }

        RandomNumberGenerator.Fill(bytes[16..]);
        return (Base64Url.EncodeToString(bytes), Hash(bytes[16..]));
    }

    /// <summary>Le dossier et l'empreinte du secret d'un jeton bien formé ; rien sinon.</summary>
    public static (DossierId Dossier, string SecretHash)? Read(string? token)
    {
        // Une adresse tapée à la main ou tronquée n'est qu'un lien introuvable : jamais une exception.
        if (string.IsNullOrEmpty(token) || token.Length > 128 || !Base64Url.IsValid(token, out int length) || length != TokenBytes)
        {
            return null;
        }

        Span<byte> bytes = stackalloc byte[TokenBytes];
        if (!Base64Url.TryDecodeFromChars(token, bytes, out int written) || written != TokenBytes)
        {
            return null;
        }

        Guid dossier = new(bytes[..16]);
        return dossier == Guid.Empty ? null : (new DossierId(dossier), Hash(bytes[16..]));
    }

    private static string Hash(ReadOnlySpan<byte> secret) => Convert.ToHexStringLower(SHA256.HashData(secret));
}
