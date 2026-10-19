using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.Contracts.Speech;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Les autres gateways HTTP du navigateur, serveur coupé : chaque appel rend <c>Api.Unreachable</c>, jamais une
/// exception à l'écran ; la session du navigateur n'a pas de compte local.
/// </summary>
public sealed class OtherHttpGatewaysUnreachableTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Serveur_injoignable_comptabilite_cabinet_exports_voix_clients_et_session_rendent_Api_Unreachable()
    {
        await using ServiceProvider services = GatewayHarness.Unreachable();
        IAccountingGateway accounting = services.GetRequiredService<IAccountingGateway>();
        ICabinetGateway cabinet = services.GetRequiredService<ICabinetGateway>();
        IFecGateway fec = services.GetRequiredService<IFecGateway>();
        ISpeechGateway speech = services.GetRequiredService<ISpeechGateway>();
        ICustomersGateway customers = services.GetRequiredService<ICustomersGateway>();
        ISessionGateway session = services.GetRequiredService<ISessionGateway>();
        Guid id = Guid.CreateVersion7();

        List<IReadOnlyList<ErrorDetail>> failures =
        [
            (await accounting.GetJournalAsync(new JournalQuery(Today, Today, "x", "411", id), default)).Errors,
            (await accounting.GetBalanceAsync(Today, Today, default)).Errors,
            (await accounting.GetLedgerAsync("411", Today, Today, id, default)).Errors,
            (await accounting.GetLedgerAsync("512", Today, Today, null, default)).Errors,
            (await cabinet.ListDossiersAsync(default)).Errors,
            (await cabinet.GetOverviewAsync(default)).Errors,
            (await cabinet.RefreshOverviewAsync(id, default)).Errors,
            (await cabinet.RefreshOverviewAsync(null, default)).Errors,
            (await cabinet.GetReviewAsync("2026-10", default)).Errors,
            (await cabinet.SetReviewItemAsync("2026-10", "banque", new SetDossierReviewItemRequest { Checked = true }, default)).Errors,
            (await fec.ExportAsync(Today, Today, default)).Errors,
            (await fec.ValidateAsync("FEC.txt", [1, 2, 3], Today, Today, default)).Errors,
            (await fec.ValidateAsync("FEC.txt", [1, 2, 3], null, null, default)).Errors,
            (await speech.GetStatusAsync(default)).Errors,
            (await speech.TranscribeAsync(new byte[] { 1, 2, 3 }, SpeechMode.Dictation, "fr", default)).Errors,
            (await customers.CreateAsync(CustomerApiTests.Martin(), default)).Errors,
            (await customers.ListAsync(1, 10, default)).Errors,
            (await customers.GetAsync(id, default)).Errors,
            (await customers.SearchAsync("martin", 1, 10, default)).Errors,
            (await customers.UpdateAsync(id, 1, CustomerApiTests.Martin(), default)).Errors,
            (await customers.DeleteAsync(id, 1, default)).Errors,
            (await session.GetAsync(default)).Errors,
        ];

        failures.Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable));
        (await session.CreateLocalAccountAsync(new CreateLocalAccountRequest(), default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LocalAccount.NotSupported);
        (await session.UnlockAsync(new UnlockLocalSessionRequest(), default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LocalAccount.NotSupported);
        (await session.LockAsync(default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LocalAccount.NotSupported);
    }
}
