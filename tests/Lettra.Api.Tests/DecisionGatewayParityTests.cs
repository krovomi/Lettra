using System.Net;
using Lettra.Contracts.Common;
using Lettra.Contracts.Decisions;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// « Décisions à prendre » par la même interface, en HTTP et en direct : un IBAN de fournisseur changé devient une
/// décision à l'actualisation, se remet à plus tard, s'ignore, revient dans la liste, et se règle d'elle-même quand le
/// changement est vérifié. Le cadran se règle avec sa version ; chacun ne voit que ce qu'il peut traiter.
/// </summary>
public sealed class DecisionGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private const string OtherFrenchIban = "FR14 2004 1010 0505 0001 3M02 606";

    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    private static DateOnly Today => LegalCalendar.DateOf(DateTimeOffset.UtcNow);

    /// <summary>Un fournisseur neuf dont l'IBAN vient de changer : le changement attend sa vérification.</summary>
    private static async Task<(string Name, IbanChangeResponse Change)> PendingIbanChangeAsync(ISuppliersGateway suppliers)
    {
        CreateSupplierRequest request = PartiesAndCatalogApiTests.Dupuis();
        request.Name = "Minoterie " + Guid.CreateVersion7().ToString("N")[^8..];
        SupplierResponse created = (await suppliers.CreateAsync(request, default)).Value!;
        request.Iban = OtherFrenchIban;
        SupplierResponse changed = (await suppliers.UpdateAsync(created.Id, created.Version, request, default)).Value!;
        return (request.Name, changed.PendingIbanChange!);
    }

    private static DecisionRow? RowOf(OperationResult<DecisionsResponse> list, string supplier) =>
        list.Value!.Rows.SingleOrDefault(r => r.Kind == "iban.a-verifier" && r.Facts.TryGetValue("supplier", out string? name) && name == supplier);

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Un_iban_change_devient_une_decision_qui_se_remet_s_ignore_et_revient(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IDecisionsGateway decisions = services.GetRequiredService<IDecisionsGateway>();
        (string supplier, _) = await PendingIbanChangeAsync(services.GetRequiredService<ISuppliersGateway>());

        OperationResult<DecisionsResponse> refreshed = await decisions.RefreshAsync(default);

        refreshed.Value!.RefreshedAt.Should().NotBeNull();
        refreshed.Value.CanConfigure.Should().BeTrue();
        refreshed.Value.Level.Should().Be(AutonomyLevelDto.Prepare);
        DecisionRow row = RowOf(refreshed, supplier)!;
        row.Should().Match<DecisionRow>(r =>
            r.Status == DecisionStatusDto.Proposed && r.Urgency == DecisionUrgencyDto.High && r.Audience == DecisionAudienceDto.Entreprise
            && r.Link == "iban-changes" && !r.CanAccept && r.ClosedAt == null);

        OperationResult<DecisionsResponse> withoutDate = await decisions.AnswerAsync(row.Id, new AnswerDecisionRequest { Choice = DecisionChoiceDto.Snooze }, row.Version, default);
        OperationResult<DecisionsResponse> nothingToDo = await decisions.AnswerAsync(row.Id, new AnswerDecisionRequest { Choice = DecisionChoiceDto.Accept }, row.Version, default);
        OperationResult<DecisionsResponse> snoozed = await decisions.AnswerAsync(
            row.Id, new AnswerDecisionRequest { Choice = DecisionChoiceDto.Snooze, SnoozeUntil = Today.AddDays(7) }, row.Version, default);
        OperationResult<DecisionsResponse> stale = await decisions.AnswerAsync(row.Id, new AnswerDecisionRequest { Choice = DecisionChoiceDto.Dismiss }, row.Version, default);

        withoutDate.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Decision.SnoozeDateInvalid);
        nothingToDo.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Decision.NoAction, "vérifier un IBAN se fait à la main, par un autre canal");
        DecisionRow later = RowOf(snoozed, supplier)!;
        later.Status.Should().Be(DecisionStatusDto.Snoozed);
        later.SnoozedUntil.Should().Be(Today.AddDays(7));
        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        RowOf(OperationResult<DecisionsResponse>.Success(stale.Current!), supplier)!.Version.Should().Be(later.Version);

        OperationResult<DecisionsResponse> restored = await decisions.AnswerAsync(later.Id, new AnswerDecisionRequest { Choice = DecisionChoiceDto.Restore }, later.Version, default);
        DecisionRow back = RowOf(restored, supplier)!;
        OperationResult<DecisionsResponse> dismissed = await decisions.AnswerAsync(back.Id, new AnswerDecisionRequest { Choice = DecisionChoiceDto.Dismiss }, back.Version, default);
        DecisionRow closed = RowOf(dismissed, supplier)!;
        OperationResult<DecisionsResponse> acceptClosed = await decisions.AnswerAsync(closed.Id, new AnswerDecisionRequest { Choice = DecisionChoiceDto.Accept }, null, default);
        OperationResult<DecisionsResponse> unknown = await decisions.AnswerAsync(Guid.CreateVersion7(), new AnswerDecisionRequest { Choice = DecisionChoiceDto.Dismiss }, null, default);
        OperationResult<DecisionsResponse> stillDismissed = await decisions.RefreshAsync(default);

        back.Status.Should().Be(DecisionStatusDto.Proposed);
        closed.Status.Should().Be(DecisionStatusDto.Dismissed);
        closed.ClosedAt.Should().NotBeNull();
        acceptClosed.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Decision.NotOpen);
        unknown.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Decision.NotFound);
        RowOf(stillDismissed, supplier)!.Status.Should().Be(DecisionStatusDto.Dismissed, "une décision ignorée ne revient pas d'elle-même");
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Une_decision_se_regle_d_elle_meme_quand_son_sujet_est_traite(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IDecisionsGateway decisions = services.GetRequiredService<IDecisionsGateway>();
        (string supplier, IbanChangeResponse change) = await PendingIbanChangeAsync(services.GetRequiredService<ISuppliersGateway>());
        RowOf(await decisions.RefreshAsync(default), supplier)!.Status.Should().Be(DecisionStatusDto.Proposed);

        (await services.GetRequiredService<IIbanChangesGateway>().ConfirmAsync(
            change.Id, new ConfirmIbanChangeRequest { Method = IbanVerificationMethodDto.KnownPhoneNumber, ExpectedVersion = change.Version }, default)).Value.Should().BeTrue();
        OperationResult<DecisionsResponse> after = await decisions.RefreshAsync(default);

        DecisionRow resolved = RowOf(after, supplier)!;
        resolved.Status.Should().Be(DecisionStatusDto.Resolved);
        resolved.ClosedAt.Should().NotBeNull();
        after.Value!.Rows.TakeWhile(r => r.Status == DecisionStatusDto.Proposed).Should().NotContain(resolved, "ce qui est traité passe après ce qui reste à décider");
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Le_cadran_se_regle_avec_sa_version_et_coupe_les_actions_preparees(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IDecisionsGateway decisions = services.GetRequiredService<IDecisionsGateway>();
        DecisionsResponse before = (await decisions.RefreshAsync(default)).Value!;

        OperationResult<DecisionsResponse> shown = await decisions.SetPolicyAsync(new SetDecisionPolicyRequest { Level = AutonomyLevelDto.Show }, before.PolicyVersion, default);
        OperationResult<DecisionsResponse> stale = await decisions.SetPolicyAsync(new SetDecisionPolicyRequest { Level = AutonomyLevelDto.Prepare }, before.PolicyVersion, default);
        OperationResult<DecisionsResponse> invalid = await decisions.SetPolicyAsync(new SetDecisionPolicyRequest { Level = (AutonomyLevelDto)7 }, shown.Value!.PolicyVersion, default);
        OperationResult<DecisionsResponse> prepared = await decisions.SetPolicyAsync(new SetDecisionPolicyRequest { Level = AutonomyLevelDto.Prepare }, shown.Value.PolicyVersion, default);

        shown.Value.Level.Should().Be(AutonomyLevelDto.Show);
        shown.Value.PolicyVersion.Should().BeGreaterThan(before.PolicyVersion!.Value);
        shown.Value.Rows.Should().NotContain(r => r.CanAccept, "au niveau « montrer », rien n'est préparé");
        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        stale.Current!.Level.Should().Be(AutonomyLevelDto.Show);
        invalid.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Decision.LevelInvalid);
        prepared.Value!.Level.Should().Be(AutonomyLevelDto.Prepare);
    }

    [Fact]
    public async Task Le_collaborateur_traite_sans_regler_le_cadran_et_le_portail_ne_voit_rien()
    {
        await using ServiceProvider collaborateur = _harness.HttpGateway(LettraApiFactory.DossierId, LettraApiFactory.CollaborateurId);
        IDecisionsGateway decisions = collaborateur.GetRequiredService<IDecisionsGateway>();

        OperationResult<DecisionsResponse> list = await decisions.ListAsync(default);
        OperationResult<DecisionsResponse> refused = await decisions.SetPolicyAsync(new SetDecisionPolicyRequest { Level = AutonomyLevelDto.Show }, null, default);

        list.Value!.CanConfigure.Should().BeFalse();
        refused.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden);

        using HttpClient portal = factory.ClientFor(LettraApiFactory.PortailId);
        portal.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        using HttpResponseMessage response = await portal.GetAsync(new Uri("/api/v1/decisions", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Serveur_injoignable_chaque_appel_rend_Api_Unreachable()
    {
        await using ServiceProvider services = GatewayHarness.Unreachable();
        IDecisionsGateway decisions = services.GetRequiredService<IDecisionsGateway>();

        List<IReadOnlyList<ErrorDetail>> failures =
        [
            (await decisions.ListAsync(default)).Errors,
            (await decisions.RefreshAsync(default)).Errors,
            (await decisions.AnswerAsync(Guid.CreateVersion7(), new AnswerDecisionRequest { Choice = DecisionChoiceDto.Dismiss }, 1, default)).Errors,
            (await decisions.SetPolicyAsync(new SetDecisionPolicyRequest(), null, default)).Errors,
        ];

        failures.Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable));
    }

    public void Dispose() => _harness.Dispose();
}
