using Lettra.Application.Abstractions.Mail;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Insights;
using Lettra.Domain.Common;
using Lettra.Domain.Portal;
using Lettra.Domain.Reminders;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Insights;
using Lettra.Exports.Application.Portal;

namespace Lettra.Exports.Application.Reminders;

/// <summary>
/// Qui reçoit le rappel avant échéance : les clients dont la météo des paiements annonce pluie ou orage, sauf ceux qui
/// s'y opposent. La météo est une statistique simple et déterministe (aucune IA), la même pour tous ; ce ciblage
/// selon le comportement de paiement est un profilage, dit dans la mention d'information, avec droit d'opposition.
/// </summary>
public sealed record TargetingSnapshot(IReadOnlyDictionary<Guid, CustomerPaymentWeatherResponse> Weather, IReadOnlySet<CustomerId> Objections)
{
    public static TargetingSnapshot Empty { get; } = new(new Dictionary<Guid, CustomerPaymentWeatherResponse>(), new HashSet<CustomerId>());

    public CustomerPaymentWeatherResponse? WeatherOf(CustomerId customer) => Weather.GetValueOrDefault(customer.Value);

    /// <summary>Pluie ou orage, sans opposition.</summary>
    public bool Targets(CustomerId customer) =>
        !Objections.Contains(customer) && WeatherOf(customer)?.Weather is PaymentWeatherDto.Rainy or PaymentWeatherDto.Stormy;
}

/// <summary>Lit la météo des paiements de chaque client et les oppositions au ciblage.</summary>
public sealed class ReminderTargeting(IPaymentHistoryReader history, IPaymentTargetingObjectionRepository objections)
{
    public async Task<TargetingSnapshot> ReadAsync(DateOnly today, CancellationToken cancellationToken)
    {
        PaymentWeatherResponse weather = PaymentWeatherForecast.Compute(await history.ReadAsync(cancellationToken).ConfigureAwait(false), today);
        IReadOnlyList<PaymentTargetingObjection> objected = await objections.ListAsync(cancellationToken).ConfigureAwait(false);
        return new TargetingSnapshot(weather.Customers.ToDictionary(c => c.CustomerId), objected.Select(o => o.CustomerId).ToHashSet());
    }
}

/// <summary>
/// Ce qui part avec un rappel « Avant le retard » : un lien d'espace client propre à ce client (valable 30 jours, créé
/// à l'envoi seulement, enregistré avec la relance) et le QR code de virement. Le lien n'existe que sur un serveur où
/// l'espace client est ouvert et dont l'adresse publique est connue.
/// </summary>
public sealed class ReminderPaymentAids(
    ICustomerPortalSettings portal,
    IPortalLinkRepository links,
    ITenantContext tenant,
    LettraPublicAddress address,
    IPaymentQrCodeImage qr,
    TimeProvider clock)
{
    public bool OffersPortal => portal.IsEnabled && address.Url is not null && tenant.IsResolved;

    /// <summary>Crée le lien d'espace client du client (ajouté au suivi : la relance l'enregistre avec elle).</summary>
    public async Task<Uri?> PortalLinkAsync(CustomerId customer, Guid userId, CancellationToken cancellationToken)
    {
        if (!OffersPortal)
        {
            return null;
        }

        (string token, string hash) = PortalToken.Create(tenant.Current.DossierId);
        Result<PortalLink> link = PortalLink.Create(customer, hash, null, null, PortalLink.DefaultValidityDays, userId, clock.GetUtcNow());
        await links.AddAsync(link.Value, cancellationToken).ConfigureAwait(false);
        return new Uri(address.Url!, CreatePortalLinkHandler.PathPrefix + token);
    }

    /// <summary>Le QR code de virement en pièce jointe, ou rien si le virement ne peut pas être prérempli.</summary>
    public EmailAttachment? QrCode(ReminderSender sender, decimal amount, string invoiceNumber)
    {
        ArgumentNullException.ThrowIfNull(sender);
        byte[]? png = qr.Png(sender.Name, sender.Iban, sender.Bic, amount, invoiceNumber);
        return png is null ? null : new EmailAttachment($"virement-{invoiceNumber}.png", "image/png", png);
    }
}
