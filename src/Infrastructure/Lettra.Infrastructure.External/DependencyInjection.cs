using Lettra.Application.Abstractions.External;
using Lettra.Application.Abstractions.Mail;
using Lettra.Infrastructure.External.Mail;
using Lettra.Infrastructure.External.Sirene;
using Lettra.Infrastructure.External.Vies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Lettra.Infrastructure.External;

public static class DependencyInjection
{
    /// <summary>
    /// Services externes (SIRENE, VIES, envoi d'e-mails), dans tous les modes. Hors ligne, les appels échouent vite et
    /// renvoient <c>Directory.Unavailable</c> ; <c>Lettra:External:Enabled=false</c> coupe les annuaires. L'envoi
    /// d'e-mails a son propre réglage (<see cref="EmailOptions"/>), coupé par défaut.
    /// </summary>
    public static IServiceCollection AddExternalServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddEmailSender(configuration);
        IConfigurationSection section = configuration.GetSection(ExternalServicesOptions.SectionName);
        services.AddOptions<ExternalServicesOptions>().Bind(section);
        services.TryAddSingleton(TimeProvider.System);

        ExternalServicesOptions settings = section.Get<ExternalServicesOptions>() ?? new ExternalServicesOptions();
        if (!settings.Enabled)
        {
            services.AddSingleton<DisabledDirectories>();
            services.AddSingleton<ICompanyDirectory>(sp => sp.GetRequiredService<DisabledDirectories>());
            services.AddSingleton<IVatNumberChecker>(sp => sp.GetRequiredService<DisabledDirectories>());
            return services;
        }

        services.AddHybridCache();

        services.AddHttpClient<ICompanyDirectory, RechercheEntreprisesDirectory>((sp, client) =>
                client.BaseAddress = sp.GetRequiredService<IOptions<ExternalServicesOptions>>().Value.SireneBaseAddress)
            .AddResilienceHandler("sirene", (pipeline, context) => Configure(pipeline, context.ServiceProvider));

        services.AddHttpClient<IVatNumberChecker, ViesVatNumberChecker>((sp, client) =>
                client.BaseAddress = sp.GetRequiredService<IOptions<ExternalServicesOptions>>().Value.ViesBaseAddress)
            .AddResilienceHandler("vies", (pipeline, context) => Configure(pipeline, context.ServiceProvider));

        return services;
    }

    /// <summary>
    /// Le port <see cref="IEmailSender"/> : SMTP quand <c>Lettra:Email:Enabled</c> est vrai (serveur et adresse
    /// d'expédition obligatoires, vérifiés au démarrage). Sinon reste l'adaptateur coupé de l'application, qui n'envoie
    /// rien.
    /// </summary>
    public static IServiceCollection AddEmailSender(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        IConfigurationSection section = configuration.GetSection(EmailOptions.SectionName);
        services.AddOptions<EmailOptions>().Bind(section);
        EmailOptions email = section.Get<EmailOptions>() ?? new EmailOptions();
        services.Replace(ServiceDescriptor.Singleton(new LettraPublicAddress(email.PublicUrl)));
        if (!email.Enabled)
        {
            return services;
        }

        if (string.IsNullOrWhiteSpace(email.Host) || string.IsNullOrWhiteSpace(email.FromAddress) || email.Port is <= 0 or > 65535)
        {
            throw new InvalidOperationException(
                $"{EmailOptions.SectionName} : Host, Port et FromAddress sont obligatoires quand Enabled vaut true.");
        }

        services.Replace(ServiceDescriptor.Singleton<IEmailSender, SmtpEmailSender>());
        return services;
    }

    /// <summary>Une seule nouvelle tentative, et un délai total court : l'utilisateur attend devant l'écran.</summary>
    private static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> pipeline, IServiceProvider services)
    {
        TimeSpan total = services.GetRequiredService<IOptions<ExternalServicesOptions>>().Value.Timeout;
        pipeline
            .AddTimeout(total)
            .AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 1, Delay = TimeSpan.FromMilliseconds(200) })
            .AddTimeout(total / 2);
    }
}
