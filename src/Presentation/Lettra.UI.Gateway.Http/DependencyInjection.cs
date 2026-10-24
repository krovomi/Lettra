using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.UI.Gateway.Http;

public static class DependencyInjection
{
    public static IServiceCollection AddHttpGateways(this IServiceCollection services, Uri apiBaseAddress)
    {
        // Navigateur : le dossier de la configuration n'est que celui du démarrage, le sélecteur de dossier en change.
        if (services.LastOrDefault(d => d.ServiceType == typeof(IDossierSelection))?.ImplementationInstance is FixedDossierSelection configured)
        {
            services.Replace(ServiceDescriptor.Singleton<IDossierSelection>(new SwitchableDossierSelection(configured.CurrentDossierId)));
        }

        services.AddTransient<CsrfHeaderHandler>();
        services.AddTransient<OfflineShortCircuitHandler>();
        services.AddHttpClient<ISessionGateway, HttpSessionGateway>(client => client.BaseAddress = apiBaseAddress)
            .AddHttpMessageHandler<OfflineShortCircuitHandler>()
            .AddHttpMessageHandler<CsrfHeaderHandler>();
        return services.AddApiGateways(apiBaseAddress, builder => builder
            .AddHttpMessageHandler<OfflineShortCircuitHandler>()
            .AddHttpMessageHandler<CsrfHeaderHandler>());
    }

    /// <summary>
    /// Application de bureau reliée à un serveur (modes 2, 3 et 5) : mêmes gateways HTTP, authentifiées par le jeton
    /// d'accès du poste (PKCE) au lieu du cookie du BFF. L'hôte fournit <see cref="Device.IAuthorizationBrowser"/> et
    /// <see cref="Device.ITokenVault"/>.
    /// </summary>
    public static IServiceCollection AddDeviceHttpGateways(this IServiceCollection services, Device.DeviceSignInOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Uri apiBaseAddress = options.BaseAddress ?? throw new InvalidOperationException($"{Device.DeviceSignInOptions.SectionName}:BaseAddress est obligatoire.");
        services.AddDeviceSignIn(options);
        services.AddSingleton<ISessionGateway>(sp => sp.GetRequiredService<Device.DeviceSessionGateway>());
        return services.AddApiGateways(apiBaseAddress, builder => builder.AddHttpMessageHandler<Device.BearerTokenHandler>());
    }

    /// <summary>
    /// Connexion du poste au Keycloak du cabinet (PKCE) et <see cref="Device.BearerTokenHandler"/>, sans les gateways
    /// HTTP ni la session de l'UI : le poste relié travaille sur sa réplique (session locale) et ne se connecte au
    /// serveur que pour se synchroniser.
    /// </summary>
    public static IServiceCollection AddDeviceSignIn(this IServiceCollection services, Device.DeviceSignInOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(nameof(Device.OidcPkceClient));
        // Un seul client OIDC (et un seul rafraîchissement à la fois) pour toute l'application.
        services.AddSingleton(sp => new Device.OidcPkceClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(Device.OidcPkceClient)),
            options,
            sp.GetRequiredService<Device.ITokenVault>(),
            sp.GetRequiredService<Device.IAuthorizationBrowser>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<Device.DeviceSessionGateway>();
        services.AddSingleton<IInteractiveSignIn>(sp => sp.GetRequiredService<Device.DeviceSessionGateway>());
        services.AddTransient<Device.BearerTokenHandler>();
        return services;
    }

    private static IServiceCollection AddApiGateways(this IServiceCollection services, Uri apiBaseAddress, Action<IHttpClientBuilder> authenticate)
    {
        services.AddTransient<DossierHeaderHandler>();
        void Configure(IHttpClientBuilder builder)
        {
            authenticate(builder);
            builder.AddHttpMessageHandler<DossierHeaderHandler>();
        }

        Configure(services.AddHttpClient<ICustomersGateway, HttpCustomersGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<ISuppliersGateway, HttpSuppliersGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<ICatalogGateway, HttpCatalogGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IDirectoryGateway, HttpDirectoryGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IQuotesGateway, HttpQuotesGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IInvoicesGateway, HttpInvoicesGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IRecurringInvoicesGateway, HttpRecurringInvoicesGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IPlatformTransmissionGateway, HttpPlatformTransmissionGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IReceivablesGateway, HttpReceivablesGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IDemoDataGateway, HttpDemoDataGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IFecGateway, HttpFecGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IAuditKitGateway, HttpAuditKitGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IAccountingGateway, HttpAccountingGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IBankingGateway, HttpBankingGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IRemindersGateway, HttpRemindersGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<ICabinetGateway, HttpCabinetGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IPilotageGateway, HttpPilotageGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<ITaxGateway, HttpTaxGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IBillingSettingsGateway, HttpBillingSettingsGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IBillingDashboardGateway, HttpBillingDashboardGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IPriceListsGateway, HttpPriceListsGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IAlertsGateway, HttpAlertsGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IAssistantGateway, HttpAssistantGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<ISpeechGateway, HttpSpeechGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IAdministrationGateway, HttpAdministrationGateway>(client => client.BaseAddress = apiBaseAddress));
        Configure(services.AddHttpClient<IUserPreferencesGateway, HttpUserPreferencesGateway>(client => client.BaseAddress = apiBaseAddress));
        return services;
    }
}
