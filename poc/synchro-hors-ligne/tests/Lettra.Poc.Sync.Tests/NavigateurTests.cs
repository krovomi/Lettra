using Lettra.Poc.Sync.Client;
using Lettra.Poc.Sync.Contracts;

namespace Lettra.Poc.Sync.Tests;

/// <summary>Critère : le navigateur ne garde que des brouillons et la facture est numérotée à la synchronisation.</summary>
public sealed class NavigateurTests : IDisposable
{
    private readonly World _world = new();
    public void Dispose() => _world.Dispose();

    [Fact]
    public async Task Le_navigateur_ne_numerote_pas_hors_ligne_le_serveur_numerote_a_la_synchro()
    {
        var web = _world.Browser();
        web.Network.Online = false;
        var id = web.Invoicing.CreateDraft("Garage Petit", Lines.Two());

        Assert.Null(web.Invoicing.Validate(id));
        var local = web.Store.FindInvoice(id)!;
        Assert.True(local.PendingValidation);
        Assert.Null(local.Invoice.Number);
        Assert.Throws<InvalidOperationException>(() => web.Invoicing.EditDraft(id, i => i.SetNotes("trop tard")));

        var report = await web.ReconnectAsync();

        Assert.Equal(SyncState.UpToDate, report.State);
        Assert.Equal("F-2026-SRV-000001", _world.State.Find(id)!.Number);
        local = web.Store.FindInvoice(id)!;
        Assert.Equal("F-2026-SRV-000001", local.Invoice.Number);
        Assert.False(local.PendingValidation);
    }

    [Fact]
    public async Task Stockage_du_navigateur_efface_avant_la_synchro_aucun_trou_dans_la_numerotation()
    {
        var web = _world.Browser();
        web.Network.Online = false;
        web.Invoicing.Validate(web.Invoicing.CreateDraft("Garage Petit", Lines.Two()));

        web.Store.Wipe();   // quota, nettoyage, éviction : le navigateur a tout perdu
        await web.ReconnectAsync();

        var maui = _world.Maui("P01");
        maui.Invoicing.Validate(maui.Invoicing.CreateDraft("Pharmacie Roux", Lines.Audit()));
        var webAgain = _world.Browser();
        var id = webAgain.Invoicing.CreateDraft("Fleuriste Blanc", Lines.Audit());
        webAgain.Invoicing.Validate(id);
        await maui.ReconnectAsync();
        await webAgain.ReconnectAsync();

        // Le brouillon perdu n'avait consommé aucun numéro : la série serveur commence à 1, sans trou.
        Assert.Equal("F-2026-SRV-000001", _world.State.Find(id)!.Number);
    }

    [Fact]
    public async Task Des_modifications_puis_une_validation_hors_ligne_passent_ensemble()
    {
        var web = _world.Browser();
        var id = web.Invoicing.CreateDraft("Cabinet Morel", Lines.Audit());
        await web.ReconnectAsync();

        web.Network.Online = false;
        web.Invoicing.EditDraft(id, i => i.SetNotes("Paiement à 30 jours"));
        web.Invoicing.EditDraft(id, i => i.SetDueDate(new DateOnly(2026, 11, 13)));
        web.Invoicing.Validate(id);

        var report = await web.ReconnectAsync();

        // Les deux modifications font avancer la version serveur ; la validation compare le contenu vu, pas la version.
        Assert.Equal(0, report.Conflicts);
        var server = _world.State.Find(id)!;
        Assert.Equal(InvoiceStatus.Validated, server.Status);
        Assert.Equal("Paiement à 30 jours", server.Notes);
        Assert.Equal(new DateOnly(2026, 11, 13), server.DueDate);
    }

    [Fact]
    public async Task Validation_differee_refusee_si_le_brouillon_a_change_au_bureau()
    {
        var web = _world.Browser();
        var id = web.Invoicing.CreateDraft("Cabinet Morel", Lines.Audit());
        await web.ReconnectAsync();

        web.Network.Online = false;
        web.Invoicing.Validate(id);
        _world.State.EditOnServer(id, i => i.SetLines(Lines.Audit(1500m)));

        var report = await web.ReconnectAsync();

        Assert.Equal(1, report.Conflicts);
        Assert.Equal(InvoiceStatus.Draft, _world.State.Find(id)!.Status);
        Assert.Contains(report.Messages, m => m.Contains("relisez-le", StringComparison.Ordinal));
        var local = web.Store.FindInvoice(id)!;
        Assert.False(local.PendingValidation);
        Assert.Equal(1800m, local.Invoice.TotalInclTax);   // l'utilisateur voit maintenant la version du bureau
    }
}
