using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Features.Catalog;
using Lettra.Application.Features.Customers;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Suppliers;
using Lettra.Contracts.Sync;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Sync.Client;

/// <summary>
/// Sur un poste qui synchronise, chaque commande des tiers et du catalogue prépare son intention avant de s'exécuter ;
/// l'enregistrement l'écrit dans l'outbox avec ses écritures, dans la même transaction. Une commande refusée n'en laisse
/// aucune. Le comportement est le plus interne du pipeline : dossier, droits et validation sont déjà passés.
/// </summary>
internal sealed class SyncOutboxBehavior<TRequest, TResult>(
    SyncOutboxStaging staging,
    ICustomerReadStore customers,
    ISupplierReadStore suppliers,
    ICatalogItemReadStore items,
    TimeProvider clock) : IPipelineBehavior<TRequest, TResult>
    where TRequest : notnull
{
    public async Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        SyncIntent? intent = await TranslateAsync(request, cancellationToken).ConfigureAwait(false);
        if (intent is null)
        {
            return await next().ConfigureAwait(false);
        }

        staging.Stage(intent);
        try
        {
            TResult result = await next().ConfigureAwait(false);
            if (result is Result { IsFailure: true } or Result<CustomerId> { IsFailure: true } or Result<SupplierId> { IsFailure: true } or Result<CatalogItemId> { IsFailure: true })
            {
                staging.Discard();
            }

            return result;
        }
        catch
        {
            staging.Discard();
            throw;
        }
    }

    private async Task<SyncIntent?> TranslateAsync(TRequest request, CancellationToken ct)
    {
        Guid key = Guid.CreateVersion7();
        DateTimeOffset now = clock.GetUtcNow();
        switch (request)
        {
            case CreateCustomerCommand c:
                return new CreateCustomerIntent(key, Required(c.Id?.Value), now, c.Data.ToRequest());
            case UpdateCustomerCommand u:
                return await customers.GetAsync(u.CustomerId, ct).ConfigureAwait(false) is { } customer
                    ? new UpdateCustomerIntent(key, u.CustomerId.Value, now, customer.ToRequest(), u.Data.ToRequest())
                    : null;
            case CreateSupplierCommand c:
                return new CreateSupplierIntent(key, Required(c.Id?.Value), now, c.Data.ToRequest());
            case UpdateSupplierCommand u:
                return await suppliers.GetAsync(u.SupplierId, ct).ConfigureAwait(false) is { } supplier
                    ? new UpdateSupplierIntent(key, u.SupplierId.Value, now, supplier.ToRequest(), u.Data.ToRequest())
                    : null;
            case CreateCatalogItemCommand c:
                return new CreateCatalogItemIntent(key, Required(c.Id?.Value), now, c.Data.ToRequest());
            case UpdateCatalogItemCommand u:
                return await items.GetAsync(u.CatalogItemId, ct).ConfigureAwait(false) is { } item
                    ? new UpdateCatalogItemIntent(key, u.CatalogItemId.Value, now, item.ToRequest(), u.Data.ToRequest())
                    : null;
            case SetCatalogItemArchivedCommand a:
                return await items.GetAsync(a.CatalogItemId, ct).ConfigureAwait(false) is { } archived
                    ? new SetCatalogItemArchivedIntent(key, a.CatalogItemId.Value, now, archived.IsArchived, a.IsArchived)
                    : null;
            default:
                return null;
        }
    }

    /// <summary>Le serveur doit créer le même élément : l'identifiant est tiré avant la commande (mapping des contrats).</summary>
    private static Guid Required(Guid? id) =>
        id ?? throw new InvalidOperationException("Une création synchronisée porte son identifiant (voir ContractMapper.ToCommand).");
}

/// <summary>Le contrat de saisie d'un brouillon de domaine : la forme dans laquelle l'intention voyage.</summary>
internal static class DraftRequests
{
    public static CreateCustomerRequest ToRequest(this Customer.Draft draft) => new()
    {
        Kind = (CustomerKindDto)(int)draft.Kind,
        Name = draft.Name ?? string.Empty,
        Siren = draft.Siren,
        Siret = draft.Siret,
        VatNumber = draft.VatNumber,
        Email = draft.Email,
        AddressLine1 = draft.AddressLine1 ?? string.Empty,
        AddressLine2 = draft.AddressLine2,
        PostalCode = draft.PostalCode ?? string.Empty,
        City = draft.City ?? string.Empty,
        CountryCode = draft.CountryCode ?? string.Empty,
    };

    public static CreateSupplierRequest ToRequest(this Supplier.Draft draft) => new()
    {
        Name = draft.Name ?? string.Empty,
        Siren = draft.Siren,
        Siret = draft.Siret,
        VatNumber = draft.VatNumber,
        Email = draft.Email,
        Iban = draft.Iban,
        AddressLine1 = draft.AddressLine1 ?? string.Empty,
        AddressLine2 = draft.AddressLine2,
        PostalCode = draft.PostalCode ?? string.Empty,
        City = draft.City ?? string.Empty,
        CountryCode = draft.CountryCode ?? string.Empty,
    };

    public static SaveCatalogItemRequest ToRequest(this CatalogItem.Draft draft) => new()
    {
        Reference = draft.Reference ?? string.Empty,
        Designation = draft.Designation ?? string.Empty,
        Description = draft.Description,
        Kind = (CatalogItemKindDto)(int)draft.Kind,
        UnitCode = draft.UnitCode ?? string.Empty,
        UnitPriceExclTax = draft.UnitPriceExclTax,
        VatRateCode = draft.VatRateCode ?? string.Empty,
    };
}
