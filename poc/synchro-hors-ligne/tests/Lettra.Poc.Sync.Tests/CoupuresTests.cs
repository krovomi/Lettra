using Lettra.Poc.Sync.Client;
using Lettra.Poc.Sync.Contracts;

namespace Lettra.Poc.Sync.Tests;

/// <summary>Critère : coupure simulée à chaque étape, reprise sans doublon.</summary>
public sealed class CoupuresTests : IDisposable
{
    private readonly World _world = new();
    public void Dispose() => _world.Dispose();

    public static TheoryData<string, Fault> Etapes => new()
    {
        { "/api/auth/refresh", Fault.DropRequest },
        { "/api/auth/refresh", Fault.DropResponse },
        { "/api/sync/facts", Fault.DropRequest },
        { "/api/sync/facts", Fault.DropResponse },
        { "/api/sync/intents", Fault.DropRequest },
        { "/api/sync/intents", Fault.DropResponse },
        { "/api/sync/changes", Fault.DropRequest },
        { "/api/sync/changes", Fault.DropResponse },
    };

    [Theory]
    [MemberData(nameof(Etapes))]
    public async Task Coupure_a_chaque_etape_puis_reprise_sans_doublon(string etape, Fault fault)
    {
        var (poste, drafts) = await PosteAvecFaitsEtIntentions();
        poste.Network.CutOnce(etape, fault);

        var first = await poste.ReconnectAsync();
        Assert.Equal(SyncState.Failed, first.State);

        poste.Restart();   // pire cas : l'app est aussi relancée entre les deux tentatives
        var second = await poste.ReconnectAsync();
        Assert.Equal(SyncState.UpToDate, second.State);

        AssertConvergence(poste, drafts);
    }

    [Fact]
    public async Task Coupure_au_milieu_d_un_lot_reprend_au_bon_endroit()
    {
        var (poste, drafts) = await PosteAvecFaitsEtIntentions(batchSize: 1);
        poste.Network.CutOnce("/api/sync/facts", Fault.DropResponse, occurrence: 2);
        poste.Network.CutOnce("/api/sync/intents", Fault.DropResponse, occurrence: 2);

        Assert.Equal(SyncState.Failed, (await poste.ReconnectAsync()).State);
        Assert.Equal(SyncState.Failed, (await poste.ReconnectAsync()).State);
        Assert.Equal(SyncState.UpToDate, (await poste.ReconnectAsync()).State);

        AssertConvergence(poste, drafts);
    }

    [Fact]
    public async Task Hors_ligne_complet_pendant_la_synchro_rien_n_est_perdu()
    {
        var (poste, drafts) = await PosteAvecFaitsEtIntentions();
        poste.Consent.Answer(SyncConsent.Now);
        poste.Network.Online = true;
        poste.Network.CutOnce("/api/sync/facts", Fault.DropRequest);

        var report = await poste.Sync.OnNetworkChangedAsync();
        Assert.Equal(SyncState.Failed, report.State);
        Assert.Equal(3, poste.Sync.Summarize().ValidatedInvoices);

        await poste.ReconnectAsync();
        AssertConvergence(poste, drafts);
    }

    /// <summary>
    /// Poste qui a, hors ligne : 3 factures validées, 1 brouillon créé, 1 brouillon existant modifié.
    /// Écritures effectives attendues côté serveur : 3 faits + 1 création + 1 modification.
    /// </summary>
    private async Task<(TestDevice Poste, Guid[] Drafts)> PosteAvecFaitsEtIntentions(int batchSize = 50)
    {
        var poste = _world.Maui("P01", batchSize);
        var existing = poste.Invoicing.CreateDraft("Existant", Lines.Audit());
        await poste.ReconnectAsync();
        _world.Clock.Advance(TimeSpan.FromHours(1));   // le jeton d'accès a expiré : il faudra le rafraîchir
        var baseline = _world.State.EffectiveWrites;
        Assert.Equal(1, baseline);

        poste.Network.Online = false;
        for (var i = 1; i <= 3; i++) poste.Invoicing.Validate(poste.Invoicing.CreateDraft($"Client {i}", Lines.Audit()));
        var created = poste.Invoicing.CreateDraft("Nouveau", Lines.Two());
        poste.Invoicing.EditDraft(existing, i => i.SetNotes("Modifié hors ligne"));
        return (poste, [existing, created]);
    }

    private void AssertConvergence(TestDevice poste, Guid[] drafts)
    {
        // Exactement 1 (avant) + 3 faits + 1 création + 1 modification : aucun doublon.
        Assert.Equal(1 + 3 + 1 + 1, _world.State.EffectiveWrites);

        var numbers = _world.State.All().Where(i => i.Number is not null).Select(i => i.Number).ToList();
        Assert.Equal(["F-2026-P01-000001", "F-2026-P01-000002", "F-2026-P01-000003"], numbers.Order());

        Assert.Equal("Modifié hors ligne", _world.State.Find(drafts[0])!.Notes);
        Assert.Equal(2, _world.State.Find(drafts[0])!.Version);
        Assert.NotNull(_world.State.Find(drafts[1]));

        Assert.All(poste.Store.Outbox(), e => Assert.Equal(OutboxStatus.Sent, e.Status));
        Assert.Empty(poste.Store.Conflicts());
        Assert.Equal(_world.State.All().Count, poste.Store.AllInvoices().Count);
        Assert.All(poste.Store.AllInvoices(), l => Assert.Equal(_world.State.Find(l.Invoice.Id)!.Version, l.ServerVersion));
    }
}
