using Lettra.Poc.Sync.Client;
using Lettra.Poc.Sync.Contracts;
using Lettra.Poc.Sync.Domain;
using Lettra.Poc.Sync.Server;

namespace Lettra.Poc.Sync.Tests;

/// <summary>Vérifications de l'ADR-0008 : appareil révoqué, jeton expiré, faits revérifiés par le serveur.</summary>
public sealed class SecuriteTests : IDisposable
{
    private readonly World _world = new();
    public void Dispose() => _world.Dispose();

    [Fact]
    public async Task Appareil_revoque_efface_ses_donnees_locales_et_n_envoie_rien()
    {
        var poste = _world.Maui("P01");
        poste.Network.Online = false;
        poste.Invoicing.Validate(poste.Invoicing.CreateDraft("SARL Dupont", Lines.Audit()));
        _world.State.RevokeDevice(poste.Profile.DeviceId);

        var report = await poste.ReconnectAsync();

        Assert.Equal(SyncState.Revoked, report.State);
        Assert.Empty(poste.Store.AllInvoices());
        Assert.Empty(poste.Store.Outbox());
        Assert.Empty(_world.State.All());
    }

    [Fact]
    public async Task Jeton_expire_pendant_le_hors_ligne_reconnexion_avant_toute_ecriture()
    {
        var poste = _world.Maui("P01");
        poste.Network.Online = false;
        poste.Invoicing.Validate(poste.Invoicing.CreateDraft("SARL Dupont", Lines.Audit()));
        _world.Clock.Advance(TimeSpan.FromDays(8));   // au-delà de la durée maximale hors ligne

        var report = await poste.ReconnectAsync();

        Assert.Equal(SyncState.Failed, report.State);
        Assert.Contains(report.Messages, m => m.Contains("reconnectez-vous", StringComparison.Ordinal));
        Assert.Equal(0, _world.State.EffectiveWrites);
        Assert.Equal(1, poste.Sync.Summarize().ValidatedInvoices);

        await poste.Http.LoginAsync("pin-1234", default);
        Assert.Equal(SyncState.UpToDate, (await poste.ReconnectAsync()).State);
        Assert.Single(_world.State.All());
    }

    [Fact]
    public void Le_serveur_revérifie_les_faits_et_refuse_un_contenu_altere()
    {
        var device = _world.State.EnrollDevice(Guid.NewGuid(), "P09");
        var invoice = Invoice.NewDraft(Guid.NewGuid(), _world.DossierId, "SARL Dupont", Lines.Audit());
        invoice.Validate("F-2026-P09-000001", new DateOnly(2026, 10, 14), null);
        var dto = invoice.ToDto();

        var altered = new ValidatedInvoiceFact(Guid.NewGuid(), "P09", 1, null, dto with { TotalInclTax = 1.00m }, null);
        Assert.Equal(FactOutcome.Rejected, _world.State.ApplyFact(device, altered).Outcome);

        var otherSeries = new ValidatedInvoiceFact(Guid.NewGuid(), "P01", 1, null, dto, null);
        Assert.Equal(FactOutcome.Rejected, _world.State.ApplyFact(device, otherSeries).Outcome);

        var gap = new ValidatedInvoiceFact(Guid.NewGuid(), "P09", 2, null, dto, null);
        Assert.Equal(FactOutcome.Rejected, _world.State.ApplyFact(device, gap).Outcome);

        var genuine = new ValidatedInvoiceFact(Guid.NewGuid(), "P09", 1, null, dto, null);
        Assert.Equal(FactOutcome.Accepted, _world.State.ApplyFact(device, genuine).Outcome);
        Assert.Equal(FactOutcome.Accepted, _world.State.ApplyFact(device, genuine).Outcome);   // rejeu : même réponse
        Assert.Equal(1, _world.State.EffectiveWrites);
    }

    [Fact]
    public void Une_cle_d_idempotence_reutilisee_pour_autre_chose_est_refusee()
    {
        var key = Guid.NewGuid();
        var draft = Invoice.NewDraft(Guid.NewGuid(), _world.DossierId, "A", Lines.Audit()).ToDto();
        _world.State.ApplyIntent(new CreateDraftCommand(key, draft));

        Assert.Throws<IdempotencyKeyReusedException>(() =>
            _world.State.ApplyIntent(new CreateDraftCommand(key, draft with { CustomerName = "B" })));
    }
}
