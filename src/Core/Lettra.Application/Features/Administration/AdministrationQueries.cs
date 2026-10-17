using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Authorization;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Rights;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Administration;

/// <summary>
/// Ce que l'utilisateur courant peut voir : son cabinet, s'il administre (le cabinet ou une équipe), et ses niveaux par
/// espace sur le dossier ouvert. L'interface s'en sert pour n'afficher que les écrans permis ; le serveur, lui, revérifie
/// chaque cas d'usage.
/// </summary>
public sealed record GetMyAccessQuery(DossierId? Dossier) : IQuery<Result<MyAccessResponse>>, IFilterByPermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// L'espace d'administration du cabinet en une lecture, réduit à ce que l'utilisateur administre : un administrateur
/// d'équipe voit les personnes (pour composer son équipe) mais seulement les rôles des membres de ses équipes.
/// </summary>
public sealed record GetAdministrationOverviewQuery(OrganisationId Organisation) : IQuery<Result<AdministrationOverview>>, IFilterByPermission
{
    public Permission Permission => Permission.GroupeGerer;
}

internal sealed class GetMyAccessHandler(
    ICurrentUser user,
    IAuthorizationDirectory directory,
    TimeProvider clock) : IQueryHandler<GetMyAccessQuery, Result<MyAccessResponse>>
{
    public async Task<Result<MyAccessResponse>> HandleAsync(GetMyAccessQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        UserAuthorizationProfile? profile = await directory.FindUserAsync(user.UserId, cancellationToken).ConfigureAwait(false);
        if (profile is not { State: AccountState.Active, Cabinet: OrganisationId cabinet })
        {
            return new MyAccessResponse(null, false, []);
        }

        DateTimeOffset now = clock.GetUtcNow();
        HashSet<Permission> granted = [.. await ResolveAsync(profile, AuthorizationScope.ForCabinet(cabinet), cabinet, now, cancellationToken).ConfigureAwait(false)];
        if (query.Dossier is DossierId dossier)
        {
            granted.UnionWith(await ResolveAsync(profile, AuthorizationScope.ForDossier(dossier), cabinet, now, cancellationToken).ConfigureAwait(false));
        }

        // Administrateur d'une équipe : il gère cette équipe, sans rien voir d'autre du cabinet.
        foreach (RoleAssignment assignment in profile.Assignments.Where(a => a.Scope.Kind == ScopeKind.Groupe))
        {
            IReadOnlySet<Permission> onGroup = await ResolveAsync(profile, assignment.Scope, cabinet, now, cancellationToken).ConfigureAwait(false);
            if (onGroup.Contains(Permission.GroupeGerer))
            {
                granted.Add(Permission.GroupeGerer);
            }
        }

        bool canAdminister = granted.Contains(Permission.UtilisateursGerer)
            || granted.Contains(Permission.GroupeGerer)
            || granted.Contains(Permission.CabinetAdministrer);
        return new MyAccessResponse(cabinet.Value, canAdminister, AccessAreaPermissions.Describe(granted));
    }

    private async Task<IReadOnlySet<Permission>> ResolveAsync(
        UserAuthorizationProfile profile, AuthorizationScope scope, OrganisationId cabinet, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ScopePath? path = await directory.GetScopePathAsync(scope, cancellationToken).ConfigureAwait(false);
        return path?.Cabinet == cabinet ? PermissionResolver.Resolve(profile, path, now) : PermissionResolver.None;
    }
}

internal sealed class GetAdministrationOverviewHandler(
    ICurrentUser user,
    IPermissionService permissions,
    IAuthorizationRegistry registry,
    IDossierDirectory dossiers) : IQueryHandler<GetAdministrationOverviewQuery, Result<AdministrationOverview>>
{
    public async Task<Result<AdministrationOverview>> HandleAsync(GetAdministrationOverviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlySet<Permission> onCabinet = await permissions
            .GetEffectiveAsync(user.UserId, AuthorizationScope.ForCabinet(query.Organisation), cancellationToken).ConfigureAwait(false);
        CabinetRegistry? read = await registry.ReadAsync(query.Organisation, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            return onCabinet.Count == 0 ? SecurityErrors.Forbidden : RightsErrors.NotSupported;
        }

        HashSet<Guid> manageable = [];
        foreach (RegistryGroup group in read.Groups)
        {
            IReadOnlySet<Permission> onGroup = await permissions
                .GetEffectiveAsync(user.UserId, AuthorizationScope.ForGroup(new GroupId(group.Id)), cancellationToken).ConfigureAwait(false);
            if (onGroup.Contains(Permission.GroupeGerer))
            {
                manageable.Add(group.Id);
            }
        }

        bool managePeople = onCabinet.Contains(Permission.UtilisateursGerer);
        bool manageTeams = onCabinet.Contains(Permission.GroupeGerer);
        bool manageRoles = onCabinet.Contains(Permission.CabinetAdministrer);
        if (!managePeople && !manageTeams && !manageRoles && manageable.Count == 0)
        {
            return SecurityErrors.Forbidden;
        }

        AdministrationCapabilities can = read.ReadOnly
            ? new AdministrationCapabilities(false, false, false, ReadOnly: true)
            : new AdministrationCapabilities(managePeople, manageTeams, manageRoles, ReadOnly: false);
        IReadOnlyList<DossierSummary> cabinetDossiers = await dossiers.ListAsync(query.Organisation, cancellationToken).ConfigureAwait(false);
        return AdministrationOverviewBuilder.Build(read, user.UserId, can, manageable, cabinetDossiers);
    }
}

