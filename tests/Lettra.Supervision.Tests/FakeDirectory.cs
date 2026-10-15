using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Authorization;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Supervision.Tests;

/// <summary>
/// Registre des droits en mémoire : des utilisateurs avec leurs attributions directes, des dossiers rangés dans des
/// groupes. Les permissions sont calculées par le vrai service (<c>AddLettraAuthorization</c>).
/// </summary>
internal sealed class FakeDirectory : IAuthorizationDirectory, IScopeMemberDirectory
{
    private readonly Dictionary<Guid, UserAuthorizationProfile> _users = [];
    private readonly Dictionary<Guid, (OrganisationId Cabinet, GroupId[] Groups)> _dossiers = [];

    public void AddDossier(DossierId dossier, OrganisationId cabinet, params GroupId[] groups) => _dossiers[dossier.Value] = (cabinet, groups);

    public Guid AddUser(OrganisationId cabinet, string role, AuthorizationScope scope, AccountState state = AccountState.Active)
    {
        Guid id = Guid.CreateVersion7();
        _users[id] = new UserAuthorizationProfile(id, state, cabinet, [], [new RoleAssignment(new SubjectRef(SubjectKind.User, id), role, scope)], []);
        return id;
    }

    public ValueTask<UserAuthorizationProfile?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_users.GetValueOrDefault(userId));

    public ValueTask<ScopePath?> GetScopePathAsync(AuthorizationScope scope, CancellationToken cancellationToken)
    {
        ScopePath? path = scope.Kind switch
        {
            ScopeKind.Cabinet => new ScopePath(scope, new OrganisationId(scope.Id), [scope, AuthorizationScope.Plateforme]),
            ScopeKind.Dossier when _dossiers.TryGetValue(scope.Id, out var d) => new ScopePath(
                scope,
                d.Cabinet,
                [scope, .. d.Groups.Select(AuthorizationScope.ForGroup), AuthorizationScope.ForCabinet(d.Cabinet), AuthorizationScope.Plateforme]),
            _ => null,
        };
        return ValueTask.FromResult(path);
    }

    public ValueTask<IReadOnlyCollection<Guid>> ListCandidatesAsync(ScopePath path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        IReadOnlyCollection<Guid> candidates = [.. _users.Values
            .Where(u => u.State == AccountState.Active && u.Assignments.Any(a => path.Ancestors.Contains(a.Scope)))
            .Select(u => u.UserId)];
        return ValueTask.FromResult(candidates);
    }
}

internal static class FakeDirectoryRegistration
{
    public static IServiceCollection AddFakeDirectory(this IServiceCollection services, FakeDirectory directory)
    {
        services.AddSingleton<IAuthorizationDirectory>(directory);
        services.AddSingleton<IScopeMemberDirectory>(directory);
        services.AddLettraAuthorization();
        return services;
    }
}
