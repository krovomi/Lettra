using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Supervision.Application;

public static class DependencyInjection
{
    /// <summary>Point d'accroche des cas d'usage du module Supervision.</summary>
    public static IServiceCollection AddSupervisionApplication(this IServiceCollection services) => services;
}
