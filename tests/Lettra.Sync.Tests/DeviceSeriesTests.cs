using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Sync;
using Lettra.Domain.Common;
using Lettra.Sync.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Sync.Tests;

/// <summary>Séries de numérotation par poste (ADR-0007) : ouvertes au premier passage en ligne, une par poste.</summary>
public sealed class DeviceSeriesTests : IAsyncLifetime
{
    private readonly SyncWorld _world = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _world.DisposeAsync();

    private static Task<DeviceSeries?> SeriesAsync(DossierNode node) =>
        node.RunAsync(sp => sp.GetRequiredService<IDeviceNumbering>().FindAsync(NumberSeriesCodes.Invoices, default));

    [Fact]
    public async Task Chaque_poste_recoit_sa_serie_au_premier_passage_en_ligne_et_la_garde()
    {
        DeviceNode first = _world.Device();
        DeviceNode second = _world.Device();
        (await SeriesAsync(first)).Should().BeNull("un poste qui n'est jamais passé en ligne ne valide pas hors ligne");

        (await first.SyncAsync()).State.Should().Be(SyncState.UpToDate);
        (await second.SyncAsync()).State.Should().Be(SyncState.UpToDate);
        (await first.SyncAsync()).State.Should().Be(SyncState.UpToDate);

        DeviceSeries? one = await SeriesAsync(first);
        DeviceSeries? two = await SeriesAsync(second);
        one.Should().BeEquivalentTo(new { BaseSeries = "FAC", Code = "FAC-P01", Prefix = "FAC-P01-", Next = 1L });
        two.Should().BeEquivalentTo(new { Code = "FAC-P02", Prefix = "FAC-P02-" });
        first.Transport.SeriesRequests.Should().Be(1, "la série est demandée une fois puis gardée sur le poste");
        (await SeriesAsync(_world.Server)).Should().BeNull("le serveur numérote toujours dans la série du dossier");
    }

    [Fact]
    public async Task La_serie_rendue_au_meme_poste_est_la_meme_et_le_numero_attendu_suit()
    {
        DeviceNode device = _world.Device();
        await device.SyncAsync();
        DeviceSeries mine = (await SeriesAsync(device))!;

        DeviceSeries again = await _world.Server.RunAsync(sp =>
            sp.GetRequiredService<IDeviceSeriesRegistry>().OpenAsync(mine.DeviceId, "FAC", _world.Clock.GetUtcNow(), default));
        again.Code.Should().Be(mine.Code);

        IDeviceSeriesRegistry Registry(IServiceProvider sp) => sp.GetRequiredService<IDeviceSeriesRegistry>();
        Task<Result<bool>> Receive(string number, Guid? device = null) => _world.Server.RunAsync(sp =>
            Registry(sp).ExecuteWithDeviceNumberAsync(mine.Code, device ?? mine.DeviceId, number, _ => Task.FromResult<Result<bool>>(true), default));

        (await Receive("FAC-P01-000002")).Errors.Should().ContainSingle(e => e.Code == "Sync.NumberOutOfOrder");
        (await Receive("FAC-P01-000001", Guid.CreateVersion7())).Errors.Should().ContainSingle(e => e.Code == "Sync.DeviceSeriesUnknown");
        (await Receive("FAC-P01-000001")).IsSuccess.Should().BeTrue();
        (await Receive("FAC-P01-000001")).Errors.Should().ContainSingle(e => e.Code == "Sync.NumberOutOfOrder", "un numéro n'est reçu qu'une fois");

        Result<bool> refused = await _world.Server.RunAsync(sp =>
            Registry(sp).ExecuteWithDeviceNumberAsync(mine.Code, mine.DeviceId, "FAC-P01-000002", _ => Task.FromResult(Result<bool>.Failure(SyncErrors.EntityNotFound)), default));
        refused.IsFailure.Should().BeTrue();
        (await Receive("FAC-P01-000002")).IsSuccess.Should().BeTrue("une réception refusée ne consomme pas le numéro");
    }

    [Fact]
    public async Task Sans_droit_de_valider_pas_de_serie_et_la_synchronisation_reussit()
    {
        DeviceNode device = _world.Device();
        _world.Server.Permissions.Granted.Remove(Permission.FactureValider);

        (await device.SyncAsync()).State.Should().Be(SyncState.UpToDate);
        (await SeriesAsync(device)).Should().BeNull();
    }
}
