using Lettra.Application.Abstractions.Identity;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using static Lettra.Sync.Tests.SynchronisationTests;

namespace Lettra.Sync.Tests;

/// <summary>L'adaptateur des écrans de synchronisation (étape 4.3) sur le vrai coordinateur du poste.</summary>
public sealed class SyncCenterTests : IAsyncLifetime
{
    private readonly SyncWorld _world = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _world.DisposeAsync();

    [Fact]
    public async Task Une_saisie_hors_ligne_apparait_en_attente_avec_son_libelle_et_part_avec_l_accord_du_dialogue()
    {
        DeviceNode poste = _world.Device(center: true);
        ISyncCenter center = poste.Center;
        int changed = 0;
        center.Changed += (_, _) => Interlocked.Increment(ref changed);

        await CreateAsync(poste, Martin());

        await WaitForAsync(() => center.Current.Pending.Creations == 1);
        changed.Should().BeGreaterThan(0, "la pastille est prévenue de la saisie");
        SyncPendingItem pending = (await center.ListPendingAsync(default)).Should().ContainSingle().Subject;
        pending.Should().BeEquivalentTo(new { Item = SyncItemKind.Customer, Change = SyncChangeKind.Creation, Label = "Boulangerie Martin" });

        poste.Dialog.Answer = SyncAnswer.Later;
        SyncCycleReport later = await center.SyncNowAsync(default);
        later.Postponed.Should().BeTrue();
        poste.Dialog.LastSummary!.Creations.Should().Be(1);
        poste.Dialog.LastNetwork.Should().Be(SyncNetwork.Wifi);

        poste.Dialog.Answer = SyncAnswer.Now;
        SyncCycleReport report = await center.SyncNowAsync(default);
        report.Sent.Should().Be(1);
        report.Status.Should().Be(SyncStatus.UpToDate);
        center.Current.Status.Should().Be(SyncStatus.UpToDate);
        center.Current.Pending.Total.Should().Be(0);
        center.Current.LastReport.Should().Be(report);
    }

    [Fact]
    public async Task Un_conflit_se_lit_et_se_tranche_par_l_ecran()
    {
        DeviceNode poste = _world.Device(center: true);
        CustomerId id = await CreateAsync(_world.Server, Martin());
        await poste.Center.SyncNowAsync(default);
        await UpdateAsync(_world.Server, id, r => r with { Name = "Boulangerie Martin et Fils", Email = "contact@martin.fr" });
        await UpdateAsync(poste, id, r => r with { Name = "Martin SARL", Email = "factures@martin.fr" });
        await poste.Center.SyncNowAsync(default);

        poste.Center.Current.Conflicts.Should().Be(2);
        poste.Center.Current.Status.Should().Be(SyncStatus.ResolvingConflicts);
        IReadOnlyList<SyncFieldDispute> disputes = await poste.Center.ListConflictsAsync(default);
        disputes.Should().HaveCount(2).And.OnlyContain(d => d.Label == "Boulangerie Martin et Fils" && d.Item == SyncItemKind.Customer);
        SyncFieldDispute name = disputes.Single(d => d.Field == "Name");
        name.Should().BeEquivalentTo(new { Base = "Boulangerie Martin", Mine = "Martin SARL", Server = "Boulangerie Martin et Fils" });

        SyncFieldDispute email = disputes.Single(d => d.Field == "Email");
        var resolved = await poste.Center.ResolveAsync([new SyncFieldChoice(name.Id, KeepMine: true), new SyncFieldChoice(email.Id, KeepMine: false)], default);

        resolved.IsSuccess.Should().BeTrue();
        poste.Center.Current.Conflicts.Should().Be(0);
        poste.Center.Current.Pending.Changes.Should().Be(1, "le nom gardé repart");
        await poste.Center.SyncNowAsync(default);
        var server = (await CustomerAsync(_world.Server, id))!;
        server.Name.Should().Be("Martin SARL");
        server.Email.Should().Be("contact@martin.fr");
    }

    [Fact]
    public async Task Un_conflit_deja_tranche_est_refuse_et_les_choix_suivants_attendent()
    {
        DeviceNode poste = _world.Device(center: true);
        CustomerId id = await CreateAsync(_world.Server, Martin());
        await poste.Center.SyncNowAsync(default);
        await UpdateAsync(_world.Server, id, r => r with { Name = "Boulangerie Martin et Fils" });
        await UpdateAsync(poste, id, r => r with { Name = "Martin SARL" });
        await poste.Center.SyncNowAsync(default);
        SyncFieldDispute name = (await poste.Center.ListConflictsAsync(default)).Single();

        var resolved = await poste.Center.ResolveAsync([new SyncFieldChoice(name.Id + 1000, KeepMine: true), new SyncFieldChoice(name.Id, KeepMine: true)], default);

        resolved.Errors.Should().ContainSingle(e => e.Code == "Sync.EntityNotFound");
        poste.Center.Current.Conflicts.Should().Be(1, "le refus arrête la série de choix");
    }

    [Fact]
    public async Task Une_saisie_refusee_par_le_serveur_se_lit_avec_ses_codes()
    {
        DeviceNode poste = _world.Device(center: true);
        await CreateAsync(poste, Martin());
        _world.Server.Permissions.Granted.Remove(Permission.TiersCreer);

        await poste.Center.SyncNowAsync(default);

        poste.Center.Current.Rejected.Should().Be(1);
        SyncRejectedItem rejected = (await poste.Center.ListRejectedAsync(default)).Should().ContainSingle().Subject;
        rejected.Label.Should().Be("Boulangerie Martin");
        rejected.ErrorCodes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Le_mode_choisi_est_garde_et_relu()
    {
        DeviceNode poste = _world.Device(center: true);

        await poste.Center.SetModeAsync(SyncMode.NeverOnCellular, default);

        poste.Center.Mode.Should().Be(SyncMode.NeverOnCellular);
        (await poste.Preference.GetAsync(default)).Should().Be(Lettra.Sync.Client.SyncPreference.NeverOnCellular);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        condition().Should().BeTrue();
    }
}
