using System.Collections.Frozen;
using Lettra.Application.Abstractions.Identity;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Identity.Local;

public sealed class LocalIdentityOptions
{
    public const string SectionName = "Lettra:LocalIdentity";

    /// <summary>Identifiant stable de l'utilisateur du poste.</summary>
    public Guid UserId { get; set; } = new("00000000-0000-7000-8000-000000000001");
}

/// <summary>L'utilisateur du poste : toujours authentifié (la session Windows ou macOS fait foi en mode 1).</summary>
internal sealed class LocalCurrentUser(IOptions<LocalIdentityOptions> options) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public Guid UserId => options.Value.UserId;
}

/// <summary>Propriétaire du poste : toutes les permissions sur les dossiers auxquels le catalogue lui donne accès.</summary>
internal sealed class OwnerPermissionService : IPermissionService
{
    private static readonly FrozenSet<Permission> All = Enum.GetValues<Permission>().ToFrozenSet();

    public ValueTask<IReadOnlySet<Permission>> GetEffectiveAsync(Guid userId, DossierId dossier, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlySet<Permission>>(All);
}

public static class DependencyInjection
{
    public static IServiceCollection AddLocalIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LocalIdentityOptions>().Bind(configuration.GetSection(LocalIdentityOptions.SectionName));
        services.AddSingleton<ICurrentUser, LocalCurrentUser>();
        services.AddSingleton<IPermissionService, OwnerPermissionService>();
        return services;
    }
}
