using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Integrity;
using Lettra.Application.Features.Sync;
using Lettra.Application.Mapping;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Sync;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Sync.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Sync.Tests;

/// <summary>
/// Débloquer une série de poste (étape 4.4). Une pièce refusée à la réception ne peut pas être reprise : elle a été remise
/// au client. Les suivantes de la même série l'attendent sans être refusées ; un manager débloque la série, la pièce est
/// reçue sous son autorité et les suivantes passent derrière elle.
/// </summary>
public sealed class SeriesReleaseTests : IAsyncLifetime
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

    private static Guid DeviceIdOf(DeviceNode device) => device.Services.GetRequiredService<SyncClientOptions>().DeviceId;

    private async Task<InvoiceId> ValidateAsync(DeviceNode device, decimal price)
    {
        Result<InvoiceId> created = await device.SendAsync(new CreateInvoiceCommand(device.Dossier, new SaveInvoiceRequest
        {
            CustomerId = _customer,
            Lines = [new() { Designation = "Conseil", Quantity = 1, UnitPriceExclTax = price, VatRateCode = "FR-20", Kind = CatalogItemKindDto.Service, UnitCode = "HUR" }],
        }));
        (await device.SendAsync(new ValidateInvoiceCommand(device.Dossier, created.Value, null))).IsSuccess.Should().BeTrue();
        return created.Value;
    }

    private Task<Result<InvoiceResponse>> ServerInvoiceAsync(InvoiceId id) =>
        _world.Server.QueryAsync(new GetInvoiceQuery(_world.Dossier, id));

    /// <summary>Hors ligne : une facture, un avoir sur elle, une autre facture. Le cabinet exige ensuite l'approbation des avoirs.</summary>
    private async Task<(DeviceNode Device, InvoiceId Invoice, InvoiceId Credit, InvoiceId Next)> BlockedAsync()
    {
        DeviceNode device = _world.Device();
        (await device.SyncAsync()).State.Should().Be(SyncState.UpToDate);
        device.Probe.Status = new NetworkStatus(false, NetworkKind.None);
        InvoiceId invoice = await ValidateAsync(device, 100m);
        Result<InvoiceId> credit = await device.SendAsync(new CreateCreditNoteCommand(device.Dossier, invoice, new CreateCreditNoteRequest { Reason = "Geste commercial" }));
        (await device.SendAsync(new ValidateInvoiceCommand(device.Dossier, credit.Value, null))).IsSuccess.Should().BeTrue();
        InvoiceId next = await ValidateAsync(device, 50m);

        _world.Server.Policies.Policy = new SegregationOfDutiesPolicy(true, 10m);
        _world.Server.Permissions.Granted.Remove(Permission.AvoirApprouver);
        device.Probe.Status = new NetworkStatus(true, NetworkKind.Wifi);
        return (device, invoice, credit.Value, next);
    }

    [Fact]
    public async Task Les_pieces_qui_suivent_une_piece_refusee_attendent_sans_etre_refusees()
    {
        (DeviceNode device, InvoiceId invoice, InvoiceId credit, InvoiceId next) = await BlockedAsync();

        SyncReport report = await device.SyncAsync();

        report.Sent.Should().Be(1, "la première facture passe");
        report.Rejected.Should().Be(1, "l'avoir attend un manager");
        (await ServerInvoiceAsync(invoice)).IsSuccess.Should().BeTrue();
        (await ServerInvoiceAsync(credit)).IsFailure.Should().BeTrue();
        (await ServerInvoiceAsync(next)).IsFailure.Should().BeTrue();
        (await device.OutboxAsync()).Should().ContainSingle(i => i.Status == Application.Abstractions.Sync.SyncOutboxStatus.Pending,
            "la facture suivante attend, elle n'est pas refusée");
    }

    [Fact]
    public async Task Une_fois_la_serie_debloquee_la_piece_refusee_et_les_suivantes_sont_recues()
    {
        (DeviceNode device, _, InvoiceId credit, InvoiceId next) = await BlockedAsync();
        await device.SyncAsync();

        Result<ReleaseDeviceSeriesResponse> released = await _world.Server.SendAsync(new ReleaseDeviceSeriesCommand(_world.Dossier, DeviceIdOf(device)));
        released.Value.Should().Be(new ReleaseDeviceSeriesResponse("FAC-P01", "FAC-P01-000002"), "le numéro de l'avoir, attendu par le serveur");
        (await _world.Server.SendAsync(new ReleaseDeviceSeriesCommand(_world.Dossier, DeviceIdOf(device)))).Value.Should().Be(released.Value, "rejouable");

        SyncReport report = await device.SyncAsync();

        report.Rejected.Should().Be(0);
        report.Sent.Should().Be(2, "l'avoir sous l'autorité du manager, puis la facture qui l'attendait");
        (await ServerInvoiceAsync(credit)).Value.Number.Should().Be("FAC-P01-000002");
        (await ServerInvoiceAsync(next)).Value.Number.Should().Be("FAC-P01-000003");
        (await device.OutboxAsync()).Should().OnlyContain(i => i.Status == Application.Abstractions.Sync.SyncOutboxStatus.Sent);
        (await _world.Server.SendAsync(new VerifyDossierIntegrityCommand(_world.Dossier))).Value.Findings.Should().BeEmpty();
        (await device.SyncAsync()).Sent.Should().Be(0, "le déblocage a servi une fois");
    }

    [Fact]
    public async Task Une_piece_alteree_reste_refusee_meme_apres_deblocage()
    {
        DeviceNode device = _world.Device();
        await device.SyncAsync();
        device.Probe.Status = new NetworkStatus(false, NetworkKind.None);
        await ValidateAsync(device, 100m);
        ValidatedInvoiceFact fact = (ValidatedInvoiceFact)(await device.OutboxAsync()).Single().Intent;
        ValidatedInvoiceFact altered = fact with
        {
            IdempotencyKey = Guid.CreateVersion7(),
            Document = fact.Document with { Lines = [fact.Document.Lines[0] with { UnitPriceExclTax = 10m }] },
        };
        await _world.Server.SendAsync(new ReleaseDeviceSeriesCommand(_world.Dossier, DeviceIdOf(device)));

        SyncIntentResult result = (await _world.Server.RunAsync(sp => sp.GetRequiredService<Server.SyncServer>()
            .PushAsync(new PushSyncIntentsRequest(DeviceIdOf(device), [altered]), default))).Value.Results.Single();

        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Billing.ReplicaMismatch, "l'empreinte est toujours revérifiée");
    }

    [Fact]
    public async Task Debloquer_demande_le_droit_de_gerer_les_postes_et_une_serie_ouverte()
    {
        (await _world.Server.SendAsync(new ReleaseDeviceSeriesCommand(_world.Dossier, Guid.CreateVersion7())))
            .Errors.Should().ContainSingle(e => e.Code == ErrorCodes.Sync.DeviceUnknown);

        DeviceNode device = _world.Device();
        _world.Server.Permissions.Granted.Remove(Permission.FactureValider);
        await device.SyncAsync();
        (await _world.Server.SendAsync(new ReleaseDeviceSeriesCommand(_world.Dossier, DeviceIdOf(device))))
            .Errors.Should().ContainSingle(e => e.Code == ErrorCodes.Sync.NothingToRelease, "sans droit de valider, le poste n'a pas reçu de série");

        _world.Server.Permissions.Granted.Remove(Permission.PostesGerer);
        (await _world.Server.SendAsync(new ReleaseDeviceSeriesCommand(_world.Dossier, DeviceIdOf(device)))).IsFailure.Should().BeTrue();
    }
}
