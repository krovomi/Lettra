using Lettra.Application;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Authorization;
using Lettra.Contracts.Alerts;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Sqlite;
using Lettra.Supervision.Application;
using Lettra.Supervision.Domain;
using Lettra.Supervision.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Supervision.Tests;

/// <summary>
/// Alertes de dossier de la chaîne anti-fraude, de bout en bout dans le module : signal → base de supervision →
/// routage par les droits → notification, puis centre d'alertes filtré par portée.
/// </summary>
public sealed class DossierAlertTests : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 2, 0, 0, TimeSpan.Zero);
    private static readonly OrganisationId Cabinet = new(Guid.CreateVersion7());
    private static readonly OrganisationId OtherCabinet = new(Guid.CreateVersion7());
    private static readonly GroupId Group = new(Guid.CreateVersion7());
    private static readonly GroupId OtherGroup = new(Guid.CreateVersion7());
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());

    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-supervision-").FullName;
    private readonly FakeDirectory _directory = new();
    private readonly RecordingChannel _channel = new();
    private readonly TestUser _user = new();
    private ServiceProvider _services = null!;

    private Guid _collaborator;
    private Guid _otherGroupCollaborator;
    private Guid _manager;
    private Guid _admin;
    private Guid _portal;
    private Guid _suspended;
    private Guid _otherCabinetManager;

    public async Task InitializeAsync()
    {
        _directory.AddDossier(Dossier, Cabinet, Group);
        _collaborator = _directory.AddUser(Cabinet, RoleTemplates.Collaborateur, AuthorizationScope.ForGroup(Group));
        _otherGroupCollaborator = _directory.AddUser(Cabinet, RoleTemplates.Collaborateur, AuthorizationScope.ForGroup(OtherGroup));
        _manager = _directory.AddUser(Cabinet, RoleTemplates.Manager, AuthorizationScope.ForCabinet(Cabinet));
        _admin = _directory.AddUser(Cabinet, RoleTemplates.AdministrateurCabinet, AuthorizationScope.ForCabinet(Cabinet));
        _portal = _directory.AddUser(Cabinet, RoleTemplates.ClientPortail, AuthorizationScope.ForDossier(Dossier));
        _suspended = _directory.AddUser(Cabinet, RoleTemplates.Manager, AuthorizationScope.ForCabinet(Cabinet), AccountState.Suspended);
        _otherCabinetManager = _directory.AddUser(OtherCabinet, RoleTemplates.Manager, AuthorizationScope.ForCabinet(OtherCabinet));

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(T0));
        services.AddApplication();
        services.AddSupervision(new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddSupervisionDatabase(b => SqliteConfigurator.ConfigureSupervision(b, $"Data Source={Path.Combine(_folder, "supervision.db")};Pooling=False"));
        services.AddFakeDirectory(_directory);
        services.AddSingleton<ICurrentUser>(_user);
        services.AddSingleton<ITenantContext>(new NoDossier());
        services.AddSingleton<INotificationChannel>(_channel);
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await _services.MigrateSupervisionAsync(default);
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    private async Task PublishAsync(DossierAlertRule rule, bool firing, DateTimeOffset at)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDossierAlertPublisher>()
            .PublishAsync(new DossierAlertSignal(rule, Cabinet, Dossier, firing, at), default);
    }

    private async Task<TResult> AsAsync<TResult>(Guid user, Func<IServiceProvider, Task<TResult>> action)
    {
        _user.UserId = user;
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task<Result<AlertCentreResponse>> CentreAsync(Guid user, AuthorizationScope within) =>
        AsAsync(user, sp => sp.GetRequiredService<IQueryDispatcher>().QueryAsync(new ListAlertsQuery(within), default));

    [Fact]
    public async Task Une_chaine_compromise_part_en_S1_vers_les_bonnes_personnes_sans_donnee_de_dossier()
    {
        await PublishAsync(DossierAlertRule.IntegrityChainCompromised, firing: true, T0);

        (AlertNotification notification, IReadOnlyCollection<Recipient> recipients) = _channel.Sent.Should().ContainSingle().Subject;
        notification.Severity.Should().Be(Severity.S1);
        notification.ScopeLevel.Should().Be(ScopeLevel.Dossier);
        notification.Title.Should().Be("Chaîne anti-fraude d'un dossier compromise");
        notification.Link.ToString().Should().Be($"https://localhost/alertes/{notification.AlertId}");
        recipients.Select(r => Guid.Parse(r.Id)).Should().BeEquivalentTo([_collaborator, _manager, _admin]);
        recipients.Select(r => Guid.Parse(r.Id)).Should().NotContain([_otherGroupCollaborator, _portal, _suspended, _otherCabinetManager]);
    }

    [Fact]
    public async Task Le_signal_repete_ne_renotifie_pas_et_un_controle_propre_resout_l_alerte()
    {
        await PublishAsync(DossierAlertRule.IntegrityChainCompromised, firing: true, T0);
        await PublishAsync(DossierAlertRule.IntegrityChainCompromised, firing: true, T0.AddDays(1));
        await PublishAsync(DossierAlertRule.NumberingGap, firing: false, T0.AddDays(1));

        _channel.Sent.Should().ContainSingle();
        AlertSummary alert = (await CentreAsync(_manager, AuthorizationScope.ForCabinet(Cabinet))).Value.Alerts.Should().ContainSingle().Subject;
        alert.Occurrences.Should().Be(2);

        await PublishAsync(DossierAlertRule.IntegrityChainCompromised, firing: false, T0.AddDays(2));

        (await CentreAsync(_manager, AuthorizationScope.ForCabinet(Cabinet))).Value.Alerts.Should().ContainSingle()
            .Which.State.Should().Be(Contracts.Alerts.AlertState.Resolved);
    }

    [Fact]
    public async Task Le_centre_d_alertes_suit_les_droits_de_chacun()
    {
        await PublishAsync(DossierAlertRule.IntegrityChainCompromised, firing: true, T0);
        await PublishAsync(DossierAlertRule.NumberingGap, firing: true, T0);

        AlertCentreResponse dossierView = (await CentreAsync(_collaborator, AuthorizationScope.ForDossier(Dossier))).Value;
        dossierView.Alerts.Select(a => a.Severity).Should().Equal(AlertSeverity.S1, AlertSeverity.S2);
        dossierView.Counters.Single(c => c.Severity == AlertSeverity.S1).Open.Should().Be(1);
        dossierView.Alerts[0].TakeoverDue.Should().Be(T0 + EscalationPolicy.S1Takeover);

        (await CentreAsync(_manager, AuthorizationScope.ForCabinet(Cabinet))).Value.Alerts.Should().HaveCount(2);
        (await CentreAsync(_collaborator, AuthorizationScope.ForCabinet(Cabinet))).Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
        (await CentreAsync(_otherGroupCollaborator, AuthorizationScope.ForDossier(Dossier))).Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
        (await CentreAsync(_portal, AuthorizationScope.ForDossier(Dossier))).Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
        (await CentreAsync(_otherCabinetManager, AuthorizationScope.ForCabinet(OtherCabinet))).Value.Alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_manager_prend_en_charge_dans_son_cabinet_un_collaborateur_ne_peut_pas()
    {
        await PublishAsync(DossierAlertRule.IntegrityChainCompromised, firing: true, T0);
        AlertId id = new((await CentreAsync(_manager, AuthorizationScope.ForCabinet(Cabinet))).Value.Alerts[0].Id);

        Result byCollaborator = await AsAsync(_collaborator, sp => sp.GetRequiredService<ICommandDispatcher>()
            .SendAsync(new AcknowledgeAlertCommand(id, AuthorizationScope.ForDossier(Dossier)), default));
        Result fromOtherCabinet = await AsAsync(_otherCabinetManager, sp => sp.GetRequiredService<ICommandDispatcher>()
            .SendAsync(new AcknowledgeAlertCommand(id, AuthorizationScope.ForCabinet(OtherCabinet)), default));
        Result byManager = await AsAsync(_manager, sp => sp.GetRequiredService<ICommandDispatcher>()
            .SendAsync(new AcknowledgeAlertCommand(id, AuthorizationScope.ForCabinet(Cabinet)), default));

        byCollaborator.Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
        fromOtherCabinet.Errors.Should().ContainSingle().Which.Code.Should().Be(SupervisionErrors.NotFoundCode);
        byManager.IsSuccess.Should().BeTrue();
        AlertDetailResponse detail = (await AsAsync(_manager, sp => sp.GetRequiredService<IQueryDispatcher>()
            .QueryAsync(new GetAlertQuery(id, AuthorizationScope.ForCabinet(Cabinet)), default))).Value;
        detail.Alert.State.Should().Be(Contracts.Alerts.AlertState.Acknowledged);
        detail.Alert.AcknowledgedBy.Should().Be(_manager.ToString());
        detail.Timeline.Select(t => t.Kind).Should().Equal("Raised", "Acknowledged");
    }

    /// <summary>Le centre d'alertes n'ouvre aucune base de dossier.</summary>
    private sealed class NoDossier : ITenantContext
    {
        public TenantDescriptor Current => throw new InvalidOperationException("Aucun dossier ouvert.");

        public bool IsResolved => false;
    }

    private sealed class TestUser : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public Guid UserId { get; set; }
    }

    private sealed class RecordingChannel : INotificationChannel
    {
        public List<(AlertNotification Notification, IReadOnlyCollection<Recipient> Recipients)> Sent { get; } = [];

        public string Name => "test";

        public Task SendAsync(AlertNotification notification, IReadOnlyCollection<Recipient> recipients, CancellationToken cancellationToken)
        {
            Sent.Add((notification, recipients));
            return Task.CompletedTask;
        }
    }
}
