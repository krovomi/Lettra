using Lettra.UI.Abstractions;
using Lettra.UI.Gateway.Http.Device;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.UI.Gateway.Http;

public static class SharedServerDependencyInjection
{
    /// <summary>Vérification de l'adresse saisie sur l'écran « Serveur du cabinet » (sans connexion).</summary>
    public static IServiceCollection AddSharedServerProbe(this IServiceCollection services)
    {
        services.AddHttpClient<SharedServerProbe>(client => client.Timeout = SharedServerProbe.Timeout + TimeSpan.FromSeconds(5));
        return services;
    }

    /// <summary>
    /// Application installée reliée au serveur commun du cabinet : les mêmes gateways HTTP que le navigateur, sur la même
    /// API et le même dossier, authentifiées par le jeton du poste (PKCE). L'hôte fournit <see cref="IAuthorizationBrowser"/>
    /// et <see cref="ITokenVault"/>, et garde le dossier choisi (<paramref name="rememberedDossier"/>,
    /// <paramref name="rememberDossier"/>).
    /// </summary>
    public static IServiceCollection AddSharedServerGateways(
        this IServiceCollection services,
        SharedServerSettings server,
        Guid? rememberedDossier,
        Action<Guid?> rememberDossier)
    {
        ArgumentNullException.ThrowIfNull(server);
        SharedServerDossierSelection selection = new(rememberedDossier, rememberDossier);
        services.AddSingleton(selection);
        services.Replace(ServiceDescriptor.Singleton<IDossierSelection>(selection));
        services.AddDeviceHttpGateways(server.SignIn());
        services.AddHttpClient<SharedServerDossierChooser>(client => client.BaseAddress = server.Address)
            .AddHttpMessageHandler<BearerTokenHandler>();
        services.AddSingleton<SharedServerSessionGateway>();
        services.Replace(ServiceDescriptor.Singleton<ISessionGateway>(sp => sp.GetRequiredService<SharedServerSessionGateway>()));
        services.Replace(ServiceDescriptor.Singleton<IInteractiveSignIn>(sp => sp.GetRequiredService<SharedServerSessionGateway>()));
        return services.AddSharedServerProbe();
    }
}
