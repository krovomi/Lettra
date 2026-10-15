using System.Text.Json;
using System.Text.Json.Serialization;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Identity;
using Lettra.Contracts.Directory;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Pricing;
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
[JsonSerializable(typeof(CreateSupplierRequest))]
[JsonSerializable(typeof(SupplierResponse))]
[JsonSerializable(typeof(PagedResponse<SupplierSummary>))]
[JsonSerializable(typeof(SaveCatalogItemRequest))]
[JsonSerializable(typeof(SetCatalogItemArchivedRequest))]
[JsonSerializable(typeof(CatalogItemResponse))]
[JsonSerializable(typeof(PagedResponse<CatalogItemSummary>))]
[JsonSerializable(typeof(CatalogReferenceData))]
[JsonSerializable(typeof(BillingReferenceData))]
[JsonSerializable(typeof(SaveQuoteRequest))]
[JsonSerializable(typeof(QuoteResponse))]
[JsonSerializable(typeof(PagedResponse<QuoteSummary>))]
[JsonSerializable(typeof(SetQuoteOutcomeRequest))]
[JsonSerializable(typeof(CreateDownPaymentRequest))]
[JsonSerializable(typeof(SaveInvoiceRequest))]
[JsonSerializable(typeof(InvoiceResponse))]
[JsonSerializable(typeof(PagedResponse<InvoiceSummary>))]
[JsonSerializable(typeof(CreateCreditNoteRequest))]
[JsonSerializable(typeof(ComplianceReportResponse))]
[JsonSerializable(typeof(SaveCompanyProfileRequest))]
[JsonSerializable(typeof(CompanyProfileResponse))]
[JsonSerializable(typeof(SavePriceListRequest))]
[JsonSerializable(typeof(PriceListResponse))]
[JsonSerializable(typeof(PagedResponse<PriceListSummary>))]
[JsonSerializable(typeof(IReadOnlyList<ResolvedPrice>))]
[JsonSerializable(typeof(CompanySearchResponse))]
[JsonSerializable(typeof(VatNumberCheckResponse))]
[JsonSerializable(typeof(CreateDossierRequest))]
[JsonSerializable(typeof(DossierSummary))]
[JsonSerializable(typeof(AskAssistantRequest))]
[JsonSerializable(typeof(AssistantEvent))]
[JsonSerializable(typeof(IReadOnlyList<AssistantJournalEntry>))]
public sealed partial class ContractsJsonContext : JsonSerializerContext;
