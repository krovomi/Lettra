using System.Text.Json;
using System.Text.Json.Serialization;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Common;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Identity;
using Lettra.Contracts.Policies;
using Lettra.Contracts.Presence;
using Lettra.Contracts.Directory;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Rights;
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
[JsonSerializable(typeof(SessionResponse))]
[JsonSerializable(typeof(CreateLocalAccountRequest))]
[JsonSerializable(typeof(UnlockLocalSessionRequest))]
[JsonSerializable(typeof(IssueRightsSnapshotRequest))]
[JsonSerializable(typeof(RightsSnapshotResponse))]
[JsonSerializable(typeof(RightsSnapshotKeysResponse))]
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
[JsonSerializable(typeof(CreateDossierRequest))]
[JsonSerializable(typeof(DossierSummary))]
[JsonSerializable(typeof(IReadOnlyList<DossierSummary>))]
[JsonSerializable(typeof(SetUserAccountRequest))]
[JsonSerializable(typeof(CreateGroupRequest))]
[JsonSerializable(typeof(GroupSummary))]
[JsonSerializable(typeof(AssignRoleRequest))]
[JsonSerializable(typeof(RoleAssignmentSummary))]
[JsonSerializable(typeof(AskAssistantRequest))]
[JsonSerializable(typeof(AssistantEvent))]
[JsonSerializable(typeof(IReadOnlyList<AssistantJournalEntry>))]
[JsonSerializable(typeof(SegregationOfDutiesSettings))]
[JsonSerializable(typeof(DocumentPresence))]
public sealed partial class ContractsJsonContext : JsonSerializerContext;
