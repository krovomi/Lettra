using Lettra.Poc.Sync.Client;
using Lettra.Poc.Sync.Contracts;

namespace Lettra.Poc.Sync.Tests;

/// <summary>Critère : MAUI travaille hors ligne et valide ses factures dans la série du poste.</summary>
public sealed class HorsLigneMauiTests : IDisposable
{
    private readonly World _world = new();
    public void Dispose() => _world.Dispose();

    [Fact]
    public async Task Le_poste_MAUI_valide_hors_ligne_dans_sa_serie_puis_le_serveur_accepte_les_faits()
    {
        var poste = _world.Maui("P01");
        poste.Network.Online = false;

        var numbers = Enumerable.Range(1, 3).Select(i =>
        {
            var id = poste.Invoicing.CreateDraft($"Client {i}", Lines.Audit());
            return poste.Invoicing.Validate(id);
        }).ToList();

        Assert.Equal(["F-2026-P01-000001", "F-2026-P01-000002", "F-2026-P01-000003"], numbers);
        Assert.Empty(_world.State.All());

        var report = await poste.ReconnectAsync();

        Assert.Equal(SyncState.UpToDate, report.State);
        Assert.Equal(3, report.FactsAccepted);
        var server = _world.State.All().OrderBy(i => i.Number).ToList();
        Assert.Equal(numbers, server.Select(i => i.Number));
        Assert.All(server, i => Assert.Equal(InvoiceStatus.Validated, i.Status));
        Assert.Contains(poste.Store.AuditTrail(), a => a.Origin == "Synchronisation" && a.Text.StartsWith("3 factures validées", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deux_postes_hors_ligne_numerotent_sans_collision()
    {
        var p01 = _world.Maui("P01");
        var p02 = _world.Maui("P02");
        p01.Network.Online = p02.Network.Online = false;

        foreach (var poste in new[] { p01, p02, p01, p02 })
            poste.Invoicing.Validate(poste.Invoicing.CreateDraft("Boulangerie Martin", Lines.Two()));

        await p02.ReconnectAsync();
        await p01.ReconnectAsync();

        var numbers = _world.State.All().Select(i => i.Number).Order().ToList();
        Assert.Equal(["F-2026-P01-000001", "F-2026-P01-000002", "F-2026-P02-000001", "F-2026-P02-000002"], numbers);
    }

    [Fact]
    public async Task Un_fait_n_est_jamais_en_conflit_meme_si_le_brouillon_a_change_au_bureau()
    {
        var poste = _world.Maui("P01");
        var id = poste.Invoicing.CreateDraft("SARL Dupont", Lines.Audit());
        await poste.ReconnectAsync();

        poste.Network.Online = false;
        _world.State.EditOnServer(id, i => i.SetNotes("Ajouté par un collègue"));
        var number = poste.Invoicing.Validate(id);

        var report = await poste.ReconnectAsync();

        Assert.Equal(1, report.FactsAccepted);
        Assert.Equal(0, report.Conflicts);
        Assert.Equal(number, _world.State.Find(id)!.Number);
        Assert.Null(_world.State.Find(id)!.Notes);
        Assert.Contains(report.Messages, m => m.Contains("remplacé par la facture validée", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_modification_de_brouillon_envoyee_apres_le_fait_est_ignoree_sans_erreur()
    {
        var poste = _world.Maui("P01");
        poste.Network.Online = false;
        var id = poste.Invoicing.CreateDraft("SAS Lemoine", Lines.Audit());
        poste.Invoicing.EditDraft(id, i => i.SetNotes("Remise accordée"));
        poste.Invoicing.Validate(id);

        // Ordre d'envoi : faits d'abord, puis intentions (création, modification) devenues sans objet.
        var report = await poste.ReconnectAsync();

        Assert.Equal(SyncState.UpToDate, report.State);
        Assert.Equal(0, report.Conflicts);
        var server = _world.State.Find(id)!;
        Assert.Equal(InvoiceStatus.Validated, server.Status);
        Assert.Equal("Remise accordée", server.Notes);
        Assert.All(poste.Store.Outbox(), e => Assert.Equal(OutboxStatus.Sent, e.Status));
    }

    [Fact]
    public void La_conformite_locale_bloque_une_facture_invalide_meme_hors_ligne()
    {
        var poste = _world.Maui("P01");
        poste.Network.Online = false;
        var id = poste.Invoicing.CreateDraft("", Lines.Audit());

        Assert.Throws<Domain.ComplianceException>(() => poste.Invoicing.Validate(id));
        Assert.Empty(poste.Store.Outbox(kind: OutboxKind.Fact));

        // Le numéro n'a pas été consommé : la facture suivante prend le 1.
        Assert.Equal("F-2026-P01-000001", poste.Invoicing.Validate(poste.Invoicing.CreateDraft("SCI Les Tilleuls", Lines.Audit())));
    }
}