/// <summary>Assemble la réponse à partir du registre : fonctions pures, testées sans base.</summary>
internal static class AdministrationOverviewBuilder
{
    public static AdministrationOverview Build(
        CabinetRegistry read, Guid me, AdministrationCapabilities can, IReadOnlySet<Guid> manageableTeams, IReadOnlyList<DossierSummary> dossiers)
    {
        ILookup<Guid, Guid> teamsOfPerson = read.Memberships.ToLookup(m => m.UserId, m => m.GroupId);
        ILookup<Guid, Guid> membersOfTeam = read.Memberships.ToLookup(m => m.GroupId, m => m.UserId);
        ILookup<Guid, Guid> dossiersOfTeam = read.Placements.ToLookup(p => p.GroupId, p => p.DossierId);
        ILookup<SubjectRef, RoleAssignmentSummary> assignmentsOf = read.Assignments.ToLookup(a => a.Assignment.Subject, Summarize);

        // Sans la gestion des personnes, on ne voit les rôles que des membres de ses équipes.
        HashSet<Guid> visibleRoles = can.ManagePeople || can.ReadOnly
            ? read.People.Select(p => p.Id).ToHashSet()
            : manageableTeams.SelectMany(t => membersOfTeam[t]).ToHashSet();

        PersonSummary[] people = read.People
            .OrderByDescending(p => p.Id == me)
            .ThenBy(p => p.DisplayName ?? p.Email ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new PersonSummary(
                p.Id,
                p.DisplayName,
                p.Email,
                (AccountStateDto)p.State,
                p.CreatedAt,
                teamsOfPerson[p.Id].ToArray(),
                visibleRoles.Contains(p.Id) ? assignmentsOf[new SubjectRef(SubjectKind.User, p.Id)].ToArray() : [],
                p.Id == me))
            .ToArray();

        TeamSummary[] teams = read.Groups
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new TeamSummary(
                g.Id,
                g.Name,
                membersOfTeam[g.Id].ToArray(),
                dossiersOfTeam[g.Id].ToArray(),
                assignmentsOf[new SubjectRef(SubjectKind.Group, g.Id)].ToArray(),
                !can.ReadOnly && (can.ManageTeams || manageableTeams.Contains(g.Id))))
            .ToArray();

        Dictionary<string, int> uses = read.Assignments.GroupBy(a => a.Assignment.Role, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        IEnumerable<RoleDescription> provided = RoleTemplates.All.Values
            .Where(r => r.WidestScope != ScopeKind.Plateforme)
            .Select(r => Describe(r, isCustom: false, null, null, uses));
        IEnumerable<RoleDescription> custom = read.CustomRoles
            .OrderBy(r => r.Name ?? r.Definition.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => Describe(r.Definition, isCustom: true, r.Name, r.Description, uses));

        return new AdministrationOverview(
            read.Cabinet.Value,
            read.CabinetName,
            can,
            people,
            teams,
            dossiers,
            [.. RoleOrder(provided), .. custom]);
    }

    public static RoleAssignmentSummary Summarize(RegistryAssignment stored)
    {
        RoleAssignment a = stored.Assignment;
        return new RoleAssignmentSummary(stored.Id, (SubjectKindDto)a.Subject.Kind, a.Subject.Id, a.Role, (ScopeKindDto)a.Scope.Kind, a.Scope.Id, a.ExpiresAt);
    }

    public static RoleDescription Describe(RoleDefinition role, bool isCustom, string? name, string? description, IReadOnlyDictionary<string, int> uses) =>
        new(
            role.Key,
            isCustom,
            name,
            description,
            (ScopeKindDto)role.WidestScope,
            role.RequiresExpiry,
            AccessAreaPermissions.Describe(role.Permissions),
            uses.GetValueOrDefault(role.Key));

    /// <summary>Du plus courant au plus rare : ce qu'un administrateur donne le plus souvent vient en premier.</summary>
    private static IEnumerable<RoleDescription> RoleOrder(IEnumerable<RoleDescription> roles)
    {
        string[] order =
        [
            RoleTemplates.Collaborateur, RoleTemplates.CollaborateurEtendu, RoleTemplates.Manager, RoleTemplates.AdministrateurGroupe,
            RoleTemplates.AdministrateurCabinet, RoleTemplates.Auditeur, RoleTemplates.DelegationAvoirsExports, RoleTemplates.ExploitantSurSite,
            RoleTemplates.ClientPortail,
        ];
        return roles.OrderBy(r => Array.IndexOf(order, r.Key) is var i && i < 0 ? int.MaxValue : i);
    }
}
