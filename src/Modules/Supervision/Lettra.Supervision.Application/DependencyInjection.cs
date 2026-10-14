using Lettra.Application.Abstractions.Messaging;
using Lettra.Domain.Common;
using Lettra.Supervision.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Supervision.Application;

public static class DependencyInjection
{
    /// <summary>Cas d'usage, ingestion, routage et escalade du module Supervision.</summary>
    public static IServiceCollection AddSupervisionApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(BusinessCalendar.Paris);
        services.AddSingleton<EscalationPolicy>();
        services.AddScoped<RoutingPolicy>();
        services.AddScoped<AlertIngestion>();
        services.AddScoped<EscalationRun>();

        services.AddScoped<ICommandHandler<AcknowledgeAlertCommand, Result>, AlertCommandHandlers>();
        services.AddScoped<ICommandHandler<ChangeAlertSeverityCommand, Result>, AlertCommandHandlers>();
        services.AddScoped<ICommandHandler<ResolveAlertCommand, Result>, AlertCommandHandlers>();
        return services;
    }
}
