using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Outbox;
using Lettra.Contracts.Exports;
using Lettra.Domain.Common;
using Lettra.Exports.Application.EInvoicing;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Application.Fec;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Exports.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Cas d'usage du module Exports : FEC, validateur intégré, factures électroniques (CII, UBL) et transmission à la
    /// plateforme agréée (l'adapter <see cref="IAccreditedPlatformClient"/> est choisi par l'infrastructure).
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

        // Envois à la plateforme par l'outbox du serveur (étape 5.4).
        services.AddScoped<IOutboxHandler, PlatformDepositHandler>();
        services.AddScoped<IOutboxHandler, PlatformPaymentHandler>();
        return services;
    }
}
