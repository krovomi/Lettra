using Lettra.Contracts.Administration;
using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Rights;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Administration;

/// <summary>
/// Registre en mémoire d'un petit cabinet fictif : six personnes, deux équipes, trois dossiers, un rôle sur mesure.
/// Chaque écriture modifie l'état, comme le vrai serveur ; la page relit l'espace après chaque geste.
/// </summary>
internal sealed class FakeAdministrationGateway : IAdministrationGateway
{
    public static readonly Guid CabinetId = new("0199e3a0-0000-7000-8000-0000000000c1");
    public static readonly Guid Me = new("0199e3a0-0000-7000-8000-0000000de001");
    public static readonly Guid Lea = new("0199e3a0-0000-7000-8000-0000000de002");
    public static readonly Guid Hugo = new("0199e3a0-0000-7000-8000-0000000de003");
    public static readonly Guid Ines = new("0199e3a0-0000-7000-8000-0000000de004");
    public static readonly Guid Paul = new("0199e3a0-0000-7000-8000-0000000de005");
    public static readonly Guid Nadia = new("0199e3a0-0000-7000-8000-0000000de006");
    public static readonly Guid TeamTpe = new("0199e3a0-0000-7000-8000-0000000de101");
    public static readonly Guid TeamSocial = new("0199e3a0-0000-7000-8000-0000000de102");
    public static readonly Guid Boulangerie = new("0199e3a0-0000-7000-8000-00000000a001");
    public static readonly Guid Garage = new("0199e3a0-0000-7000-8000-00000000a002");
    public static readonly Guid Fleuriste = new("0199e3a0-0000-7000-8000-00000000a003");

    private static readonly Dictionary<string, (ScopeKindDto Widest, bool Expiry, AreaAccess[] Areas)> Templates = new(StringComparer.Ordinal)
    {
        ["administrateur-cabinet"] = (ScopeKindDto.Cabinet, false, [new(AccessAreaDto.Alerts, AccessLevelDto.Approve), new(AccessAreaDto.Administration, AccessLevelDto.Approve)]),
        ["manager"] = (ScopeKindDto.Cabinet, false,
        [
            new(AccessAreaDto.Quotes, AccessLevelDto.Work), new(AccessAreaDto.Invoices, AccessLevelDto.Approve), new(AccessAreaDto.CreditNotes, AccessLevelDto.Approve),
            new(AccessAreaDto.Parties, AccessLevelDto.Work), new(AccessAreaDto.Catalog, AccessLevelDto.Work), new(AccessAreaDto.Accounting, AccessLevelDto.Approve),
            new(AccessAreaDto.Alerts, AccessLevelDto.Approve), new(AccessAreaDto.Assistant, AccessLevelDto.Work), new(AccessAreaDto.Administration, AccessLevelDto.View),
        ]),
        ["administrateur-groupe"] = (ScopeKindDto.Groupe, false, [new(AccessAreaDto.Administration, AccessLevelDto.Work)]),
        ["collaborateur"] = (ScopeKindDto.Groupe, false,
        [
            new(AccessAreaDto.Quotes, AccessLevelDto.Work), new(AccessAreaDto.Invoices, AccessLevelDto.Work), new(AccessAreaDto.CreditNotes, AccessLevelDto.View),
            new(AccessAreaDto.Parties, AccessLevelDto.Work), new(AccessAreaDto.Catalog, AccessLevelDto.View), new(AccessAreaDto.Assistant, AccessLevelDto.Work),
        ]),
        ["auditeur"] = (ScopeKindDto.Cabinet, true,
        [
            new(AccessAreaDto.Quotes, AccessLevelDto.View), new(AccessAreaDto.Invoices, AccessLevelDto.View), new(AccessAreaDto.CreditNotes, AccessLevelDto.View),
            new(AccessAreaDto.Accounting, AccessLevelDto.View),
        ]),
    };

