using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Integrity;
using Lettra.Application.Mapping;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Integrity;
using Lettra.Contracts.Sync;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Sync.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Sync.Tests;

/// <summary>
/// Factures validées hors ligne dans la série du poste (ADR-0007), envoyées comme faits et revérifiées au serveur
/// (ADR-0008) : numéro suivant de la série, totaux, mentions et empreinte, puis chaîne d'intégrité et écriture.
/// </summary>
public sealed class OfflineInvoiceTests : IAsyncLifetime
{
    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Lettra", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: false, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: "FR7630006000011234567890189", Bic: "AGRIFRPP");

    private readonly SyncWorld _world = new();
    private Guid _customer;

    public async Task InitializeAsync()
    {
        (await _world.Server.SendAsync(new SaveCompanyProfileCommand(_world.Dossier, Cabinet, null))).IsSuccess.Should().BeTrue();
        _customer = (await _world.Server.SendAsync(SynchronisationTests.Martin().ToCommand(_world.Dossier))).Value.Value;
    }

    public async Task DisposeAsync() => await _world.DisposeAsync();

    private static DocumentLineRequest Line(string designation, decimal quantity, decimal price) =>
        new() { Designation = designation, Quantity = quantity, UnitPriceExclTax = price, VatRateCode = "FR-20", Kind = CatalogItemKindDto.Service, UnitCode = "HUR" };

    /// <summary>Un poste passé une fois en ligne (client, fiche entreprise, série), puis hors ligne.</summary>
    private async Task<DeviceNode> OfflineDeviceAsync()
    {
        DeviceNode device = _world.Device();
        (await device.SyncAsync()).State.Should().Be(SyncState.UpToDate);
        device.Probe.Status = new NetworkStatus(false, NetworkKind.None);
        return device;
    }

