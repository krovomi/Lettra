using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Portal;

/// <summary>
/// Lien d'espace client : ce que le cabinet envoie à son client pour qu'il voie ses factures, les paie en ligne et signe
/// ses devis, sans compte ni mot de passe. Le lien porte un secret aléatoire dont seule l'empreinte (SHA-256) est
/// gardée ; il expire, se révoque, et ne montre que les pièces de son client. Un lien de signature vise un devis précis
/// et peut demander un acompte à la signature.
/// </summary>
public sealed class PortalLink : Entity<Guid>
{
    public const int DefaultValidityDays = 30;

    public const int MaxValidityDays = 90;

    /// <summary>Longueur de l'empreinte hexadécimale du secret (SHA-256).</summary>
    public const int SecretHashLength = 64;

    private PortalLink(Guid id)
        : base(id)
    {
    }

    public CustomerId CustomerId { get; private init; }

    public string SecretHash { get; private init; } = "";

    /// <summary>Le devis à signer, pour un lien de signature ; vide pour l'espace client complet.</summary>
    public QuoteId? QuoteId { get; private init; }

    /// <summary>Acompte demandé à la signature du devis, en pourcentage de son TTC.</summary>
    public decimal? DownPaymentPercent { get; private init; }

    public Guid CreatedBy { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset ExpiresAt { get; private init; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedBy { get; private set; }

    /// <summary>Dernière ouverture par le client : le cabinet sait que le lien a été vu.</summary>
    public DateTimeOffset? LastOpenedAt { get; private set; }

    public static Result<PortalLink> Create(
        CustomerId customer,
        string secretHash,
        QuoteId? quote,
        decimal? downPaymentPercent,
        int validityDays,
        Guid userId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretHash);
        if (validityDays is < 1 or > MaxValidityDays)
        {
            return PortalErrors.ValidityInvalid;
        }

        if (downPaymentPercent is { } percent && (quote is null || percent is <= 0m or > 100m || decimal.Round(percent, 2) != percent))
        {
            return PortalErrors.DownPaymentInvalid;
        }

        return new PortalLink(Guid.CreateVersion7())
        {
            CustomerId = customer,
            SecretHash = secretHash,
            QuoteId = quote,
            DownPaymentPercent = downPaymentPercent,
            CreatedBy = userId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(validityDays),
        };
    }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public Result Revoke(Guid userId, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return PortalErrors.LinkRevoked;
        }

        RevokedAt = now;
        RevokedBy = userId;
        return Result.Success();
    }

    public void Opened(DateTimeOffset now) => LastOpenedAt = now;
}

/// <summary>
/// La preuve d'un devis signé en ligne : qui (nom saisi par le signataire), quand, l'empreinte du devis tel qu'il a été
/// signé (celle du journal inaltérable), le prestataire de signature et sa référence, et l'acompte facturé à la suite.
/// Jamais modifiée ni supprimée.
/// </summary>
public sealed class QuoteSignature : Entity<Guid>
{
    public const int MaxSignerNameLength = 120;

    public const int MaxProviderLength = 40;

    public const int MaxReferenceLength = 100;

    private QuoteSignature(Guid id)
        : base(id)
    {
    }

    public QuoteId QuoteId { get; private init; }

    public Guid PortalLinkId { get; private init; }

    public string SignerName { get; private init; } = "";

    public DateTimeOffset SignedAt { get; private init; }

    /// <summary>Empreinte SHA-256 (hexadécimale) du contenu figé du devis au moment de la signature.</summary>
    public string DocumentHash { get; private init; } = "";

    public string Provider { get; private init; } = "";

    public string ProviderReference { get; private init; } = "";

    public InvoiceId? DownPaymentInvoiceId { get; private set; }

    public static Result<QuoteSignature> Record(
        QuoteId quote, Guid portalLink, string signerName, string documentHash, string provider, string providerReference, DateTimeOffset signedAt)
    {
        Result<string> name = SignerNameOf(signerName);
        if (name.IsFailure)
        {
            return Result<QuoteSignature>.Failure(name.Errors);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(documentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerReference);
        return new QuoteSignature(Guid.CreateVersion7())
        {
            QuoteId = quote,
            PortalLinkId = portalLink,
            SignerName = name.Value,
            SignedAt = signedAt,
            DocumentHash = documentHash,
            Provider = provider[..Math.Min(provider.Length, MaxProviderLength)],
            ProviderReference = providerReference[..Math.Min(providerReference.Length, MaxReferenceLength)],
        };
    }

    /// <summary>Le nom du signataire, espaces réduits : de 2 à <see cref="MaxSignerNameLength"/> caractères.</summary>
    public static Result<string> SignerNameOf(string? raw)
    {
        string name = string.Join(' ', (raw ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return name.Length is < 2 or > MaxSignerNameLength ? PortalErrors.SignerNameInvalid : name;
    }

    public void AttachDownPayment(InvoiceId invoice) => DownPaymentInvoiceId = invoice;
}

/// <summary>Codes d'erreur de l'espace client.</summary>
public static class PortalErrors
{
    public const string DisabledCode = "Portal.Disabled";
    public const string LinkNotFoundCode = "Portal.LinkNotFound";
    public const string LinkRevokedCode = "Portal.LinkRevoked";
    public const string ValidityInvalidCode = "Portal.ValidityInvalid";
    public const string DownPaymentInvalidCode = "Portal.DownPaymentInvalid";
    public const string QuoteNotSignableCode = "Portal.QuoteNotSignable";
    public const string SignerNameInvalidCode = "Portal.SignerNameInvalid";
    public const string ConsentRequiredCode = "Portal.ConsentRequired";
    public const string AlreadySignedCode = "Portal.AlreadySigned";
    public const string NothingToPayCode = "Portal.NothingToPay";
    public const string PaymentDeclinedCode = "Portal.PaymentDeclined";
    public const string CheckoutNotFoundCode = "Portal.CheckoutNotFound";
    public const string SignatureNotFoundCode = "Portal.SignatureNotFound";

    /// <summary>L'espace client n'est pas ouvert sur cette installation (pas de compte de service, ou poste isolé).</summary>
    public static Error Disabled => new(DisabledCode, ErrorKind.Forbidden);

    /// <summary>Lien inconnu, expiré ou révoqué : une seule réponse, pour ne rien révéler.</summary>
    public static Error LinkNotFound => Error.NotFound(LinkNotFoundCode);

    public static Error LinkRevoked => Error.Conflict(LinkRevokedCode);

    public static Error ValidityInvalid => Error.Validation(ValidityInvalidCode, "ValidityDays");

    public static Error DownPaymentInvalid => Error.Validation(DownPaymentInvalidCode, "DownPaymentPercent");

    public static Error QuoteNotSignable => Error.Conflict(QuoteNotSignableCode);

    public static Error SignerNameInvalid => Error.Validation(SignerNameInvalidCode, "SignerName");

    public static Error ConsentRequired => Error.Validation(ConsentRequiredCode, "Consent");

    public static Error AlreadySigned => Error.Conflict(AlreadySignedCode);

    public static Error NothingToPay => Error.Conflict(NothingToPayCode);

    public static Error PaymentDeclined => Error.Conflict(PaymentDeclinedCode);

    public static Error CheckoutNotFound => Error.NotFound(CheckoutNotFoundCode);

    /// <summary>Le devis n'a pas été signé en ligne.</summary>
    public static Error SignatureNotFound => Error.NotFound(SignatureNotFoundCode);
}
