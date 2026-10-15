using Lettra.Application.Abstractions.Persistence;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Billing;

/// <summary>Date du jour des pièces : la date légale est celle de Paris, pas celle du serveur (UTC).</summary>
internal static class BillingCalendar
{
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Paris).DateTime);
}

/// <summary>Chargement de la copie du client et des conditions de l'émetteur, communs aux devis et factures.</summary>
internal static class BillingLoads
{
    /// <summary>La fiche client courante figée en copie ; erreur si le client n'existe pas.</summary>
    public static async Task<Result<PartySnapshot>> CustomerSnapshotAsync(
        ICustomerRepository customers, Guid customerId, CancellationToken cancellationToken)
    {
        if (customerId == Guid.Empty)
        {
            return BillingErrors.CustomerRequired;
        }

        Customer? customer = await customers.FindAsync(new CustomerId(customerId), cancellationToken).ConfigureAwait(false);
        return customer is null ? BillingErrors.CustomerRequired : PartySnapshot.Of(customer);
    }

    /// <summary>La copie à jour du client, ou la copie déjà portée par la pièce si la fiche a disparu.</summary>
    public static async Task<PartySnapshot> RefreshedCustomerAsync<TId>(
        ICustomerRepository customers, BillingDocument<TId> document, CancellationToken cancellationToken)
        where TId : struct, IEquatable<TId>
    {
        Customer? customer = await customers.FindAsync(document.CustomerId, cancellationToken).ConfigureAwait(false);
        return customer is null ? document.Customer : PartySnapshot.Of(customer);
    }

    public static async Task<SellerTerms?> SellerTermsAsync(ICompanyProfileRepository profiles, CancellationToken cancellationToken)
    {
        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        return profile?.ToSellerTerms();
    }
}
