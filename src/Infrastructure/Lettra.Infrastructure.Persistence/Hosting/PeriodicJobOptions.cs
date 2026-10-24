using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Persistence.Hosting;

/// <summary>Le calendrier d'une tâche de fond périodique : première passe après <see cref="InitialDelay"/>, puis une par <see cref="Interval"/>.</summary>
public interface IPeriodicJobOptions
{
    TimeSpan Interval { get; }

    TimeSpan InitialDelay { get; }
}

/// <summary>Validation au démarrage de l'hôte du calendrier des tâches de fond.</summary>
public static class PeriodicJobOptions
{
    /// <summary>
    /// Un intervalle nul ou négatif ferait échouer <see cref="PeriodicTimer"/>, un délai négatif <c>Task.Delay</c>, à la
    /// première passe seulement : l'hôte refuse de démarrer avec un message qui nomme le réglage.
    /// </summary>
    public static OptionsBuilder<T> ValidatePeriodicJob<T>(this OptionsBuilder<T> builder, string sectionName)
        where T : class, IPeriodicJobOptions
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .Validate(o => o.Interval > TimeSpan.Zero, $"{sectionName}:{nameof(IPeriodicJobOptions.Interval)} doit être strictement positif.")
            .Validate(o => o.InitialDelay >= TimeSpan.Zero, $"{sectionName}:{nameof(IPeriodicJobOptions.InitialDelay)} ne peut pas être négatif.")
            .ValidateOnStart();
    }
}
