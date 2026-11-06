using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Authorization;
using Lettra.Application.Features.Platform;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Common;
using Lettra.Contracts.Platform;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Tests;

/// <summary>
/// Arrivée et départ d'un cabinet par l'administrateur de la plateforme : l'ordre des étapes, leur idempotence et les
/// refus. Les adaptateurs (Keycloak, catalogue) sont essayés à part, sur un vrai Keycloak et sur les trois bases.
/// </summary>
public sealed class PlatformCabinetsTests
{
    private readonly Lifecycle _lifecycle = new();
    private readonly Identity _identity = new();
    private readonly Registry _registry = new();

    private static OnboardCabinetRequest Request(string name = "Cabinet Écureuil & Fils", string? cell = null) => new()
    {
        Name = name,
        AdministratorName = "Camille Martin",
        AdministratorEmail = "  Camille.Martin@ecureuil.example ",
        CellId = cell,
    };

    private Task<Result<OnboardCabinetResponse>> Onboard(OnboardCabinetRequest request) =>
        new OnboardCabinetHandler(_lifecycle, _identity, _registry, _registry).HandleAsync(new OnboardCabinetCommand(request), CancellationToken.None);

    [Fact]
    public async Task L_arrivee_cree_l_organisation_inscrit_le_cabinet_et_nomme_son_administrateur()
    {
        OnboardCabinetResponse onboarded = (await Onboard(Request())).Value;

        _identity.Organisations.Should().ContainSingle().Which.Should().Be(("cabinet-ecureuil-fils", "Cabinet Écureuil & Fils"));
        _lifecycle.Registered.Should().ContainSingle().Which.Should().Be((_identity.OrganisationId, "Cabinet Écureuil & Fils", "cellule-a"));
        onboarded.AdministratorId.Should().Be(_identity.UserId);
        onboarded.Invitation.Should().Be(InvitationEmailStatusDto.Sent);
        _registry.Accounts.Should().ContainSingle().Which.Should().Be((_identity.UserId, AccountState.Active, "Camille Martin", "camille.martin@ecureuil.example"));
        _registry.Assignments.Should().ContainSingle().Which.Should().Be(new RoleAssignment(
            new SubjectRef(SubjectKind.User, _identity.UserId), RoleTemplates.AdministrateurCabinet, AuthorizationScope.ForCabinet(_identity.OrganisationId)));
    }

    [Fact]
    public async Task Une_arrivee_relancee_ne_cree_aucun_doublon()
    {
        await Onboard(Request());
        _identity.InvitationSent = false;

        OnboardCabinetResponse again = (await Onboard(Request())).Value;

        _registry.Assignments.Should().ContainSingle("l'administrateur garde un seul rôle");
        again.Invitation.Should().Be(InvitationEmailStatusDto.Failed, "sans serveur d'e-mail, le compte existe quand même");
    }

    [Theory]
    [InlineData("", ErrorCodes.CabinetLifecycle.NameRequired)]
    [InlineData("   ", ErrorCodes.CabinetLifecycle.NameRequired)]
    public async Task Un_nom_vide_est_refuse(string name, string code)
    {
        (await Onboard(Request(name))).Errors.Should().ContainSingle().Which.Code.Should().Be(code);
        _identity.Organisations.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_nom_trop_long_ou_une_adresse_invalide_sont_refuses()
    {
        (await Onboard(Request(new string('a', OnboardCabinetRequest.MaxNameLength + 1)))).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ErrorCodes.CabinetLifecycle.NameTooLong);
        OnboardCabinetRequest invalid = Request();
        invalid.AdministratorEmail = "pas une adresse";
        (await Onboard(invalid)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Administration.EmailInvalid);
    }

