using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.UI.Shared.Services;

/// <summary>
/// Une dépendance que tous les hôtes ne fournissent pas (synchronisation hors MAUI, voix, assistant, session dans un
/// test d'écran) : le composant ou le service la déclare ainsi au lieu de recevoir <see cref="IServiceProvider"/> et d'y
/// chercher ce qu'il veut. <see cref="Value"/> vaut <c>null</c> quand l'hôte ne l'a pas enregistrée ; elle est lue au
/// moment où on s'en sert, comme avant, ce qui évite les cycles entre services de l'écran.
/// </summary>
public sealed class Optional<T>
    where T : class
{
    private readonly Func<T?> _resolve;

    public Optional(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _resolve = services.GetService<T>;
    }

    private Optional(Func<T?> resolve) => _resolve = resolve;

    public T? Value => _resolve();

    /// <summary>Une dépendance qui n'existe qu'à une condition (la session, seulement si l'hôte a une passerelle de session).</summary>
    public static Optional<T> When(Func<T?> resolve) => new(resolve);
}

internal static class OptionalRegistration
{
    /// <summary>Rend <see cref="Optional{T}"/> disponible ; appelé par chaque groupe de services de l'interface qui s'en sert.</summary>
    public static IServiceCollection AddOptionalDependencies(this IServiceCollection services)
    {
        services.TryAdd(ServiceDescriptor.Transient(typeof(Optional<>), typeof(Optional<>)));
        return services;
    }
}
