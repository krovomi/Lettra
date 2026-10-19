using System.Text.Json;
using System.Text.Json.Serialization;
using Lettra.Contracts.Accounting;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Alerts;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Reminders;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Common;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Demo;
using Lettra.Contracts.Identity;
using Lettra.Contracts.Integrity;
using Lettra.Contracts.Policies;
using Lettra.Contracts.Presence;
using Lettra.Contracts.Directory;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Pricing;
using Lettra.Contracts.Rights;
using Lettra.Contracts.Speech;
using Lettra.Contracts.Suppliers;
using Lettra.Contracts.Sync;
using Lettra.Contracts.Tax;
using Lettra.Contracts.Exports;

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
[JsonSerializable(typeof(ServerDiscoveryResponse))]
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
[JsonSerializable(typeof(SaveRecurringInvoiceRequest))]
[JsonSerializable(typeof(SetRecurringInvoiceActiveRequest))]
[JsonSerializable(typeof(RecurringInvoiceResponse))]
[JsonSerializable(typeof(PagedResponse<RecurringInvoiceSummary>))]
[JsonSerializable(typeof(RecurringRunResponse))]
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
[JsonSerializable(typeof(IReadOnlyList<DossierSummary>))]
[JsonSerializable(typeof(CabinetOverviewResponse))]
[JsonSerializable(typeof(CabinetOverviewRefreshResponse))]
[JsonSerializable(typeof(DossierReviewResponse))]
[JsonSerializable(typeof(SetDossierReviewItemRequest))]
[JsonSerializable(typeof(SetUserAccountRequest))]
[JsonSerializable(typeof(CreateGroupRequest))]
[JsonSerializable(typeof(GroupSummary))]
[JsonSerializable(typeof(AssignRoleRequest))]
[JsonSerializable(typeof(RoleAssignmentSummary))]
[JsonSerializable(typeof(AdministrationOverview))]
[JsonSerializable(typeof(MyAccessResponse))]
[JsonSerializable(typeof(InvitePersonRequest))]
[JsonSerializable(typeof(UpdatePersonRequest))]
[JsonSerializable(typeof(RenameTeamRequest))]
[JsonSerializable(typeof(SaveCustomRoleRequest))]
[JsonSerializable(typeof(PersonSummary))]
[JsonSerializable(typeof(RoleDescription))]
[JsonSerializable(typeof(AskAssistantRequest))]
[JsonSerializable(typeof(AssistantEvent))]
[JsonSerializable(typeof(AssistantStatusResponse))]
[JsonSerializable(typeof(IReadOnlyList<AssistantJournalEntry>))]
[JsonSerializable(typeof(IntegrityReportResponse))]
[JsonSerializable(typeof(FecValidationResponse))]
[JsonSerializable(typeof(AuditKitManifestResponse))]
[JsonSerializable(typeof(AuditKitAttestationResponse))]
[JsonSerializable(typeof(SalesJournalResponse))]
[JsonSerializable(typeof(TrialBalanceResponse))]
[JsonSerializable(typeof(AccountLedgerResponse))]
[JsonSerializable(typeof(TransmitInvoiceRequest))]
[JsonSerializable(typeof(ReportPaymentRequest))]
[JsonSerializable(typeof(PlatformTransmissionResponse))]
[JsonSerializable(typeof(IReadOnlyList<ClosingSummary>))]
[JsonSerializable(typeof(IReadOnlyList<IntegrityKeySummary>))]
[JsonSerializable(typeof(AlertCentreResponse))]
[JsonSerializable(typeof(AlertDetailResponse))]
[JsonSerializable(typeof(ChangeAlertSeverityRequest))]
[JsonSerializable(typeof(SaveAiSettingsRequest))]
[JsonSerializable(typeof(AiSettingsResponse))]
[JsonSerializable(typeof(SegregationOfDutiesSettings))]
[JsonSerializable(typeof(DocumentPresence))]
[JsonSerializable(typeof(SyncIntent))]
[JsonSerializable(typeof(PushSyncIntentsRequest))]
[JsonSerializable(typeof(PushSyncIntentsResponse))]
[JsonSerializable(typeof(SyncChangesResponse))]
[JsonSerializable(typeof(OpenDeviceSeriesRequest))]
[JsonSerializable(typeof(OpenDeviceSeriesResponse))]
[JsonSerializable(typeof(SyncPolicyResponse))]
[JsonSerializable(typeof(ReleaseDeviceSeriesResponse))]
[JsonSerializable(typeof(SaveSyncPolicyRequest))]
[JsonSerializable(typeof(SyncDeviceResponse))]
[JsonSerializable(typeof(IReadOnlyList<SyncDeviceResponse>))]
[JsonSerializable(typeof(ReceivablesSummaryResponse))]
[JsonSerializable(typeof(DemoDataStatusResponse))]
[JsonSerializable(typeof(DemoSeedResponse))]
[JsonSerializable(typeof(SpeechStatusResponse))]
[JsonSerializable(typeof(TranscriptionResponse))]
[JsonSerializable(typeof(RecordPaymentRequest))]
[JsonSerializable(typeof(ReversePaymentRequest))]
[JsonSerializable(typeof(PaymentResponse))]
[JsonSerializable(typeof(PagedResponse<PaymentSummary>))]
[JsonSerializable(typeof(IReadOnlyList<OpenInvoiceResponse>))]
[JsonSerializable(typeof(ImportBankStatementRequest))]
[JsonSerializable(typeof(BankImportResponse))]
[JsonSerializable(typeof(ReconciliationResponse))]
[JsonSerializable(typeof(ConfirmMatchesRequest))]
[JsonSerializable(typeof(ConfirmMatchesResponse))]
[JsonSerializable(typeof(IgnoreBankLineRequest))]
[JsonSerializable(typeof(BankLineResponse))]
[JsonSerializable(typeof(BankingSettingsResponse))]
[JsonSerializable(typeof(UpdateBankingSettingsRequest))]
[JsonSerializable(typeof(BankReconciliationStatusResponse))]
[JsonSerializable(typeof(VatReturnResponse))]
[JsonSerializable(typeof(DesResponse))]
[JsonSerializable(typeof(FiscalCalendarResponse))]
[JsonSerializable(typeof(FiscalSettingsResponse))]
[JsonSerializable(typeof(UpdateFiscalSettingsRequest))]
[JsonSerializable(typeof(MarkFiscalFilingRequest))]
[JsonSerializable(typeof(FiscalDeadlineResponse))]
[JsonSerializable(typeof(IReadOnlyList<FiscalDeadlineResponse>))]
[JsonSerializable(typeof(ReminderSettingsResponse))]
[JsonSerializable(typeof(UpdateReminderSettingsRequest))]
[JsonSerializable(typeof(ReminderOverviewResponse))]
[JsonSerializable(typeof(ReminderPreviewResponse))]
[JsonSerializable(typeof(SendReminderRequest))]
[JsonSerializable(typeof(PaymentReminderResponse))]
[JsonSerializable(typeof(PagedResponse<PaymentReminderResponse>))]
[JsonSerializable(typeof(RunRemindersResponse))]
[JsonSerializable(typeof(InvitationEmailResponse))]
public sealed partial class ContractsJsonContext : JsonSerializerContext;
