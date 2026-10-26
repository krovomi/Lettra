using System.Security.Cryptography;
using Lettra.Domain.Common;
using Lettra.Domain.Portal;
using Lettra.Exports.Application.Portal;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Un prestataire de paiement simulé, en mémoire (<c>Lettra:CustomerPortal:Payments=Simulated</c>, par défaut) : la page de
/// paiement est servie par Lettra lui-même (<see cref="PagePath"/>), aucune carte n'est débitée, rien ne sort du
/// processus. Le numéro de test <see cref="DeclinedCard"/> est refusé ; tout autre numéro est accepté. Comme sur une page
/// de paiement réelle, le client peut réessayer après un refus ; au bout de <see cref="MaxDeclines"/> refus la session se
/// ferme. Les paiements commencés sont perdus au redémarrage, comme une session de paiement expirée.
/// </summary>
public sealed class SimulatedPaymentProvider(TimeProvider clock) : IOnlinePaymentProvider
{
    public const string ProviderName = "Paiement simulé";

    /// <summary>Chemin de la page de paiement simulée, suivi de l'identifiant du paiement.</summary>
    public const string PagePath = "/paiement-simule/";

    /// <summary>La carte de test refusée par le prestataire simulé.</summary>
    public const string DeclinedCard = "4000000000000002";

    /// <summary>Refus de carte au-delà desquels la session de paiement se ferme.</summary>
    public const int MaxDeclines = 3;

    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(1);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Session> _checkouts = new(StringComparer.Ordinal);

    public string Name => ProviderName;

    public bool IsAvailable => true;

    public Task<Result<Checkout>> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string id = "cs_sim_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
        Checkout checkout = new(id, request, CheckoutState.Pending, null, PagePath + id);
        lock (_lock)
        {
            Purge();
            _checkouts[id] = new Session(checkout, clock.GetUtcNow() + SessionLifetime, 0);
        }

        return Task.FromResult<Result<Checkout>>(checkout);
    }

    public Task<Checkout?> FindAsync(string checkoutId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            Purge();
            return Task.FromResult(_checkouts.TryGetValue(checkoutId ?? "", out Session? found) ? found.Checkout : null);
        }
    }

    public Task<bool> TryClaimAsync(string checkoutId, CancellationToken cancellationToken)
    {
        bool claimed = false;
        Update(checkoutId, c =>
        {
            claimed = c.State == CheckoutState.Paid;
            return claimed ? c with { State = CheckoutState.Recorded } : c;
        });
        return Task.FromResult(claimed);
    }

    public Task ReleaseAsync(string checkoutId, CancellationToken cancellationToken)
    {
        Update(checkoutId, c => c.State == CheckoutState.Recorded ? c with { State = CheckoutState.Paid } : c);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Ce que fait la page de paiement simulée quand le client valide : la carte est acceptée (référence de transaction)
    /// ou refusée (la session reste ouverte pour un autre essai, puis se ferme au bout de <see cref="MaxDeclines"/> refus).
    /// Renvoie l'issue de l'essai, ou rien si le paiement est inconnu ou n'attend plus de carte.
    /// </summary>
    public CheckoutState? Pay(string checkoutId, string? cardNumber)
    {
        string digits = string.Concat((cardNumber ?? "").Where(char.IsAsciiDigit));
        bool declined = digits == DeclinedCard || digits.Length is < 12 or > 19;
        string reference = "pi_sim_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        lock (_lock)
        {
            if (!_checkouts.TryGetValue(checkoutId ?? "", out Session? session) || session.Checkout.State != CheckoutState.Pending)
            {
                return null;
            }

            if (!declined)
            {
                _checkouts[session.Checkout.Id] = session with { Checkout = session.Checkout with { State = CheckoutState.Paid, ProviderReference = reference } };
                return CheckoutState.Paid;
            }

            int declines = session.Declines + 1;
            Checkout checkout = declines >= MaxDeclines ? session.Checkout with { State = CheckoutState.Declined } : session.Checkout;
            _checkouts[checkout.Id] = session with { Checkout = checkout, Declines = declines };
            return CheckoutState.Declined;
        }
    }

    private void Update(string checkoutId, Func<Checkout, Checkout> change)
    {
        lock (_lock)
        {
            if (_checkouts.TryGetValue(checkoutId ?? "", out Session? found))
            {
                _checkouts[found.Checkout.Id] = found with { Checkout = change(found.Checkout) };
            }
        }
    }

    private void Purge()
    {
        DateTimeOffset now = clock.GetUtcNow();
        foreach (string expired in _checkouts.Where(c => c.Value.ExpiresAt <= now).Select(c => c.Key).ToList())
        {
            _checkouts.Remove(expired);
        }
    }

    private sealed record Session(Checkout Checkout, DateTimeOffset ExpiresAt, int Declines);
}

/// <summary>Sans prestataire de paiement (<c>Lettra:CustomerPortal:Payments=None</c>) : l'espace client propose le virement seulement.</summary>
internal sealed class NoOnlinePayment : IOnlinePaymentProvider
{
    public string Name => "";

    public bool IsAvailable => false;

    public Task<Result<Checkout>> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<Result<Checkout>>(PortalErrors.PaymentDeclined);

    public Task<Checkout?> FindAsync(string checkoutId, CancellationToken cancellationToken) => Task.FromResult<Checkout?>(null);

    public Task<bool> TryClaimAsync(string checkoutId, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task ReleaseAsync(string checkoutId, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Signature électronique simulée : une signature électronique simple (nom saisi, accord coché, horodatage, empreinte du
/// devis), référencée comme le ferait un prestataire. Aucune valeur de signature qualifiée eIDAS.
/// </summary>
public sealed class SimulatedSignatureProvider(TimeProvider clock) : IElectronicSignatureProvider
{
    public const string ProviderName = "Signature simulée";

    public Task<Result<SignatureEvidence>> SignAsync(SignatureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string reference = "sig_sim_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(10));
        return Task.FromResult<Result<SignatureEvidence>>(new SignatureEvidence(ProviderName, reference, clock.GetUtcNow()));
    }
}
