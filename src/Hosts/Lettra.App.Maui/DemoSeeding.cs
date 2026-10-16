using Lettra.Application.Abstractions.Identity;
using Lettra.DemoData;
using Lettra.Domain.Common;
using Lettra.Infrastructure.Persistence.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lettra.App.Maui;

/// <summary>
/// Version de démonstration du poste : le jeu « Cabinet Démo » (Lettra.DemoData) se charge en arrière-plan dès que le
/// compte local est créé ou déverrouillé, au nom de ce compte. Il ne fait rien sans <c>Lettra:Demo:Seed=true</c> et
/// n'ajoute rien à un dossier qui a déjà des pièces : en pratique, une seule fois, juste après la création du compte.
/// </summary>
internal static class DemoSeeding
{
    /// <summary>
    /// Enveloppe le compte local pour lancer le chargement après une ouverture de session réussie. À appeler après
    /// <c>AddLettra</c> (qui enregistre le compte local) ; sans demande de jeu, rien n'est enregistré.
    /// </summary>
    public static IServiceCollection AddDemoSeeding(this IServiceCollection services, IConfiguration configuration)
    {
        if (!LettraDemoData.IsRequested(configuration))
        {
            return services;
        }

        ServiceDescriptor inner = services.Last(d => d.ServiceType == typeof(ILocalAccounts));
        services.Remove(inner);
        services.AddSingleton(sp => new DemoSeeder(
            configuration, sp.GetRequiredService<IKekStore>(), sp.GetRequiredService<ICurrentUser>(), sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<ILocalAccounts>(sp => new SeedingLocalAccounts(Create(sp, inner), sp.GetRequiredService<DemoSeeder>()));
        return services;
    }

    private static ILocalAccounts Create(IServiceProvider services, ServiceDescriptor descriptor) =>
        (ILocalAccounts)(descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(services)
            ?? ActivatorUtilities.CreateInstance(services, descriptor.ImplementationType!));

    /// <summary>Le compte local du poste, inchangé, qui signale chaque ouverture de session réussie.</summary>
    private sealed class SeedingLocalAccounts(ILocalAccounts inner, DemoSeeder seeder) : ILocalAccounts, IDisposable
    {
        public ValueTask<LocalAccountStatus> GetStatusAsync(CancellationToken cancellationToken) => inner.GetStatusAsync(cancellationToken);

        public async ValueTask<Result> CreateAsync(string displayName, string passphrase, CancellationToken cancellationToken) =>
            Opened(await inner.CreateAsync(displayName, passphrase, cancellationToken).ConfigureAwait(false));

        public async ValueTask<Result> UnlockAsync(string passphrase, CancellationToken cancellationToken) =>
            Opened(await inner.UnlockAsync(passphrase, cancellationToken).ConfigureAwait(false));

        public void Lock() => inner.Lock();

        public void Dispose() => (inner as IDisposable)?.Dispose();

        private Result Opened(Result result)
        {
            if (result.IsSuccess)
            {
                seeder.Start();
            }

            return result;
        }
    }
}

/// <summary>
/// Le chargement lui-même : sur un fil de fond, jamais deux à la fois, au plus une fois par lancement ; un échec est
/// journalisé et l'application continue. Il compose sa propre instance de l'application (Lettra.DemoData) et partage
/// seulement le coffre de clés de l'OS avec celle qui tourne.
/// </summary>
internal sealed class DemoSeeder(IConfiguration configuration, IKekStore keys, ICurrentUser currentUser, ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<DemoSeeder>();
    private int _started;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        Guid user = currentUser.UserId;
        if (user == Guid.Empty)
        {
            // Session déjà refermée : on réessaiera à la prochaine ouverture.
            Volatile.Write(ref _started, 0);
            return;
        }

        _ = Task.Run(() => SeedAsync(user));
    }

    private async Task SeedAsync(Guid user)
    {
#pragma warning disable CA1031 // Le jeu de démonstration ne doit jamais faire tomber l'application : tout échec est journalisé.
        try
        {
            DemoSeedResult result = await LettraDemoData.SeedAsync(
                configuration, "Demo", s => s.AddSingleton(keys), user, loggerFactory).ConfigureAwait(false);
            _logger.LogInformation("Jeu de démonstration : {Status} ({Dossiers} dossier(s)).", result.Status, result.Dossiers.Count);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Jeu de démonstration : chargement interrompu.");
        }
#pragma warning restore CA1031
    }
}