    private readonly List<PersonRow> _people =
    [
        new(Me, "Camille Démo", "demo@cabinet-demo.example", AccountStateDto.Active),
        new(Lea, "Léa Martin", "lea.martin@cabinet-demo.example", AccountStateDto.Active),
        new(Hugo, "Hugo Lefèvre", "hugo.lefevre@cabinet-demo.example", AccountStateDto.Active),
        new(Ines, "Inès Bernard", "ines.bernard@cabinet-demo.example", AccountStateDto.Invited),
        new(Paul, "Paul Garnier", "paul.garnier@cabinet-demo.example", AccountStateDto.Suspended),
        new(Nadia, "Nadia Haddad", "nadia.haddad@cabinet-demo.example", AccountStateDto.Active),
    ];

    private readonly List<TeamRow> _teams =
    [
        new(TeamTpe, "Pôle commerçants") { Members = { Lea, Hugo }, Dossiers = { Boulangerie, Fleuriste } },
        new(TeamSocial, "Pôle social") { Members = { Nadia }, Dossiers = { Garage } },
    ];

    private readonly List<RoleAssignmentSummary> _assignments = [];
    private readonly List<RoleDescription> _custom =
    [
        new("c-assistant-devis", true, "Assistant devis", "Prépare les devis, consulte les clients.", ScopeKindDto.Cabinet, false,
            [new(AccessAreaDto.Quotes, AccessLevelDto.Work), new(AccessAreaDto.Invoices, AccessLevelDto.View), new(AccessAreaDto.CreditNotes, AccessLevelDto.View), new(AccessAreaDto.Parties, AccessLevelDto.View)], 0),
    ];

    public FakeAdministrationGateway()
    {
        Add(SubjectKindDto.User, Me, "administrateur-cabinet", ScopeKindDto.Cabinet, CabinetId);
        Add(SubjectKindDto.User, Me, "manager", ScopeKindDto.Cabinet, CabinetId);
        Add(SubjectKindDto.Group, TeamTpe, "collaborateur", ScopeKindDto.Groupe, TeamTpe);
        Add(SubjectKindDto.User, Lea, "administrateur-groupe", ScopeKindDto.Groupe, TeamTpe);
        Add(SubjectKindDto.User, Nadia, "collaborateur", ScopeKindDto.Groupe, TeamSocial);
        Add(SubjectKindDto.User, Paul, "auditeur", ScopeKindDto.Cabinet, CabinetId, DateTimeOffset.Now.AddDays(20));
    }

    public bool CanAdminister { get; set; } = true;

    public bool ReadOnly { get; set; }

    public List<string> Calls { get; } = [];

    public SaveCustomRoleRequest? LastRole { get; private set; }

    public Task<OperationResult<MyAccessResponse>> GetMyAccessAsync(Guid? dossierId, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<MyAccessResponse>.Success(new MyAccessResponse(CabinetId, CanAdminister, CanAdminister ? Templates["manager"].Areas : [])));

    public Task<OperationResult<AdministrationOverview>> GetOverviewAsync(Guid cabinetId, CancellationToken cancellationToken)
    {
        if (!CanAdminister)
        {
            return Task.FromResult(OperationResult<AdministrationOverview>.Failure(ErrorCodes.Security.Forbidden));
        }

        List<RoleDescription> roles = [.. Templates.Select(t => new RoleDescription(t.Key, false, null, null, t.Value.Widest, t.Value.Expiry, t.Value.Areas, Count(t.Key)))];
        roles.AddRange(_custom.Select(r => r with { AssignedCount = Count(r.Key) }));
        AdministrationOverview overview = new(
            CabinetId,
            "Cabinet Démo",
            ReadOnly ? new AdministrationCapabilities(false, false, false, true) : new AdministrationCapabilities(true, true, true, false),
            [.. _people.Select(p => new PersonSummary(p.Id, p.Name, p.Email, p.State, DateTimeOffset.UnixEpoch,
                [.. _teams.Where(t => t.Members.Contains(p.Id)).Select(t => t.Id)], [.. Of(SubjectKindDto.User, p.Id)], p.Id == Me))],
            [.. _teams.Select(t => new TeamSummary(t.Id, t.Name, [.. t.Members], [.. t.Dossiers], [.. Of(SubjectKindDto.Group, t.Id)], !ReadOnly))],
            [new(Boulangerie, "Boulangerie Dupain"), new(Garage, "Garage du Centre"), new(Fleuriste, "Fleurs de Lys")],
            roles);
        return Task.FromResult(OperationResult<AdministrationOverview>.Success(overview));
    }

