using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Exports;
using Lettra.Domain.Common;
using Lettra.Exports.Application.Fec;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Exports.Application;

public static class DependencyInjection
{
    /// <summary>Cas d'usage du module Exports : FEC et validateur intégré.</summary>
    public static IServiceCollection AddExportsApplication(this IServiceCollection services)
    {
        services.AddScoped<FecExportService>();
        services.AddScoped<IQueryHandler<ExportFecQuery, Result<FecFile>>, ExportFecHandler>();
        services.AddScoped<IQueryHandler<ValidateFecQuery, Result<FecValidationResponse>>, ValidateFecHandler>();
        return services;
    }
}
