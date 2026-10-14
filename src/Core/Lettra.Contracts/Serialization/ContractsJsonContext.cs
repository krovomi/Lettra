using System.Text.Json;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Directory;
using Lettra.Contracts.Suppliers;

namespace Lettra.Contracts.Serialization;

/// <summary>
/// Sérialisation générée à la compilation : indispensable au trimming de l'application WASM,
/// et partagée par l'API pour garantir le même format des deux côtés.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(CreateCustomerRequest))]
[JsonSerializable(typeof(CustomerResponse))]
[JsonSerializable(typeof(PagedResponse<CustomerSummary>))]
[JsonSerializable(typeof(ProblemResponse))]
[JsonSerializable(typeof(CreateSupplierRequest))]
[JsonSerializable(typeof(SupplierResponse))]
[JsonSerializable(typeof(PagedResponse<SupplierSummary>))]
[JsonSerializable(typeof(SaveCatalogItemRequest))]
[JsonSerializable(typeof(SetCatalogItemArchivedRequest))]
[JsonSerializable(typeof(CatalogItemResponse))]
[JsonSerializable(typeof(PagedResponse<CatalogItemSummary>))]
[JsonSerializable(typeof(CatalogReferenceData))]
[JsonSerializable(typeof(CompanySearchResponse))]
[JsonSerializable(typeof(VatNumberCheckResponse))]
public sealed partial class ContractsJsonContext : JsonSerializerContext;
