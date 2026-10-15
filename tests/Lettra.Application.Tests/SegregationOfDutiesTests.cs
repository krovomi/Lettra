using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Authorization;
using Lettra.Application.Features.Policies;
using Lettra.Contracts.Policies;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Application.Tests;

/// <summary>Politique de cabinet en mémoire.</summary>
internal sealed class InMemoryPolicies : IOrganisationPolicyStore
{
    public Dictionary<OrganisationId, SegregationOfDutiesPolicy> Policies { get; } = [];

    public Guid? LastChangedBy { get; private set; }

    public bool SupportsSegregationOfDuties { get; set; } = true;

    public Task<SegregationOfDutiesPolicy> GetSegregationOfDutiesAsync(OrganisationId organisation, CancellationToken cancellationToken) =>
        Task.FromResult(Policies.GetValueOrDefault(organisation, SegregationOfDutiesPolicy.Disabled));

    public Task SaveSegregationOfDutiesAsync(OrganisationId organisation, SegregationOfDutiesPolicy policy, Guid changedBy, CancellationToken cancellationToken)
    {
        Policies[organisation] = policy;
        LastChangedBy = changedBy;
        return Task.CompletedTask;
    }
}

/// <summary>Séparation des tâches, option du cabinet (annexe F §3.2).</summary>
public sealed class SegregationOfDutiesTests
{
    private static readonly OrganisationId Cabinet = new(Guid.CreateVersion7());
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());

    private readonly FakeTenantContext _tenant = new() { Descriptor = new TenantDescriptor(Dossier, Cabinet, DatabaseProvider.Sqlite, "config:test") };
    private readonly FakeUser _user = new();
    private readonly FakePermissions _permissions = new();
    private readonly InMemoryPolicies _policies = new();

    private SegregationOfDutiesService Service() => new(_tenant, _user, _permissions, _policies);

    private void Active(decimal? seuil = 1000m) => _policies.Policies[Cabinet] = new SegregationOfDutiesPolicy(true, seuil);

    [Fact]
    public async Task Politique_inactive_celui_qui_saisit_peut_emettre()
    {
        (await Service().EnsureCanIssueAsync([_user.UserId], default)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Politique_active_celui_qui_a_saisi_ne_peut_pas_emettre()
    {
        Active();

        Result result = await Service().EnsureCanIssueAsync([Guid.CreateVersion7(), _user.UserId], default);

        result.Errors.Should().ContainSingle().Which.Should().Be(SegregationOfDutiesErrors.EnteredByIssuer);
        result.Errors[0].Kind.Should().Be(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Politique_active_un_autre_collaborateur_peut_emettre()
    {
        Active();

        (await Service().EnsureCanIssueAsync([Guid.CreateVersion7()], default)).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(999.99, CreditNoteApproval.NotRequired)]
    [InlineData(1000, CreditNoteApproval.NotRequired)]
    [InlineData(1000.01, CreditNoteApproval.ManagerApprovalRequired)]
    [InlineData(-1500, CreditNoteApproval.ManagerApprovalRequired)]
    public async Task Un_avoir_au_dela_du_seuil_attend_un_manager(decimal montantTtc, CreditNoteApproval attendu)
    {
        Active(1000m);
        _permissions.Granted.Remove(Permission.AvoirApprouver);

        (await Service().EvaluateCreditNoteAsync(montantTtc, default)).Should().Be(attendu);
    }

    [Fact]
    public async Task Un_manager_qui_cree_l_avoir_l_approuve_du_meme_coup()
    {
        Active(0m);

        (await Service().EvaluateCreditNoteAsync(10m, default)).Should().Be(CreditNoteApproval.GrantedByCurrentUser);
        _permissions.Asked.Should().ContainSingle().Which.Should().Be(AuthorizationScope.ForDossier(Dossier));
    }

    [Fact]
    public async Task Sans_seuil_aucun_avoir_n_attend_d_approbation()
    {
        Active(seuil: null);
        _permissions.Granted.Remove(Permission.AvoirApprouver);

        (await Service().EvaluateCreditNoteAsync(1_000_000m, default)).Should().Be(CreditNoteApproval.NotRequired);
    }

    [Fact]
    public async Task L_auteur_d_un_avoir_ne_peut_pas_l_approuver_lui_meme()
    {
        Active();

        (await Service().EnsureCanApproveCreditNoteAsync(_user.UserId, default)).Errors.Should().ContainSingle()
            .Which.Should().Be(SegregationOfDutiesErrors.ApproverIsCreator);
        (await Service().EnsureCanApproveCreditNoteAsync(Guid.CreateVersion7(), default)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Sur_le_poste_isole_tout_est_permis()
    {
        _policies.SupportsSegregationOfDuties = false;
        Active(0m);
        _permissions.Granted.Remove(Permission.AvoirApprouver);

        (await Service().EnsureCanIssueAsync([_user.UserId], default)).IsSuccess.Should().BeTrue();
        (await Service().EvaluateCreditNoteAsync(5000m, default)).Should().Be(CreditNoteApproval.NotRequired);
    }

    [Fact]
    public async Task L_administrateur_regle_la_politique_par_le_pipeline()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<IOrganisationPolicyStore>(_policies);
        services.AddSingleton<ITenantContext>(_tenant);
        services.AddSingleton<ICurrentUser>(_user);
        services.AddSingleton<IPermissionService>(_permissions);
        await using ServiceProvider root = services.BuildServiceProvider();
        await using AsyncServiceScope scope = root.CreateAsyncScope();
        ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        Result<SegregationOfDutiesSettings> saved = await commands.SendAsync(new UpdateSegregationOfDutiesCommand(Cabinet, true, 500m), default);
        Result<SegregationOfDutiesSettings> refused = await commands.SendAsync(new UpdateSegregationOfDutiesCommand(Cabinet, true, -1m), default);

        saved.Value.Should().Be(new SegregationOfDutiesSettings(true, 500m));
        _policies.LastChangedBy.Should().Be(_user.UserId);
        refused.Errors.Should().ContainSingle().Which.Code.Should().Be(SegregationOfDutiesErrors.ThresholdInvalidCode);
        (await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>().QueryAsync(new GetSegregationOfDutiesQuery(Cabinet), default))
            .Value.Should().Be(new SegregationOfDutiesSettings(true, 500m));
    }

    [Fact]
    public async Task Sans_la_permission_d_administrer_le_cabinet_le_reglage_est_refuse()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<IOrganisationPolicyStore>(_policies);
        services.AddSingleton<ITenantContext>(_tenant);
        services.AddSingleton<ICurrentUser>(_user);
        _permissions.Granted.Remove(Permission.CabinetAdministrer);
        services.AddSingleton<IPermissionService>(_permissions);
        await using ServiceProvider root = services.BuildServiceProvider();
        await using AsyncServiceScope scope = root.CreateAsyncScope();

        Result<SegregationOfDutiesSettings> result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
            .SendAsync(new UpdateSegregationOfDutiesCommand(Cabinet, true, 500m), default);

        result.Errors.Should().ContainSingle().Which.Kind.Should().Be(ErrorKind.Forbidden);
        _policies.Policies.Should().BeEmpty();
    }

    [Fact]
    public async Task Sur_le_poste_isole_le_reglage_n_est_pas_disponible()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<ITenantContext>(_tenant);
        services.AddSingleton<ICurrentUser>(_user);
        services.AddSingleton<IPermissionService>(_permissions);
        await using ServiceProvider root = services.BuildServiceProvider();
        await using AsyncServiceScope scope = root.CreateAsyncScope();

        Result<SegregationOfDutiesSettings> result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
            .SendAsync(new UpdateSegregationOfDutiesCommand(Cabinet, true, 500m), default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(SegregationOfDutiesErrors.NotAvailableCode);
    }
}
