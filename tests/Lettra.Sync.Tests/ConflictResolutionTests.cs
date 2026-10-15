using Lettra.Application.Abstractions.Sync;
using Lettra.Contracts.Customers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Sync.Client;
using Microsoft.Extensions.DependencyInjection;
using static Lettra.Sync.Tests.SynchronisationTests;

namespace Lettra.Sync.Tests;

/// <summary>
/// Ce que les écrans de l'étape 4.3 demandent au poste : trancher un champ disputé, garder la préférence, relancer la
/// synchronisation au retour du réseau.
/// </summary>
public sealed class ConflictResolutionTests : IAsyncLifetime
{
    private readonly SyncWorld _world = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _world.DisposeAsync();

    /// <summary>Le nom du client changé des deux côtés : le cabinet garde le sien, le poste attend l'utilisateur.</summary>
    private async Task<(DeviceNode Poste, CustomerId Id, SyncConflictRecord Conflict)> ConflictAsync(Cut? cut = null)
    {
        DeviceNode poste = _world.Device();
        CustomerId id = await CreateAsync(_world.Server, Martin());
        await poste.SyncAsync();
        await UpdateAsync(_world.Server, id, r => r with { Name = "Boulangerie Martin et Fils" });
        await UpdateAsync(poste, id, r => r with { Name = "Martin SARL" });
        poste.Transport.CutNext = cut;
        await poste.SyncAsync();
        return (poste, id, (await poste.ConflictsAsync()).Should().ContainSingle().Subject);
    }

    [Fact]
    public async Task Garder_ma_saisie_la_renvoie_sur_la_version_du_cabinet_et_le_serveur_la_prend()
    {
        (DeviceNode poste, CustomerId id, SyncConflictRecord conflict) = await ConflictAsync();
        poste.Coordinator.State.Should().Be(SyncState.ResolvingConflicts);

        (await poste.Coordinator.ResolveConflictAsync(conflict.Id, SyncResolution.KeepMine)).IsSuccess.Should().BeTrue();

        (await poste.ConflictsAsync()).Should().BeEmpty();
        poste.Coordinator.State.Should().Be(SyncState.UpToDate, "plus rien à trancher");
        (await CustomerAsync(poste, id))!.Name.Should().Be("Martin SARL");

        SyncReport report = await poste.SyncAsync();
        report.Conflicts.Should().Be(0, "la saisie repart sur la version du cabinet : plus de dispute");
        CustomerResponse server = (await CustomerAsync(_world.Server, id))!;
        server.Name.Should().Be("Martin SARL");
        (await CustomerAsync(poste, id)).Should().BeEquivalentTo(server);
    }

    [Fact]
    public async Task Prendre_celle_du_cabinet_efface_le_conflit_sans_rien_renvoyer()
    {
        (DeviceNode poste, CustomerId id, SyncConflictRecord conflict) = await ConflictAsync();

        (await poste.Coordinator.ResolveConflictAsync(conflict.Id, SyncResolution.TakeServer)).IsSuccess.Should().BeTrue();

        (await poste.ConflictsAsync()).Should().BeEmpty();
        int asked = poste.Consent.Asked;
        SyncReport report = await poste.SyncAsync();
        report.Sent.Should().Be(0);
        poste.Consent.Asked.Should().Be(asked, "rien ne part : pas de question");
        (await CustomerAsync(poste, id))!.Name.Should().Be("Boulangerie Martin et Fils");
        (await CustomerAsync(_world.Server, id))!.Name.Should().Be("Boulangerie Martin et Fils");
    }

    [Fact]
    public async Task Sans_la_version_du_cabinet_garder_ma_saisie_attend_la_prochaine_reception()
    {
        (DeviceNode poste, CustomerId id, SyncConflictRecord conflict) = await ConflictAsync(Cut.PullRequest);
        (await CustomerAsync(poste, id))!.Name.Should().Be("Martin SARL", "la réception a échoué après l'envoi");

        Result early = await poste.Coordinator.ResolveConflictAsync(conflict.Id, SyncResolution.KeepMine);
        early.Errors.Should().ContainSingle(e => e.Code == "Sync.ConflictAwaitingRefresh");
        (await poste.ConflictsAsync()).Should().ContainSingle("le conflit reste à trancher");

        await poste.SyncAsync();
        (await CustomerAsync(poste, id))!.Name.Should().Be("Boulangerie Martin et Fils");
        (await poste.Coordinator.ResolveConflictAsync(conflict.Id, SyncResolution.KeepMine)).IsSuccess.Should().BeTrue();
        await poste.SyncAsync();
        (await CustomerAsync(_world.Server, id))!.Name.Should().Be("Martin SARL");
    }

    [Fact]
    public async Task Un_conflit_deja_tranche_n_est_plus_trouve()
    {
        (DeviceNode poste, _, SyncConflictRecord conflict) = await ConflictAsync();
        await poste.Coordinator.ResolveConflictAsync(conflict.Id, SyncResolution.TakeServer);

        (await poste.Coordinator.ResolveConflictAsync(conflict.Id, SyncResolution.KeepMine))
            .Errors.Should().ContainSingle(e => e.Code == "Sync.EntityNotFound");
    }

    [Fact]
    public async Task La_preference_est_gardee_sur_le_poste_et_demander_est_le_defaut()
    {
        DeviceNode poste = _world.Device();
        (await poste.Preference.GetAsync(default)).Should().Be(SyncPreference.AlwaysAsk);

        await poste.Preference.SetAsync(SyncPreference.NeverOnCellular, default);
        await poste.Preference.SetAsync(SyncPreference.AutoOnWifi, default);

        string? stored = await poste.RunAsync(sp => sp.GetRequiredService<ISyncReplica>().GetSettingAsync("client.preference", default));
        stored.Should().Be(nameof(SyncPreference.AutoOnWifi), "la réplique la garde : elle survit au redémarrage");
        (await poste.Preference.GetAsync(default)).Should().Be(SyncPreference.AutoOnWifi);
    }

    [Fact]
    public async Task Le_retour_du_reseau_lance_un_cycle_et_previent_l_interface()
    {
        DeviceNode poste = _world.Device();
        await CreateAsync(poste, Martin());
        int changes = 0;
        poste.Coordinator.Changed += (_, _) => changes++;

        SyncReport? report = await poste.Coordinator.NetworkAvailableAsync();

        report.Should().NotBeNull();
        report!.Sent.Should().Be(1);
        poste.Coordinator.LastReport.Should().Be(report);
        changes.Should().BeGreaterThan(1, "chaque état traversé et la fin du cycle sont annoncés");
    }
}