    private async Task<InvoiceResponse> ValidateAsync(DossierNode node, decimal price)
    {
        Result<InvoiceId> created = await node.SendAsync(new CreateInvoiceCommand(node.Dossier, new SaveInvoiceRequest { CustomerId = _customer, Lines = [Line("Conseil", 2, price)] }));
        created.IsSuccess.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Code)));
        Result validated = await node.SendAsync(new ValidateInvoiceCommand(node.Dossier, created.Value, null));
        validated.IsSuccess.Should().BeTrue(string.Join(", ", validated.Errors.Select(e => e.Code)));
        return (await node.QueryAsync(new GetInvoiceQuery(node.Dossier, created.Value))).Value;
    }

    private Task<Result<InvoiceResponse>> ServerInvoiceAsync(Guid id) =>
        _world.Server.QueryAsync(new GetInvoiceQuery(_world.Dossier, new InvoiceId(id)));

    private async Task<IntegrityReportResponse> VerifyAsync() =>
        (await _world.Server.SendAsync(new VerifyDossierIntegrityCommand(_world.Dossier))).Value;

    [Fact]
    public async Task Les_factures_du_poste_gardent_leur_numero_et_leur_empreinte_au_serveur_a_cote_de_celles_du_cabinet()
    {
        DeviceNode device = await OfflineDeviceAsync();
        InvoiceResponse first = await ValidateAsync(device, 100m);
        InvoiceResponse second = await ValidateAsync(device, 120m);
        InvoiceResponse office = await ValidateAsync(_world.Server, 90m);

        first.Number.Should().Be("FAC-P01-000001");
        first.SeriesCode.Should().Be("FAC-P01");
        second.Number.Should().Be("FAC-P01-000002");
        office.Number.Should().Be("FAC-000001", "le cabinet continue sa propre série pendant ce temps");
        (await device.OutboxAsync()).Select(i => i.Intent).Should().AllBeOfType<ValidatedInvoiceFact>().And.HaveCount(2);

        device.Probe.Status = new NetworkStatus(true, NetworkKind.Wifi);
        SyncReport report = await device.SyncAsync();
        report.State.Should().Be(SyncState.UpToDate);
        device.Consent.LastSummary!.Validations.Should().Be(2, "les factures à envoyer sont annoncées avant l'envoi");
        report.Sent.Should().Be(2);

        foreach (InvoiceResponse sent in new[] { first, second })
        {
            InvoiceResponse received = (await ServerInvoiceAsync(sent.Id)).Value;
            received.Number.Should().Be(sent.Number);
            received.ContentHash.Should().Be(sent.ContentHash);
            received.Totals.Should().BeEquivalentTo(sent.Totals);
            received.ValidatedAt.Should().Be(sent.ValidatedAt);
        }

        IntegrityReportResponse integrity = await VerifyAsync();
        integrity.Findings.Should().BeEmpty();
        integrity.Intact.Should().BeTrue();
        integrity.DocumentsChecked.Should().Be(3);
    }

    [Fact]
    public async Task Un_avoir_fait_hors_ligne_sur_une_facture_du_poste_suit_dans_la_meme_serie_et_credite_l_origine()
    {
        DeviceNode device = await OfflineDeviceAsync();
        InvoiceResponse invoice = await ValidateAsync(device, 100m);
        Result<InvoiceId> credit = await device.SendAsync(new CreateCreditNoteCommand(device.Dossier, new InvoiceId(invoice.Id), new CreateCreditNoteRequest { Reason = "Geste commercial" }));
        credit.IsSuccess.Should().BeTrue(string.Join(", ", credit.Errors.Select(e => e.Code)));
        (await device.SendAsync(new ValidateInvoiceCommand(device.Dossier, credit.Value, null))).IsSuccess.Should().BeTrue();

        device.Probe.Status = new NetworkStatus(true, NetworkKind.Wifi);
        (await device.SyncAsync()).Sent.Should().Be(2);

        InvoiceResponse received = (await ServerInvoiceAsync(credit.Value.Value)).Value;
        received.Number.Should().Be("FAC-P01-000002", "l'avoir partage la série des factures, celle du poste");
        received.Kind.Should().Be(InvoiceKindDto.CreditNote);
        (await ServerInvoiceAsync(invoice.Id)).Value.CreditedInclTax.Should().Be(invoice.Totals.TotalInclTax);
        (await VerifyAsync()).Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task Une_reponse_perdue_ne_fait_pas_recevoir_deux_fois_la_piece()
    {
        DeviceNode device = await OfflineDeviceAsync();
        InvoiceResponse invoice = await ValidateAsync(device, 100m);
        device.Probe.Status = new NetworkStatus(true, NetworkKind.Wifi);

        device.Transport.CutNext = Cut.PushResponse;
        (await device.SyncAsync()).Failure.Should().Be(SyncFailure.ConnectionLost);
        (await device.SyncAsync()).State.Should().Be(SyncState.UpToDate);

        (await ServerInvoiceAsync(invoice.Id)).Value.ContentHash.Should().Be(invoice.ContentHash);
        (await VerifyAsync()).Findings.Should().BeEmpty("une seule entrée au journal, une seule écriture");
    }

    [Fact]
    public async Task Une_piece_retouchee_apres_validation_est_refusee_et_ne_consomme_pas_son_numero()
    {
        DeviceNode device = await OfflineDeviceAsync();
        InvoiceResponse invoice = await ValidateAsync(device, 100m);
        ValidatedInvoiceFact fact = (ValidatedInvoiceFact)(await device.OutboxAsync()).Single().Intent;
        // Prix modifié dans le fait, empreinte d'origine conservée : le serveur recalcule et compare.
        ValidatedInvoiceFact altered = fact with
        {
            IdempotencyKey = Guid.CreateVersion7(),
            Document = fact.Document with { Lines = [fact.Document.Lines[0] with { UnitPriceExclTax = 10m }] },
        };
        PushSyncIntentsResponse response = (await _world.Server.RunAsync(sp =>
            sp.GetRequiredService<Server.SyncServer>().PushAsync(new PushSyncIntentsRequest(DeviceIdOf(device), [altered]), default))).Value;
        response.Results.Single().Outcome.Should().Be(SyncIntentOutcome.Rejected);
        response.Results.Single().Errors.Should().Contain(e => e.Code == ErrorCodes.Billing.ReplicaMismatch);

        device.Probe.Status = new NetworkStatus(true, NetworkKind.Wifi);
        (await device.SyncAsync()).Sent.Should().Be(1, "la vraie pièce, envoyée ensuite, prend le numéro resté libre");
        (await ServerInvoiceAsync(invoice.Id)).Value.Number.Should().Be("FAC-P01-000001");
    }

    [Fact]
    public async Task Une_piece_d_un_autre_poste_ou_hors_de_son_ordre_est_refusee()
    {
        DeviceNode device = await OfflineDeviceAsync();
        await ValidateAsync(device, 100m);
        await ValidateAsync(device, 110m);
        ValidatedInvoiceFact[] facts = [.. (await device.OutboxAsync()).Select(i => (ValidatedInvoiceFact)i.Intent)];

        async Task<SyncIntentResult> PushAs(Guid deviceId, ValidatedInvoiceFact fact) =>
            (await _world.Server.RunAsync(sp => sp.GetRequiredService<Server.SyncServer>().PushAsync(new PushSyncIntentsRequest(deviceId, [fact]), default))).Value.Results.Single();

        // Clés neuves : une réponse gardée vaut pour sa clé, refus compris.
        (await PushAs(Guid.CreateVersion7(), facts[0] with { IdempotencyKey = Guid.CreateVersion7() }))
            .Errors.Should().Contain(e => e.Code == ErrorCodes.Sync.DeviceSeriesUnknown);
        (await PushAs(DeviceIdOf(device), facts[1] with { IdempotencyKey = Guid.CreateVersion7() }))
            .Errors.Should().Contain(e => e.Code == ErrorCodes.Sync.NumberOutOfOrder);
        (await PushAs(DeviceIdOf(device), facts[0])).Outcome.Should().Be(SyncIntentOutcome.Applied);
        (await PushAs(DeviceIdOf(device), facts[0])).Outcome.Should().Be(SyncIntentOutcome.Applied, "même clé, même contenu : la réponse gardée");
    }

    [Fact]
    public async Task Sans_droit_de_valider_au_moment_de_la_synchronisation_la_piece_est_refusee()
    {
        DeviceNode device = await OfflineDeviceAsync();
        await ValidateAsync(device, 100m);
        _world.Server.Permissions.Granted.Remove(Permission.FactureValider);
        device.Probe.Status = new NetworkStatus(true, NetworkKind.Wifi);

        SyncReport report = await device.SyncAsync();
        report.Rejected.Should().Be(1);
        (await VerifyAsync()).EntriesChecked.Should().Be(0);
    }

    [Fact]
    public async Task Le_rappel_monte_a_24_h_puis_72_h_tant_que_la_facture_n_est_pas_envoyee()
    {
        DeviceNode device = await OfflineDeviceAsync();
        (await device.Coordinator.ReminderAsync()).Should().Be(SyncReminder.None);
        await ValidateAsync(device, 100m);

        _world.Clock.Advance(TimeSpan.FromHours(23));
        (await device.Coordinator.ReminderAsync()).Should().Be(SyncReminder.None);
        _world.Clock.Advance(TimeSpan.FromHours(2));
        (await device.Coordinator.ReminderAsync()).Should().Be(SyncReminder.Day);
        _world.Clock.Advance(TimeSpan.FromHours(48));
        (await device.Coordinator.ReminderAsync()).Should().Be(SyncReminder.ThreeDays);

        device.Probe.Status = new NetworkStatus(true, NetworkKind.Wifi);
        await device.SyncAsync();
        (await device.Coordinator.ReminderAsync()).Should().Be(SyncReminder.None);
    }

    private static Guid DeviceIdOf(DeviceNode device) => device.Services.GetRequiredService<SyncClientOptions>().DeviceId;
}
