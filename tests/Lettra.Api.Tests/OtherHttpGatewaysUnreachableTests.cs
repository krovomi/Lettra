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
            (await customers.SearchAsync(new PartyListQuery("martin", 1, 10), default)).Errors,
            (await customers.UpdateAsync(id, 1, CustomerApiTests.Martin(), default)).Errors,
            (await customers.DeleteAsync(id, 1, default)).Errors,
            (await session.GetAsync(default)).Errors,
        ];

        failures.Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable));
        (await session.CreateLocalAccountAsync(new CreateLocalAccountRequest(), default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LocalAccount.NotSupported);
        (await session.UnlockAsync(new UnlockLocalSessionRequest(), default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LocalAccount.NotSupported);
        (await session.LockAsync(default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LocalAccount.NotSupported);
    }

    [Fact]
    public async Task Un_delai_d_attente_depasse_rend_Api_Unreachable_comme_une_coupure()
    {
        // Le HttpClient annule la requête à l'expiration de son délai : sans annulation demandée par l'écran.
        await using ServiceProvider services = GatewayHarness.Answering(_ => throw new TaskCanceledException("Délai dépassé (test)."));
        ICustomersGateway customers = services.GetRequiredService<ICustomersGateway>();
        IInvoicesGateway invoices = services.GetRequiredService<IInvoicesGateway>();
        IAlertsGateway alerts = services.GetRequiredService<IAlertsGateway>();
        ICabinetGateway cabinet = services.GetRequiredService<ICabinetGateway>();
        ISessionGateway session = services.GetRequiredService<ISessionGateway>();
        IFecGateway fec = services.GetRequiredService<IFecGateway>();
        Guid id = Guid.CreateVersion7();

        List<IReadOnlyList<ErrorDetail>> failures =
        [
            (await customers.GetAsync(id, default)).Errors,
            (await customers.DeleteAsync(id, 1, default)).Errors,
            (await invoices.DeleteAsync(id, 1, default)).Errors,
            (await alerts.GetAsync(id, false, default)).Errors,
            (await cabinet.GetOverviewAsync(default)).Errors,
            (await session.GetAsync(default)).Errors,
            (await fec.ExportAsync(Today, Today, default)).Errors,
        ];

        failures.Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable));
    }

    [Fact]
    public async Task Une_annulation_demandee_par_l_ecran_remonte_telle_quelle()
    {
        await using ServiceProvider services = GatewayHarness.Answering(_ => throw new TaskCanceledException("Annulé (test)."));
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        Func<Task> get = () => services.GetRequiredService<ICustomersGateway>().GetAsync(Guid.CreateVersion7(), cancelled.Token);

        await get.Should().ThrowAsync<OperationCanceledException>();
    }
}
