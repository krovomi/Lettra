using System.Text.Json;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;

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
public sealed partial class ContractsJsonContext : JsonSerializerContext;