    [Fact]
    public async Task La_cellule_est_la_seule_du_serveur_ou_celle_demandee()
    {
        _lifecycle.Cells = ["cellule-a", "cellule-b"];

        (await Onboard(Request())).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.CabinetLifecycle.CellUnknown, "deux cellules : il faut choisir");
        (await Onboard(Request(cell: "cellule-z"))).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.CabinetLifecycle.CellUnknown);
        (await Onboard(Request(cell: " cellule-b "))).Value.Cabinet.CellId.Should().Be("cellule-b");
    }

    [Fact]
    public async Task Sans_KMS_ou_sans_fournisseur_d_identite_aucun_cabinet_n_arrive()
    {
        _identity.IsConfigured = false;
        (await Onboard(Request())).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.CabinetLifecycle.Unavailable);

        _identity.IsConfigured = true;
        _lifecycle.IsAvailable = false;
        (await Onboard(Request())).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.CabinetLifecycle.Unavailable);
        _identity.Organisations.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_echec_d_une_etape_arrete_l_arrivee_avec_son_code()
    {
        _identity.Failure = PlatformErrors.NameTaken;
        (await Onboard(Request())).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.CabinetLifecycle.NameTaken);
        _lifecycle.Registered.Should().BeEmpty();

        _identity.Failure = null;
        _identity.MemberFailure = PlatformErrors.IdentityFailed;
        (await Onboard(Request())).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.CabinetLifecycle.IdentityFailed);
        _registry.Accounts.Should().BeEmpty();

        _identity.MemberFailure = null;
        _lifecycle.Failure = PlatformErrors.CabinetLeaving;
        (await Onboard(Request())).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.CabinetLifecycle.CabinetLeaving);

        _lifecycle.Failure = null;
        _registry.AccountFailure = RightsErrors.UserNotFound;
        (await Onboard(Request())).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task La_vue_de_la_plateforme_dit_si_un_cabinet_peut_arriver()
    {
        PlatformOverviewResponse overview = (await new ListPlatformCabinetsHandler(_lifecycle, _identity)
            .HandleAsync(new ListPlatformCabinetsQuery(), CancellationToken.None)).Value;
        overview.CanOnboard.Should().BeTrue();
        overview.Cells.Should().Equal("cellule-a");

        _identity.IsConfigured = false;
        (await new ListPlatformCabinetsHandler(_lifecycle, _identity).HandleAsync(new ListPlatformCabinetsQuery(), CancellationToken.None))
            .Value.CanOnboard.Should().BeFalse();
    }

    [Fact]
    public async Task Depart_annulation_et_relance_passent_par_le_catalogue()
    {
        FakeUser admin = new();
        OrganisationId cabinet = new(Guid.CreateVersion7());

        (await new ScheduleCabinetDepartureHandler(_lifecycle, admin).HandleAsync(new ScheduleCabinetDepartureCommand(cabinet), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        _lifecycle.Departures.Should().Equal((cabinet, admin.UserId));
        (await new CancelCabinetDepartureHandler(_lifecycle).HandleAsync(new CancelCabinetDepartureCommand(cabinet), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        (await new RetryFailedDossiersHandler(_lifecycle).HandleAsync(new RetryFailedDossiersCommand(), CancellationToken.None))
            .Value.Resumed.Should().Be(2);
    }

    [Fact]
    public void Les_cas_d_usage_de_la_plateforme_portent_sur_la_plateforme()
    {
        ListPlatformCabinetsQuery query = new();

        query.Permission.Should().Be(Permission.PlateformeAdministrer);
        query.Scope.Should().Be(AuthorizationScope.Plateforme);
    }

    [Theory]
    [InlineData("Cabinet Écureuil & Fils", "cabinet-ecureuil-fils")]
    [InlineData("  Expert--Comptable  Ä ", "expert-comptable-a")]
    [InlineData("!!!", "cabinet")]
    public void Le_nom_technique_est_le_nom_sans_accents_ni_espaces(string name, string alias) =>
        PlatformNames.Alias(name).Should().Be(alias);

    [Fact]
    public void Le_nom_technique_est_borne() =>
        PlatformNames.Alias(new string('a', 100)).Should().HaveLength(PlatformNames.MaxAliasLength);

    [Fact]
    public async Task Sans_catalogue_la_plateforme_n_heberge_rien()
    {
        NoCabinetLifecycle none = new();
        OrganisationId cabinet = new(Guid.CreateVersion7());

        none.IsAvailable.Should().BeFalse();
        (await none.ListAsync(default)).Should().BeEmpty();
        (await none.ListCellsAsync(default)).Should().BeEmpty();
        (await none.RetryFailedDossiersAsync(default)).Should().Be(0);
        (await none.RegisterAsync(cabinet, "x", "y", default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.CabinetLifecycle.Unavailable);
        (await none.ScheduleDepartureAsync(cabinet, Guid.Empty, default)).IsFailure.Should().BeTrue();
        (await none.CancelDepartureAsync(cabinet, default)).IsFailure.Should().BeTrue();
        (await new RetryFailedDossiersHandler(none).HandleAsync(new RetryFailedDossiersCommand(), default)).Value.Resumed.Should().Be(0);

        NoIdentityProvisioning noIdentity = new();
        noIdentity.IsConfigured.Should().BeFalse();
        (await noIdentity.EnsureOrganisationAsync("a", "b", default)).IsFailure.Should().BeTrue();
        (await noIdentity.EnsureMemberAsync(cabinet, "a@b.example", "A", default)).IsFailure.Should().BeTrue();
        await noIdentity.DeleteOrganisationAsync(cabinet, default);
    }

    private sealed class Lifecycle : ICabinetLifecycle
    {
        public bool IsAvailable { get; set; } = true;

        public IReadOnlyList<string> Cells { get; set; } = ["cellule-a"];

        public Error? Failure { get; set; }

        public List<(OrganisationId Organisation, string Name, string Cell)> Registered { get; } = [];

        public List<(OrganisationId Organisation, Guid RequestedBy)> Departures { get; } = [];

        public Task<IReadOnlyList<string>> ListCellsAsync(CancellationToken cancellationToken) => Task.FromResult(Cells);

        public Task<IReadOnlyList<PlatformCabinetResponse>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlatformCabinetResponse>>([]);

        public Task<Result<PlatformCabinetResponse>> RegisterAsync(OrganisationId organisation, string name, string cellId, CancellationToken cancellationToken)
        {
            if (Failure is Error error)
            {
                return Task.FromResult<Result<PlatformCabinetResponse>>(error);
            }

            if (!Registered.Any(r => r.Organisation == organisation))
            {
                Registered.Add((organisation, name, cellId));
            }

            return Task.FromResult<Result<PlatformCabinetResponse>>(Response(organisation, name, cellId));
        }

        public Task<Result<PlatformCabinetResponse>> ScheduleDepartureAsync(OrganisationId organisation, Guid requestedBy, CancellationToken cancellationToken)
        {
            Departures.Add((organisation, requestedBy));
            return Task.FromResult<Result<PlatformCabinetResponse>>(Response(organisation, "Parti", "cellule-a"));
        }

        public Task<Result<PlatformCabinetResponse>> CancelDepartureAsync(OrganisationId organisation, CancellationToken cancellationToken) =>
            Task.FromResult<Result<PlatformCabinetResponse>>(Response(organisation, "Resté", "cellule-a"));

        public Task<int> RetryFailedDossiersAsync(CancellationToken cancellationToken) => Task.FromResult(2);

        private static PlatformCabinetResponse Response(OrganisationId organisation, string name, string cell) =>
            new(organisation.Value, name, cell, PlatformCabinetStateDto.Active, false, 0, 0, [], DateTimeOffset.UnixEpoch, null, null);
    }

    private sealed class Identity : IIdentityProvisioning
    {
        public OrganisationId OrganisationId { get; } = new(Guid.CreateVersion7());

        public Guid UserId { get; } = Guid.CreateVersion7();

        public bool IsConfigured { get; set; } = true;

        public bool InvitationSent { get; set; } = true;

        public Error? Failure { get; set; }

        public Error? MemberFailure { get; set; }

        public List<(string Alias, string Name)> Organisations { get; } = [];

        public Task<Result<OrganisationId>> EnsureOrganisationAsync(string alias, string name, CancellationToken cancellationToken)
        {
            if (Failure is Error error)
            {
                return Task.FromResult<Result<OrganisationId>>(error);
            }

            if (!Organisations.Contains((alias, name)))
            {
                Organisations.Add((alias, name));
            }

            return Task.FromResult<Result<OrganisationId>>(OrganisationId);
        }

        public Task<Result<ProvisionedAccount>> EnsureMemberAsync(OrganisationId organisation, string email, string displayName, CancellationToken cancellationToken) =>
            Task.FromResult<Result<ProvisionedAccount>>(MemberFailure is Error error ? error : new ProvisionedAccount(UserId, InvitationSent));

        public Task DeleteOrganisationAsync(OrganisationId organisation, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Registre et administration des droits d'un seul cabinet, en mémoire.</summary>
    private sealed class Registry : IAuthorizationRegistry, IAuthorizationAdministration
    {
        private readonly Dictionary<Guid, (AccountState State, string? Name, string? Email)> _accounts = [];

        public List<RoleAssignment> Assignments { get; } = [];

        public Error? AccountFailure { get; set; }

        public IEnumerable<(Guid Id, AccountState State, string? Name, string? Email)> Accounts =>
            _accounts.Select(a => (a.Key, a.Value.State, a.Value.Name, a.Value.Email));

        public Task<Result> SetAccountAsync(OrganisationId cabinet, Guid userId, AccountState state, CancellationToken cancellationToken)
        {
            if (AccountFailure is Error error)
            {
                return Task.FromResult<Result>(error);
            }

            _accounts[userId] = (state, _accounts.GetValueOrDefault(userId).Name, _accounts.GetValueOrDefault(userId).Email);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> UpdatePersonAsync(OrganisationId cabinet, Guid userId, string displayName, string email, CancellationToken cancellationToken)
        {
            _accounts[userId] = (_accounts[userId].State, displayName, email);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<Guid>> AssignAsync(OrganisationId cabinet, RoleAssignment assignment, CancellationToken cancellationToken)
        {
            Assignments.Add(assignment);
            return Task.FromResult<Result<Guid>>(Guid.CreateVersion7());
        }

        public Task<CabinetRegistry?> ReadAsync(OrganisationId cabinet, CancellationToken cancellationToken) =>
            Task.FromResult<CabinetRegistry?>(new CabinetRegistry(
                cabinet, null, false, [], [], [], [], [.. Assignments.Select(a => new RegistryAssignment(Guid.CreateVersion7(), a))], []));

        public Task<Result<Guid>> InviteAsync(OrganisationId cabinet, string displayName, string email, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result> RenameGroupAsync(OrganisationId cabinet, GroupId group, string name, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result> DeleteGroupAsync(OrganisationId cabinet, GroupId group, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result> SaveCustomRoleAsync(OrganisationId cabinet, RegistryCustomRole role, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result> DeleteCustomRoleAsync(OrganisationId cabinet, string key, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<GroupId>> CreateGroupAsync(OrganisationId cabinet, string name, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result> SetGroupMemberAsync(GroupId group, Guid userId, bool member, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result> SetDossierPlacementAsync(GroupId group, DossierId dossier, bool placed, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RoleDefinition?> FindCustomRoleAsync(OrganisationId cabinet, string key, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<SubjectRef>> RevokeAsync(OrganisationId cabinet, Guid assignmentId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
