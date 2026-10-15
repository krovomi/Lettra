using Lettra.Application.Features.Customers;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Mapping;

/// <summary>
/// Passage Contracts ↔ Application, utilisé à l'identique par l'API et par la gateway in-process
/// (ADR-0005) : les deux chemins produisent exactement la même commande.
/// </summary>
public static class ContractMapper
{
    // Noms de champs du domaine → noms de propriétés du contrat, pour que l'écran place l'erreur sous le bon champ.
    private static readonly Dictionary<string, string> TargetsToContract = new(StringComparer.Ordinal)
    {
        [nameof(PostalAddress.Line1)] = nameof(CreateCustomerRequest.AddressLine1),
        [nameof(PostalAddress.Line2)] = nameof(CreateCustomerRequest.AddressLine2),
        [nameof(PostalAddress.Country)] = nameof(CreateCustomerRequest.CountryCode),
        ["VatRate"] = "VatRateCode",
        ["Unit"] = "UnitCode",
    };

    public static CreateCustomerCommand ToCommand(this CreateCustomerRequest request, DossierId dossier) =>
        new(dossier, request.ToDraft());

    /// <summary>Modification : même contrat qu'à la création, plus la version lue (<c>If-Match</c>).</summary>
    public static UpdateCustomerCommand ToUpdateCommand(this CreateCustomerRequest request, DossierId dossier, CustomerId id, long? expectedVersion) =>
        new(dossier, id, request.ToDraft(), expectedVersion);

    public static Customer.Draft ToDraft(this CreateCustomerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new Customer.Draft(
                (CustomerKind)(int)request.Kind,
                request.Name,
                request.Siren,
                request.Siret,
                request.VatNumber,
                request.Email,
                request.AddressLine1,
                request.AddressLine2,
                request.PostalCode,
                request.City,
                request.CountryCode);
    }

    public static CustomerResponse ToResponse(this Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        return new CustomerResponse(
            customer.Id.Value,
            (CustomerKindDto)(int)customer.Kind,
            customer.Name,
            customer.Siren?.Value,
            customer.Siret?.Value,
            customer.VatNumber?.Value,
            customer.Email?.Value,
            customer.BillingAddress.Line1,
            customer.BillingAddress.Line2,
            customer.BillingAddress.PostalCode,
            customer.BillingAddress.City,
            customer.BillingAddress.Country.Value,
            customer.CreatedAt)
        {
            Version = customer.Version,
        };
    }

    public static IReadOnlyList<ErrorDetail> ToErrorDetails(this IEnumerable<Error> errors) =>
        errors.Select(e => new ErrorDetail(e.Code, MapTarget(e.Target))).ToList();

    public static OperationResult<T> ToOperationResult<T>(this Result<T> result) =>
        result.IsSuccess ? OperationResult<T>.Success(result.Value) : OperationResult<T>.Failure(result.Errors.ToErrorDetails());

    private static string? MapTarget(string? target) =>
        target is not null && TargetsToContract.TryGetValue(target, out string? mapped) ? mapped : target;
}