    /// <summary>Sort de l'e-mail d'invitation que le faux serveur annonce.</summary>
    public InvitationEmailStatusDto InvitationEmail { get; set; } = InvitationEmailStatusDto.Sent;

    public Task<OperationResult<InvitationEmailResponse>> ResendInvitationAsync(Guid cabinetId, Guid userId, CancellationToken cancellationToken)
    {
        Calls.Add($"resend {userId}");
        return Task.FromResult(OperationResult<InvitationEmailResponse>.Success(new InvitationEmailResponse(userId, InvitationEmail)));
    }

    public Task<OperationResult<PersonSummary>> InviteAsync(Guid cabinetId, InvitePersonRequest request, CancellationToken cancellationToken)
    {
        Calls.Add($"invite {request.Email}");
        if (_people.Any(p => string.Equals(p.Email, request.Email, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(OperationResult<PersonSummary>.Failure([new ErrorDetail(ErrorCodes.Administration.EmailTaken, nameof(InvitePersonRequest.Email))]));
        }

        PersonRow row = new(Guid.CreateVersion7(), request.DisplayName.Trim(), request.Email.Trim(), AccountStateDto.Invited);
        _people.Add(row);
        return Task.FromResult(OperationResult<PersonSummary>.Success(new PersonSummary(row.Id, row.Name, row.Email, row.State, DateTimeOffset.UnixEpoch, [], [], false, InvitationEmail)));
    }

    public Task<OperationResult<bool>> UpdatePersonAsync(Guid cabinetId, Guid userId, UpdatePersonRequest request, CancellationToken cancellationToken)
    {
        PersonRow row = _people.Single(p => p.Id == userId);
        row.Name = request.DisplayName;
        row.Email = request.Email;
        return Ok($"update {userId}");
    }

    public Task<OperationResult<bool>> SetAccountStateAsync(Guid cabinetId, Guid userId, AccountStateDto state, CancellationToken cancellationToken)
    {
        if (userId == Me && state != AccountStateDto.Active)
        {
            return Task.FromResult(OperationResult<bool>.Failure(ErrorCodes.Administration.SelfLockout));
        }

        _people.Single(p => p.Id == userId).State = state;
        return Ok($"state {userId} {state}");
    }

    public Task<OperationResult<RoleAssignmentSummary>> AssignAsync(Guid cabinetId, AssignRoleRequest request, CancellationToken cancellationToken)
    {
        Calls.Add($"assign {request.SubjectId} {request.Role} {request.ScopeKind}");
        RoleAssignmentSummary added = Add(request.SubjectKind, request.SubjectId, request.Role, request.ScopeKind, request.ScopeId, request.ExpiresAt);
        return Task.FromResult(OperationResult<RoleAssignmentSummary>.Success(added));
    }

    public Task<OperationResult<bool>> RevokeAsync(Guid cabinetId, Guid assignmentId, CancellationToken cancellationToken)
    {
        _assignments.RemoveAll(a => a.Id == assignmentId);
        return Ok($"revoke {assignmentId}");
    }

    public Task<OperationResult<GroupSummary>> CreateTeamAsync(Guid cabinetId, CreateGroupRequest request, CancellationToken cancellationToken)
    {
        TeamRow team = new(Guid.CreateVersion7(), request.Name.Trim());
        _teams.Add(team);
        Calls.Add($"team {team.Name}");
        return Task.FromResult(OperationResult<GroupSummary>.Success(new GroupSummary(team.Id, team.Name)));
    }

    public Task<OperationResult<bool>> RenameTeamAsync(Guid cabinetId, Guid teamId, RenameTeamRequest request, CancellationToken cancellationToken)
    {
        _teams.Single(t => t.Id == teamId).Name = request.Name.Trim();
        return Ok($"rename {teamId}");
    }

    public Task<OperationResult<bool>> DeleteTeamAsync(Guid cabinetId, Guid teamId, CancellationToken cancellationToken)
    {
        _teams.RemoveAll(t => t.Id == teamId);
        _assignments.RemoveAll(a => a.SubjectId == teamId || a.ScopeId == teamId);
        return Ok($"delete team {teamId}");
    }

    public Task<OperationResult<bool>> SetTeamMemberAsync(Guid teamId, Guid userId, bool member, CancellationToken cancellationToken)
    {
        TeamRow team = _teams.Single(t => t.Id == teamId);
        _ = member ? team.Members.Add(userId) : team.Members.Remove(userId);
        return Ok($"member {teamId} {userId} {member}");
    }

    public Task<OperationResult<bool>> SetTeamDossierAsync(Guid teamId, Guid dossierId, bool placed, CancellationToken cancellationToken)
    {
        TeamRow team = _teams.Single(t => t.Id == teamId);
        _ = placed ? team.Dossiers.Add(dossierId) : team.Dossiers.Remove(dossierId);
        return Ok($"dossier {teamId} {dossierId} {placed}");
    }

    public Task<OperationResult<RoleDescription>> SaveRoleAsync(Guid cabinetId, string? key, SaveCustomRoleRequest request, CancellationToken cancellationToken)
    {
        LastRole = request;
        RoleDescription role = new(key ?? "c-" + Guid.CreateVersion7().ToString("N"), true, request.Name.Trim(), request.Description, ScopeKindDto.Cabinet, false, [.. request.Areas], 0);
        _custom.RemoveAll(r => r.Key == role.Key);
        _custom.Add(role);
        Calls.Add($"role {role.Name}");
        return Task.FromResult(OperationResult<RoleDescription>.Success(role));
    }

    public Task<OperationResult<bool>> DeleteRoleAsync(Guid cabinetId, string key, CancellationToken cancellationToken)
    {
        _custom.RemoveAll(r => r.Key == key);
        return Ok($"delete role {key}");
    }

    private Task<OperationResult<bool>> Ok(string call)
    {
        Calls.Add(call);
        return Task.FromResult(OperationResult<bool>.Success(true));
    }

    private int Count(string role) => _assignments.Count(a => a.Role == role);

    private IEnumerable<RoleAssignmentSummary> Of(SubjectKindDto kind, Guid id) => _assignments.Where(a => a.SubjectKind == kind && a.SubjectId == id);

    private RoleAssignmentSummary Add(SubjectKindDto kind, Guid subject, string role, ScopeKindDto scope, Guid scopeId, DateTimeOffset? until = null)
    {
        RoleAssignmentSummary assignment = new(Guid.CreateVersion7(), kind, subject, role, scope, scopeId, until);
        _assignments.Add(assignment);
        return assignment;
    }

    private sealed class PersonRow(Guid id, string name, string email, AccountStateDto state)
    {
        public Guid Id { get; } = id;

        public string Name { get; set; } = name;

        public string Email { get; set; } = email;

        public AccountStateDto State { get; set; } = state;
    }

    private sealed class TeamRow(Guid id, string name)
    {
        public Guid Id { get; } = id;

        public string Name { get; set; } = name;

        public HashSet<Guid> Members { get; } = [];

        public HashSet<Guid> Dossiers { get; } = [];
    }
}
