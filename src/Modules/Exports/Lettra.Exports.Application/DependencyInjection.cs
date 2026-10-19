using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Outbox;
using Lettra.Contracts.Accounting;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Domain.Common;
using Lettra.Exports.Application.Accounting;
using Lettra.Exports.Application.Banking;
using Lettra.Exports.Application.EInvoicing;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Application.Fec;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Exports.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Cas d'usage du module Exports : FEC, validateur intégré, factures électroniques (CII, UBL) et transmission à la
    /// plateforme agréée, consultation du journal des ventes (l'adapter <see cref="IAccreditedPlatformClient"/> est choisi par l'infrastructure).
    /// </summary>
    public static IServiceCollection AddExportsApplication(this IServiceCollection services)
    {
        services.AddScoped<FecExportService>();
        services.AddScoped<IQueryHandler<ExportFecQuery, Result<FecFile>>, ExportFecHandler>();
        services.AddScoped<IQueryHandler<ValidateFecQuery, Result<FecValidationResponse>>, ValidateFecHandler>();
        services.AddScoped<IQueryHandler<GetEInvoiceQuery, Result<EInvoiceFile>>, GetEInvoiceHandler>();
        services.AddScoped<ICommandHandler<TransmitInvoiceCommand, Result<PlatformTransmissionResponse>>, TransmitInvoiceHandler>();
        services.AddScoped<ICommandHandler<RefreshPlatformStatusCommand, Result<PlatformTransmissionResponse>>, RefreshPlatformStatusHandler>();
        services.AddScoped<ICommandHandler<ReportPaymentCommand, Result<PlatformTransmissionResponse>>, ReportPaymentHandler>();
        services.AddScoped<IQueryHandler<GetPlatformTransmissionQuery, Result<PlatformTransmissionResponse>>, GetPlatformTransmissionHandler>();
        services.AddScoped<IQueryHandler<GetReceivablesSummaryQuery, Result<ReceivablesSummaryResponse>>, GetReceivablesSummaryHandler>();

        // Consultation de la comptabilité : journal des ventes, balance et grand livre, lus comme le FEC.
        services.AddScoped<IQueryHandler<GetSalesJournalQuery, Result<SalesJournalResponse>>, GetSalesJournalHandler>();
        services.AddScoped<IQueryHandler<GetTrialBalanceQuery, Result<TrialBalanceResponse>>, GetTrialBalanceHandler>();
        services.AddScoped<IQueryHandler<GetAccountLedgerQuery, Result<AccountLedgerResponse>>, GetAccountLedgerHandler>();

        // Encaissements, rapprochement bancaire et lettrage (vague 6).
        services.AddScoped<ICommandHandler<RecordPaymentCommand, Result<PaymentResponse>>, RecordPaymentHandler>();
        services.AddScoped<ICommandHandler<ReversePaymentCommand, Result<PaymentResponse>>, ReversePaymentHandler>();
        services.AddScoped<IQueryHandler<GetPaymentQuery, Result<PaymentResponse>>, GetPaymentHandler>();
        services.AddScoped<IQueryHandler<ListPaymentsQuery, Result<PagedResponse<PaymentSummary>>>, ListPaymentsHandler>();
        services.AddScoped<IQueryHandler<GetOpenInvoicesQuery, Result<IReadOnlyList<OpenInvoiceResponse>>>, GetOpenInvoicesHandler>();
        services.AddScoped<ICommandHandler<ImportBankStatementCommand, Result<BankImportResponse>>, ImportBankStatementHandler>();
        services.AddScoped<ICommandHandler<ImportDemoBankStatementCommand, Result<BankImportResponse>>, ImportDemoBankStatementHandler>();
        services.AddScoped<IQueryHandler<GetReconciliationQuery, Result<ReconciliationResponse>>, GetReconciliationHandler>();
        services.AddScoped<ICommandHandler<ConfirmMatchesCommand, Result<ConfirmMatchesResponse>>, ConfirmMatchesHandler>();
        services.AddScoped<ICommandHandler<IgnoreBankLineCommand, Result<BankLineResponse>>, IgnoreBankLineHandler>();
        services.AddScoped<IQueryHandler<GetBankingSettingsQuery, Result<BankingSettingsResponse>>, GetBankingSettingsHandler>();
        services.AddScoped<ICommandHandler<UpdateBankingSettingsCommand, Result<BankingSettingsResponse>>, UpdateBankingSettingsHandler>();
        services.AddScoped<IQueryHandler<GetBankReconciliationStatusQuery, Result<BankReconciliationStatusResponse>>, GetBankReconciliationStatusHandler>();

        // Envois à la plateforme par l'outbox du serveur (étape 5.4).
        services.AddScoped<IOutboxHandler, PlatformDepositHandler>();
        services.AddScoped<IOutboxHandler, PlatformPaymentHandler>();
        return services;
    }
}
