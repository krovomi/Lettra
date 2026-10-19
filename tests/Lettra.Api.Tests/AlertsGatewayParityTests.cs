using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Alerts;
using Lettra.Contracts.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Supervision.Application;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Centre d'alertes du dossier par la même interface, en HTTP et en direct : une alerte de dossier levée par la règle
/// fiscale se lit, se prend en charge, change de gravité et se résout ; une alerte inconnue est refusée de même.
/// </summary>
public sealed class AlertsGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    /// <summary>Lève l'alerte « échéance fiscale proche » du dossier ouvert, comme le ferait le contrôle du calendrier.</summary>
    private async Task RaiseAsync(string transport, IServiceProvider gateways)
    {
        IServiceProvider host = transport == "http" ? factory.Services : gateways;
        await using AsyncServiceScope scope = host.CreateAsyncScope();
        (OrganisationId cabinet, DossierId dossier) = (new OrganisationId(LettraApiFactory.CabinetId), new DossierId(LettraApiFactory.DossierId));
        if (transport != "http")
        {
            (await scope.ServiceProvider.GetRequiredService<ITenantInitializer>().InitializeAsync(default)).IsSuccess.Should().BeTrue();
            TenantDescriptor current = scope.ServiceProvider.GetRequiredService<ITenantContext>().Current;
            (cabinet, dossier) = (current.OrganisationId, current.DossierId);
        }

        await scope.ServiceProvider.GetRequiredService<IDossierAlertPublisher>().PublishAsync(
            new DossierAlertSignal(DossierAlertRule.FiscalDeadlineSoon, cabinet, dossier, Firing: true, DateTimeOffset.UtcNow), default);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Une_alerte_de_dossier_se_lit_se_prend_en_charge_change_de_gravite_puis_se_resout(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IAlertsGateway alerts = services.GetRequiredService<IAlertsGateway>();
        await RaiseAsync(transport, services);

        OperationResult<AlertCentreResponse> centre = await alerts.ListAsync(cabinetWide: false, includeClosed: false, default);
        centre.IsSuccess.Should().BeTrue(GatewayHarness.Codes(centre));
        AlertSummary alert = centre.Value!.Alerts.Should().ContainSingle(a => a.RuleId == DossierAlertPublisher.FiscalSoonRuleId).Subject;
        OperationResult<AlertCentreResponse> cabinetCentre = await alerts.ListAsync(cabinetWide: true, includeClosed: true, default);
        OperationResult<AlertDetailResponse> detail = await alerts.GetAsync(alert.Id, cabinetWide: false, default);
        OperationResult<AlertDetailResponse> acknowledged = await alerts.AcknowledgeAsync(alert.Id, default);
        OperationResult<AlertDetailResponse> noReason = await alerts.ChangeSeverityAsync(alert.Id, new ChangeAlertSeverityRequest { Severity = AlertSeverity.S2, Reason = "" }, default);
        OperationResult<AlertDetailResponse> raised = await alerts.ChangeSeverityAsync(
            alert.Id, new ChangeAlertSeverityRequest { Severity = AlertSeverity.S2, Reason = "Échéance de TVA du mois" }, default);
        OperationResult<AlertDetailResponse> resolved = await alerts.ResolveAsync(alert.Id, default);

        alert.Severity.Should().Be(AlertSeverity.S3);
        cabinetCentre.Value!.Alerts.Should().Contain(a => a.Id == alert.Id);
        detail.Value!.Alert.Id.Should().Be(alert.Id);
        acknowledged.Value!.Alert.State.Should().Be(AlertState.Acknowledged);
        noReason.Errors.Select(e => e.Code).Should().Contain(ErrorCodes.Alerts.ReasonRequired);
        raised.Value!.Alert.Severity.Should().Be(AlertSeverity.S2);
        resolved.Value!.Alert.State.Should().Be(AlertState.Resolved);
        (await alerts.ResolveAsync(alert.Id, default)).Errors.Select(e => e.Code).Should().ContainSingle().Which.Should().Be(ErrorCodes.Alerts.InvalidTransition);
        (await alerts.ListAsync(cabinetWide: false, includeClosed: false, default)).Value!.Alerts.Should().NotContain(a => a.Id == alert.Id && a.State == AlertState.New);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Une_alerte_inconnue_est_refusee_de_meme(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IAlertsGateway alerts = services.GetRequiredService<IAlertsGateway>();
        Guid id = Guid.CreateVersion7();

        List<IReadOnlyList<ErrorDetail>> failures =
        [
            (await alerts.GetAsync(id, cabinetWide: false, default)).Errors,
            (await alerts.GetAsync(id, cabinetWide: true, default)).Errors,
            (await alerts.AcknowledgeAsync(id, default)).Errors,
            (await alerts.ResolveAsync(id, default)).Errors,
            (await alerts.ChangeSeverityAsync(id, new ChangeAlertSeverityRequest { Severity = AlertSeverity.S1, Reason = "Test" }, default)).Errors,
        ];

        failures.Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Alerts.NotFound));
    }

    [Fact]
    public async Task Serveur_injoignable_chaque_appel_rend_Api_Unreachable()
    {
        await using ServiceProvider services = GatewayHarness.Unreachable();
        IAlertsGateway alerts = services.GetRequiredService<IAlertsGateway>();
        Guid id = Guid.CreateVersion7();

        List<IReadOnlyList<ErrorDetail>> failures =
        [
            (await alerts.ListAsync(cabinetWide: true, includeClosed: true, default)).Errors,
            (await alerts.GetAsync(id, cabinetWide: false, default)).Errors,
            (await alerts.AcknowledgeAsync(id, default)).Errors,
            (await alerts.ResolveAsync(id, default)).Errors,
            (await alerts.ChangeSeverityAsync(id, new ChangeAlertSeverityRequest { Reason = "Test" }, default)).Errors,
        ];

        failures.Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable));
    }

    public void Dispose() => _harness.Dispose();
}
