using Lettra.Application.Abstractions.Identity;
using Lettra.Contracts.Common;
using Lettra.Contracts.Demo;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lettra.DemoData;

/// <summary>
/// Le bouton « Charger les données de démonstration » sur le poste (MAUI, tout hors ligne) : charge le jeu dans le dossier
/// ouvert, au nom du compte ouvert, sans créer d'autre dossier. Mêmes garde-fous qu'au démarrage.
/// </summary>
internal sealed class InProcessDemoDataGateway(
    IConfiguration configuration, IServiceScopeFactory scopes, DemoDataHost host, ILoggerFactory? loggerFactory = null) : IDemoDataGateway
{
    public Task<OperationResult<DemoDataStatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<DemoDataStatusResponse>.Success(
            new DemoDataStatusResponse(LettraDemoData.IsAvailable(configuration, host.EnvironmentName))));

    public async Task<OperationResult<DemoSeedResponse>> SeedAsync(CancellationToken cancellationToken)
    {
        Guid? user;
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            ICurrentUser current = scope.ServiceProvider.GetRequiredService<ICurrentUser>();
            user = current.IsAuthenticated ? current.UserId : null;
        }

        DemoSeedResult result = await LettraDemoData.SeedAsync(
            configuration, host.EnvironmentName, new DemoSeedTarget(AdditionalDossiers: 0), host.HostServices, user, loggerFactory, cancellationToken)
            .ConfigureAwait(false);
        return OperationResult<DemoSeedResponse>.Success(result.ToResponse());
    }
}

/// <summary>Ce que l'hôte du poste fournit au chargement : son environnement et ses services propres (coffre de clés de l'OS).</summary>
internal sealed record DemoDataHost(string EnvironmentName, Action<IServiceCollection>? HostServices);

public static class DemoDataGatewayRegistration
{
    /// <summary>
    /// Poste de démonstration : enregistre le chargement du jeu depuis l'accueil (à appeler après
    /// <c>AddInProcessGateways</c>). Sans <c>Lettra:Demo:Seed=true</c>, ou en Production, l'accueil n'affiche pas le bouton.
    /// </summary>
    public static IServiceCollection AddDemoDataGateway(
        this IServiceCollection services, string environmentName, Action<IServiceCollection>? hostServices = null)
    {
        services.AddSingleton(new DemoDataHost(environmentName, hostServices));
        services.AddSingleton<IDemoDataGateway, InProcessDemoDataGateway>();
        return services;
    }

    /// <summary>Résumé d'un chargement pour l'écran.</summary>
    public static DemoSeedResponse ToResponse(this DemoSeedResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new DemoSeedResponse(
            (DemoSeedStatusDto)(int)result.Status,
            result.Dossiers.Count,
            result.Dossiers.Sum(d => d.Customers),
            result.Dossiers.Sum(d => d.Quotes),
            result.Dossiers.Sum(d => d.Invoices));
    }
}
