using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Outbox;
using Lettra.Contracts.Accounting;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Portal;
using Lettra.Contracts.Reminders;
using Lettra.Contracts.Tax;
using Lettra.Domain.Common;
using Lettra.Exports.Application.Accounting;
using Lettra.Exports.Application.AuditKit;
using Lettra.Exports.Application.Banking;
using Lettra.Exports.Application.Bridges;
using Lettra.Exports.Application.EInvoicing;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Application.EInvoicing.Reception;
using Lettra.Exports.Application.Fec;
using Lettra.Exports.Application.Insights;
using Lettra.Exports.Application.Portal;
using Lettra.Exports.Application.Reminders;
using Lettra.Exports.Application.Tax;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Exports.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Cas d'usage du module Exports : FEC, validateur intégré, factures électroniques (CII, UBL) et transmission à la
    /// plateforme agréée, consultation du journal des ventes, déclarations fiscales (l'adapter <see cref="IAccreditedPlatformClient"/> est choisi par l'infrastructure).
    /// </summary>
    public static IServiceCollection AddExportsApplication(this IServiceCollection services)
    {
        services.AddScoped<FecExportService>();
        services.AddScoped<IQueryHandler<ExportFecQuery, Result<FecFile>>, ExportFecHandler>();
        services.AddScoped<IQueryHandler<ExportEntriesForSoftwareQuery, Result<BridgeFile>>, ExportEntriesForSoftwareHandler>();
        services.AddScoped<IQueryHandler<ValidateFecQuery, Result<FecValidationResponse>>, ValidateFecHandler>();
        services.AddScoped<IQueryHandler<GetEInvoiceQuery, Result<EInvoiceFile>>, GetEInvoiceHandler>();
        services.AddScoped<ICommandHandler<TransmitInvoiceCommand, Result<PlatformTransmissionResponse>>, TransmitInvoiceHandler>();
        services.AddScoped<ICommandHandler<RefreshPlatformStatusCommand, Result<PlatformTransmissionResponse>>, RefreshPlatformStatusHandler>();
        services.AddScoped<ICommandHandler<ReportPaymentCommand, Result<PlatformTransmissionResponse>>, ReportPaymentHandler>();
        services.AddScoped<IQueryHandler<GetPlatformTransmissionQuery, Result<PlatformTransmissionResponse>>, GetPlatformTransmissionHandler>();
        services.TryAddSingleton<IEInvoicingDirectory, UnconfiguredDirectory>();
        services.AddScoped<IQueryHandler<GetReceivedInvoicesQuery, Result<ReceivedInvoicesResponse>>, GetReceivedInvoicesHandler>();
        services.AddScoped<IQueryHandler<GetReceivedInvoiceQuery, Result<ReceivedInvoiceResponse>>, GetReceivedInvoiceHandler>();
        services.AddScoped<IQueryHandler<DownloadReceivedInvoiceQuery, Result<EInvoiceFile>>, DownloadReceivedInvoiceHandler>();
        services.AddScoped<ICommandHandler<FetchReceivedInvoicesCommand, Result<FetchReceivedInvoicesResponse>>, FetchReceivedInvoicesHandler>();
        services.AddScoped<ICommandHandler<RespondToReceivedInvoiceCommand, Result<ReceivedInvoiceResponse>>, RespondToReceivedInvoiceHandler>();
        services.AddScoped<IQueryHandler<LookupDirectoryQuery, Result<DirectoryLookupResponse>>, LookupDirectoryHandler>();
        services.AddScoped<IQueryHandler<GetReceivablesSummaryQuery, Result<ReceivablesSummaryResponse>>, GetReceivablesSummaryHandler>();
        services.AddScoped<IQueryHandler<GetPaymentWeatherQuery, Result<PaymentWeatherResponse>>, GetPaymentWeatherHandler>();
        services.AddScoped<IQueryHandler<GetPracticeAuditQuery, Result<PracticeAuditResponse>>, GetPracticeAuditHandler>();
        services.AddScoped<IQueryHandler<RunPracticeAuditQuery, Result<PracticeAuditResponse>>, RunPracticeAuditHandler>();
        services.AddScoped<IQueryHandler<GetInvoiceProofQuery, Result<InvoiceProofResponse>>, GetInvoiceProofHandler>();
        services.AddScoped<IQueryHandler<VerifyInvoiceProofQuery, Result<InvoiceProofCheckResponse>>, VerifyInvoiceProofHandler>();
        services.AddScoped<IQueryHandler<ListInvoiceProofKeysQuery, Result<IReadOnlyList<InvoiceProofKeyResponse>>>, ListInvoiceProofKeysHandler>();
        services.AddScoped<IQueryHandler<GetDossierAsOfQuery, Result<DossierAsOfResponse>>, GetDossierAsOfHandler>();
        services.AddScoped<IQueryHandler<ExplainFigureQuery, Result<FigureExplanationResponse>>, ExplainFigureHandler>();
        services.AddScoped<IQueryHandler<GetCallSheetQuery, Result<CallSheetResponse>>, GetCallSheetHandler>();
        services.AddScoped<IQueryHandler<GetIntercompanyQuery, Result<IntercompanyResponse>>, IntercompanyHandler>();
        services.AddScoped<IQueryHandler<SimulateWhatIfQuery, Result<WhatIfResponse>>, SimulateWhatIfHandler>();
        services.AddScoped<IQueryHandler<GetEInvoicingReadinessQuery, Result<EInvoicingReadinessResponse>>, GetEInvoicingReadinessHandler>();
        services.AddScoped<IDossierIndicatorSource, EInvoicingReadinessIndicatorSource>();

        // Les mêmes éclairages pour l'assistant IA du poste (MCP), en lecture seule et avec les droits de l'utilisateur.
        services.AddScoped<IAiTool, PaymentWeatherTool>();
        services.AddScoped<IAiTool, OverdueTool>();
        services.AddScoped<IAiTool, MonthRevenueTool>();
        services.AddScoped<IAiTool, DossierAsOfTool>();
        services.AddScoped<IAiTool, WhatIfTool>();
        services.AddScoped<IAiTool, CallSheetTool>();

        // Consultation de la comptabilité : journal des ventes, balance et grand livre, lus comme le FEC.
        services.AddScoped<IQueryHandler<GetSalesJournalQuery, Result<SalesJournalResponse>>, GetSalesJournalHandler>();
        services.AddScoped<IQueryHandler<GetTrialBalanceQuery, Result<TrialBalanceResponse>>, GetTrialBalanceHandler>();
        services.AddScoped<IQueryHandler<GetAccountLedgerQuery, Result<AccountLedgerResponse>>, GetAccountLedgerHandler>();

        // Encaissements, rapprochement bancaire et lettrage (vague 6).
        services.AddScoped<ICommandHandler<RecordPaymentCommand, Result<PaymentResponse>>, RecordPaymentHandler>();
        services.AddScoped<IValidator<RecordPaymentCommand>, RecordPaymentValidator>();
        services.AddScoped<PaymentPlatformReporter>();
        services.AddScoped<ICommandHandler<ReversePaymentCommand, Result<PaymentResponse>>, ReversePaymentHandler>();
        services.AddScoped<IQueryHandler<GetPaymentQuery, Result<PaymentResponse>>, GetPaymentHandler>();
        services.AddScoped<IQueryHandler<ListPaymentsQuery, Result<PagedResponse<PaymentSummary>>>, ListPaymentsHandler>();
        services.AddScoped<IQueryHandler<GetOpenInvoicesQuery, Result<IReadOnlyList<OpenInvoiceResponse>>>, GetOpenInvoicesHandler>();
        services.AddScoped<ICommandHandler<ImportBankStatementCommand, Result<BankImportResponse>>, ImportBankStatementHandler>();
        services.AddScoped<ICommandHandler<ImportDemoBankStatementCommand, Result<BankImportResponse>>, ImportDemoBankStatementHandler>();
        services.AddScoped<IQueryHandler<GetReconciliationQuery, Result<ReconciliationResponse>>, GetReconciliationHandler>();
        services.AddScoped<ICommandHandler<ConfirmMatchesCommand, Result<ConfirmMatchesResponse>>, ConfirmMatchesHandler>();
        services.AddScoped<ICommandHandler<IgnoreBankLineCommand, Result<BankLineResponse>>, IgnoreBankLineHandler>();
        // Pièces justificatives des dépenses : réclamées au client par e-mail, déposées en photo depuis son téléphone.
        services.AddScoped<ReceiptRequestMailer>();
        services.AddScoped<IQueryHandler<GetReceiptsQuery, Result<ReceiptsResponse>>, GetReceiptsHandler>();
        services.AddScoped<IQueryHandler<GetRequestedReceiptsQuery, Result<ReceiptsResponse>>, GetRequestedReceiptsHandler>();
        services.AddScoped<ICommandHandler<RequestReceiptsCommand, Result<RequestReceiptsResponse>>, RequestReceiptsHandler>();
        services.AddScoped<ICommandHandler<SetReceiptNotNeededCommand, Result<ReceiptLineResponse>>, SetReceiptNotNeededHandler>();
        services.AddScoped<ICommandHandler<UploadReceiptCommand, Result<ReceiptLineResponse>>, UploadReceiptHandler>();
        services.AddScoped<IQueryHandler<GetReceiptFileQuery, Result<ReceiptFile>>, GetReceiptFileHandler>();
        services.AddScoped<IQueryHandler<GetBankingSettingsQuery, Result<BankingSettingsResponse>>, GetBankingSettingsHandler>();
        services.AddScoped<ICommandHandler<UpdateBankingSettingsCommand, Result<BankingSettingsResponse>>, UpdateBankingSettingsHandler>();
        services.AddScoped<IQueryHandler<GetBankReconciliationStatusQuery, Result<BankReconciliationStatusResponse>>, GetBankReconciliationStatusHandler>();
        services.AddScoped<IDossierIndicatorSource, BankUnreconciledIndicatorSource>();
        // Banques connectées : l'agrégateur (IBankAggregator) vient de l'infrastructure ; ses opérations passent par le
        // même import que les relevés.
        services.AddScoped<BankLineImporter>();
        services.AddScoped<BankConnectionSync>();
        services.AddScoped<IQueryHandler<GetBankConnectionsQuery, Result<BankConnectionsResponse>>, GetBankConnectionsHandler>();
        services.AddScoped<ICommandHandler<StartBankConnectionCommand, Result<BankConsentRedirectResponse>>, StartBankConnectionHandler>();
        services.AddScoped<ICommandHandler<CompleteBankConnectionCommand, Result<BankConnectionCompletedResponse>>, CompleteBankConnectionHandler>();
        services.AddScoped<ICommandHandler<SyncBankConnectionsCommand, Result<SyncBankConnectionsResponse>>, SyncBankConnectionsHandler>();
        services.AddScoped<ICommandHandler<RevokeBankConnectionCommand, Result<BankConnectionResponse>>, RevokeBankConnectionHandler>();
        // Déclarations fiscales : brouillons de CA3/CA12 et de DES, calendrier fiscal (la TVA sur les encaissements se
        // branche en remplaçant IVatOnReceiptsSource).
        services.TryAddScoped<IVatOnReceiptsSource, NoVatOnReceipts>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<TaxDeclarations>();
        services.AddScoped<IQueryHandler<GetVatReturnQuery, Result<VatReturnResponse>>, GetVatReturnHandler>();
        services.AddScoped<IQueryHandler<GetDesQuery, Result<DesResponse>>, GetDesHandler>();
        services.AddScoped<IQueryHandler<GetPaymentDelaysQuery, Result<PaymentDelaysResponse>>, GetPaymentDelaysHandler>();
        services.AddScoped<IQueryHandler<GetClosingReviewQuery, Result<ClosingReviewResponse>>, GetClosingReviewHandler>();
        services.AddScoped<IQueryHandler<ExportDesQuery, Result<DesFile>>, ExportDesHandler>();
        services.AddScoped<IQueryHandler<GetFiscalCalendarQuery, Result<FiscalCalendarResponse>>, GetFiscalCalendarHandler>();
        services.AddScoped<IQueryHandler<GetUpcomingFiscalDeadlinesQuery, Result<IReadOnlyList<FiscalDeadlineResponse>>>, GetUpcomingFiscalDeadlinesHandler>();
        services.AddScoped<IQueryHandler<GetFiscalSettingsQuery, Result<FiscalSettingsResponse>>, GetFiscalSettingsHandler>();
        services.AddScoped<ICommandHandler<UpdateFiscalSettingsCommand, Result<FiscalSettingsResponse>>, UpdateFiscalSettingsHandler>();
        services.AddScoped<ICommandHandler<MarkFiscalFilingCommand, Result<FiscalDeadlineResponse>>, MarkFiscalFilingHandler>();
        services.AddScoped<IDossierIndicatorSource, VatToDeclareIndicatorSource>();
        // Déclaration des honoraires (DAS 2) : bénéficiaires suivis, virements des relevés, échéance du calendrier fiscal.
        services.AddScoped<FeeReturns>();
        services.AddScoped<FeeReturnHandlers>();
        services.AddScoped<IQueryHandler<GetFeeReturnQuery, Result<FeeReturnResponse>>>(sp => sp.GetRequiredService<FeeReturnHandlers>());
        services.AddScoped<ICommandHandler<SaveFeeBeneficiaryCommand, Result>>(sp => sp.GetRequiredService<FeeReturnHandlers>());
        services.AddScoped<ICommandHandler<RemoveFeeBeneficiaryCommand, Result>>(sp => sp.GetRequiredService<FeeReturnHandlers>());
        services.AddScoped<IDossierIndicatorSource, FeeReturnIndicatorSource>();
        // Seuils qui approchent : franchise et régime de TVA, catégorie, attestation de vigilance, DAS 2 (catalogue daté).
        services.AddScoped<ThresholdWatch>();
        services.AddScoped<IQueryHandler<GetThresholdsQuery, Result<ThresholdsResponse>>, GetThresholdsHandler>();
        services.AddScoped<IDossierIndicatorSource, ThresholdIndicatorSource>();

        // Kit contrôle fiscal (vague 7) : FEC, attestation d'intégrité, piste d'audit fiable, XML et transmissions.
        services.AddScoped<AuditKitIntegrity>();
        services.AddScoped<AuditKitService>();
        services.AddScoped<IQueryHandler<GetAuditKitQuery, Result<AuditKitFile>>, GetAuditKitHandler>();
        // Relances des factures échues (vague 7) : scénario, textes FR/EN, envoi par le port e-mail et historique.
        services.AddScoped<PaymentReminderService>();
        services.AddScoped<IQueryHandler<GetReminderOverviewQuery, Result<ReminderOverviewResponse>>, GetReminderOverviewHandler>();
        services.AddScoped<IQueryHandler<GetReminderPreviewQuery, Result<ReminderPreviewResponse>>, GetReminderPreviewHandler>();
        services.AddScoped<ICommandHandler<SendReminderCommand, Result<PaymentReminderResponse>>, SendReminderHandler>();
        services.AddScoped<ICommandHandler<RunDueRemindersCommand, Result<RunRemindersResponse>>, RunDueRemindersHandler>();
        services.AddScoped<IQueryHandler<ListPaymentRemindersQuery, Result<PagedResponse<PaymentReminderResponse>>>, ListPaymentRemindersHandler>();
        services.AddScoped<IQueryHandler<GetReminderSettingsQuery, Result<ReminderSettingsResponse>>, GetReminderSettingsHandler>();
        services.AddScoped<ICommandHandler<UpdateReminderSettingsCommand, Result<ReminderSettingsResponse>>, UpdateReminderSettingsHandler>();

        // Espace client : liens envoyés au client, paiement en ligne et signature des devis. Fermé tant que l'hôte n'en
        // donne pas le compte de service (ICustomerPortalSettings).
        services.TryAddSingleton<ICustomerPortalSettings, ClosedCustomerPortal>();
        services.AddScoped<ICommandHandler<CreatePortalLinkCommand, Result<PortalLinkResponse>>, CreatePortalLinkHandler>();
        services.AddScoped<IQueryHandler<ListPortalLinksQuery, Result<IReadOnlyList<PortalLinkSummary>>>, ListPortalLinksHandler>();
        services.AddScoped<ICommandHandler<RevokePortalLinkCommand, Result>, RevokePortalLinkHandler>();
        services.AddScoped<IQueryHandler<GetQuoteSignatureQuery, Result<QuoteSignatureResponse>>, GetQuoteSignatureHandler>();
        services.AddScoped<ICommandHandler<OpenPortalCommand, Result<PortalView>>, OpenPortalHandler>();
        services.AddScoped<ICommandHandler<SignQuoteOnPortalCommand, Result<SignQuoteResult>>, SignQuoteOnPortalHandler>();
        services.AddScoped<ICommandHandler<StartOnlinePaymentCommand, Result<OnlineCheckoutResponse>>, StartOnlinePaymentHandler>();
        services.AddScoped<ICommandHandler<CompleteOnlinePaymentCommand, Result<OnlinePaymentResult>>, CompleteOnlinePaymentHandler>();

        // Envois à la plateforme par l'outbox du serveur (étape 5.4).
        services.AddScoped<IOutboxHandler, PlatformDepositHandler>();
        services.AddScoped<IOutboxHandler, PlatformPaymentHandler>();
        return services;
    }
}
