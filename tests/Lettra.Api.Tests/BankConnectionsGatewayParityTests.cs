using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>Banque connectée : par l'API comme sur le poste, même parcours (consentement, relecture, déconnexion) et mêmes refus.</summary>
public sealed partial class GatewayParityTests
{
    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Banque_connectee_meme_parcours(string transport)
    {
        await using ServiceProvider services = Gateway(transport);
        IBankConnectionsGateway bank = services.GetRequiredService<IBankConnectionsGateway>();

        BankConnectionsResponse offer = (await bank.GetAsync(default)).Value!;
        OperationResult<BankConsentRedirectResponse> unknown = await bank.StartAsync(new StartBankConnectionRequest { InstitutionId = "inconnue" }, default);
        BankConsentRedirectResponse consent = (await bank.StartAsync(new StartBankConnectionRequest { InstitutionId = "sim-neo" }, default)).Value!;
        OperationResult<BankConnectionCompletedResponse> refused = await bank.CompleteAsync(
            consent.ConnectionId, new CompleteBankConnectionRequest { Code = BankConnectionRoutes.SimulatedRefusedCode }, default);
        BankConnectionCompletedResponse linked = (await bank.CompleteAsync(
            consent.ConnectionId, new CompleteBankConnectionRequest { Code = BankConnectionRoutes.SimulatedGrantedCode }, default)).Value!;
        SyncBankConnectionsResponse again = (await bank.SyncAsync(new SyncBankConnectionsRequest { ConnectionId = linked.Connection.Id }, default)).Value!;
        BankConnectionResponse revoked = (await bank.RevokeAsync(linked.Connection.Id, default)).Value!;
        OperationResult<BankConnectionResponse> missing = await bank.RevokeAsync(Guid.CreateVersion7(), default);

        offer.Available.Should().BeTrue();
        unknown.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.BankConnection.InstitutionUnknown);
        consent.RedirectUrl.Should().StartWith(BankConnectionRoutes.SimulatedConsent);
        refused.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.BankConnection.ConsentRefused);
        linked.Connection.InstitutionName.Should().Be("Néo Compte (simulé)");
        linked.FirstSync.Imported.Should().BeGreaterThan(0);
        again.Outcomes.Should().ContainSingle().Which.Imported.Should().Be(0);
        revoked.Status.Should().Be(BankConnectionStatusDto.Revoked);
        missing.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.BankConnection.NotFound);
    }
}
