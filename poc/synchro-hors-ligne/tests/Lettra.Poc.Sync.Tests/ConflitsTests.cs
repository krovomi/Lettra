using System.Text.Json;
using Lettra.Poc.Sync.Client;
using Lettra.Poc.Sync.Contracts;

namespace Lettra.Poc.Sync.Tests;

/// <summary>Critère : un conflit de brouillon se résout champ par champ, sans bloquer le reste.</summary>
public sealed class ConflitsTests : IDisposable
{
    private readonly World _world = new();
    public void Dispose() => _world.Dispose();

    [Fact]
    public async Task Conflit_resolu_champ_par_champ_et_le_reste_passe()
    {
        var poste = _world.Maui("P01");
        var conflictual = poste.Invoicing.CreateDraft("SARL Dupont", Lines.Audit());
        var clean = poste.Invoicing.CreateDraft("EURL Bernard", Lines.Audit());
        await poste.ReconnectAsync();

        poste.Network.Online = false;
        poste.Invoicing.EditDraft(conflictual, i => { i.SetCustomerName("SARL Dupont & Fils"); i.SetNotes("Relance faite"); });
        poste.Invoicing.EditDraft(clean, i => i.SetNotes("RAS"));

        _world.State.EditOnServer(conflictual, i => { i.SetCustomerName("Dupont SARL"); i.SetDueDate(new DateOnly(2026, 12, 1)); });

        var report = await poste.ReconnectAsync();

        Assert.Equal(SyncState.ResolvingConflicts, report.State);
        Assert.Equal(1, report.Conflicts);
        var server = _world.State.Find(conflictual)!;
        Assert.Equal("Relance faite", server.Notes);                    // mon champ sans conflit : appliqué
        Assert.Equal(new DateOnly(2026, 12, 1), server.DueDate);        // le champ du collègue : conservé
        Assert.Equal("Dupont SARL", server.CustomerName);               // le champ disputé : en attente
        Assert.Equal("RAS", _world.State.Find(clean)!.Notes);           // l'autre brouillon : passé

        var conflict = Assert.Single(poste.Store.Conflicts());
        Assert.Equal(DraftFields.CustomerName, conflict.Field);
        Assert.Equal("SARL Dupont & Fils", conflict.Mine.GetString());
        Assert.Equal("Dupont SARL", conflict.Server.GetString());

        poste.Invoicing.ResolveConflict(conflictual, new Dictionary<string, FieldResolution>
        {
            [DraftFields.CustomerName] = new(ConflictChoice.Custom, JsonSerializer.SerializeToElement("Dupont & Fils SARL")),
        });
        var after = await poste.ReconnectAsync();

        Assert.Equal(SyncState.UpToDate, after.State);
        Assert.Equal("Dupont & Fils SARL", _world.State.Find(conflictual)!.CustomerName);
        Assert.Equal("Dupont & Fils SARL", poste.Store.FindInvoice(conflictual)!.Invoice.CustomerName);
        Assert.Equal(new DateOnly(2026, 12, 1), poste.Store.FindInvoice(conflictual)!.Invoice.DueDate);
    }

    [Fact]
    public async Task Choisir_la_version_serveur_ne_renvoie_rien()
    {
        var poste = _world.Maui("P01");
        var id = poste.Invoicing.CreateDraft("SARL Dupont", Lines.Audit());
        await poste.ReconnectAsync();

        poste.Network.Online = false;
        poste.Invoicing.EditDraft(id, i => i.SetLines(Lines.Audit(1000m)));
        _world.State.EditOnServer(id, i => i.SetLines(Lines.Audit(1100m)));
        await poste.ReconnectAsync();

        poste.Invoicing.ResolveConflict(id, new Dictionary<string, FieldResolution> { [DraftFields.Lines] = new(ConflictChoice.Server) });

        Assert.Empty(poste.Store.Outbox(OutboxStatus.Pending));
        Assert.Equal(1320m, poste.Store.FindInvoice(id)!.Invoice.TotalInclTax);
    }

    [Fact]
    public async Task Meme_modification_des_deux_cotes_pas_de_conflit()
    {
        var poste = _world.Maui("P01");
        var id = poste.Invoicing.CreateDraft("SARL Dupont", Lines.Audit());
        await poste.ReconnectAsync();

        poste.Network.Online = false;
        poste.Invoicing.EditDraft(id, i => i.SetNotes("Identique"));
        _world.State.EditOnServer(id, i => i.SetNotes("Identique"));

        var report = await poste.ReconnectAsync();

        Assert.Equal(0, report.Conflicts);
        Assert.Equal(SyncState.UpToDate, report.State);
    }

    [Fact]
    public async Task Deux_postes_modifient_le_meme_brouillon_hors_ligne()
    {
        var p01 = _world.Maui("P01");
        var p02 = _world.Maui("P02");
        var id = p01.Invoicing.CreateDraft("SAS Lemoine", Lines.Audit());
        await p01.ReconnectAsync();
        await p02.ReconnectAsync();

        p01.Network.Online = p02.Network.Online = false;
        p01.Invoicing.EditDraft(id, i => i.SetNotes("Note de P01"));
        p02.Invoicing.EditDraft(id, i => { i.SetNotes("Note de P02"); i.SetDueDate(new DateOnly(2026, 11, 30)); });

        await p01.ReconnectAsync();
        var report = await p02.ReconnectAsync();

        Assert.Equal(1, report.Conflicts);
        Assert.Equal(new DateOnly(2026, 11, 30), _world.State.Find(id)!.DueDate);
        Assert.Equal("Note de P01", _world.State.Find(id)!.Notes);
        Assert.Equal(DraftFields.Notes, Assert.Single(p02.Store.Conflicts()).Field);
    }
}
