using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Features.Billing;
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
using static Lettra.Sync.Tests.SynchronisationTests;

namespace Lettra.Sync.Tests;

/// <summary>
/// Sécurité du poste (étape 4.4) : registre des postes au serveur, révocation qui fait effacer la réplique au contact
/// suivant, et durée maximale hors ligne fixée par le cabinet, au-delà de laquelle le poste ne valide plus.
/// </summary>
public sealed class DeviceSecurityTests : IAsyncLifetime
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
        _customer = (await _world.Server.SendAsync(Martin().ToCommand(_world.Dossier))).Value.Value;
    }

    public async Task DisposeAsync() => await _world.DisposeAsync();

    private static Guid DeviceIdOf(DeviceNode device) => device.Services.GetRequiredService<SyncClientOptions>().DeviceId;

    private async Task<Result> ValidateAsync(DossierNode node)
    {
        Result<InvoiceId> created = await node.SendAsync(new CreateInvoiceCommand(node.Dossier, new SaveInvoiceRequest
        {
            CustomerId = _customer,
            Lines = [new() { Designation = "Conseil", Quantity = 1, UnitPriceExclTax = 100m, VatRateCode = "FR-20", Kind = CatalogItemKindDto.Service, UnitCode = "HUR" }],
        }));
        created.IsSuccess.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Code)));
        return await node.SendAsync(new ValidateInvoiceCommand(node.Dossier, created.Value, null));
    }

    private async Task<IReadOnlyList<SyncDeviceResponse>> DevicesAsync() =>
        (await _world.Server.QueryAsync(new ListSyncDevicesQuery(_world.Dossier))).Value;

    [Fact]
    public async Task Chaque_poste_qui_se_synchronise_est_inscrit_au_registre_du_dossier()
    {
        DeviceNode first = _world.Device();
        DeviceNode second = _world.Device();
        await first.SyncAsync();
        _world.Clock.Advance(TimeSpan.FromHours(1));
        await second.SyncAsync();

        IReadOnlyList<SyncDeviceResponse> devices = await DevicesAsync();
        devices.Select(d => d.DeviceId).Should().Equal([DeviceIdOf(second), DeviceIdOf(first)], "du contact le plus récent au plus ancien");
        devices.Should().AllSatisfy(d => d.RevokedAt.Should().BeNull());
    }

    [Fact]
    public async Task Un_poste_revoque_efface_sa_replique_au_contact_suivant_et_ne_valide_plus()
    {
        DeviceNode device = _world.Device();
        (await device.SyncAsync()).State.Should().Be(SyncState.UpToDate);
        await CreateAsync(device, Martin() with { Name = "Saisie pas encore partie" });
        (await device.OutboxAsync()).Should().NotBeEmpty();

        (await _world.Server.SendAsync(new RevokeSyncDeviceCommand(_world.Dossier, DeviceIdOf(device)))).IsSuccess.Should().BeTrue();
        SyncReport report = await device.SyncAsync();

        report.Failure.Should().Be(SyncFailure.DeviceRevoked);
        (await device.OutboxAsync()).Should().BeEmpty("l'outbox est effacée avec le reste");
        (await device.RunAsync(sp => sp.GetRequiredService<ICustomerReadStore>().ListAsync(null, 1, 50, default))).Items.Should().BeEmpty();
        (await DevicesAsync()).Single().WipedAt.Should().NotBeNull("le serveur sait que le poste a reçu sa révocation");

        int pushes = device.Transport.Pushes, pulls = device.Transport.Pulls;
        (await device.SyncAsync()).Failure.Should().Be(SyncFailure.DeviceRevoked);
        (device.Transport.Pushes, device.Transport.Pulls).Should().Be((pushes, pulls), "un poste révoqué ne se présente plus au serveur");
        (await device.RunAsync(sp => sp.GetRequiredService<IDeviceNumbering>().FindAsync("FAC", default)))
            .Errors.Should().ContainSingle(e => e.Code == ErrorCodes.Sync.DeviceRevoked);
    }

    [Fact]
    public async Task Un_poste_revoque_est_refuse_meme_s_il_envoie_directement()
    {
        DeviceNode device = _world.Device();
        await device.SyncAsync();
        await _world.Server.SendAsync(new RevokeSyncDeviceCommand(_world.Dossier, DeviceIdOf(device)));

        Result<SyncChangesResponse> pulled = await _world.Server.RunAsync(sp =>
            sp.GetRequiredService<Server.SyncServer>().PullAsync(null, 10, default, DeviceIdOf(device)));
        Result<OpenDeviceSeriesResponse> series = await _world.Server.RunAsync(sp =>
            sp.GetRequiredService<Server.SyncServer>().OpenDeviceSeriesAsync(new OpenDeviceSeriesRequest(DeviceIdOf(device)), default));

        pulled.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.Sync.DeviceRevoked);
        series.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.Sync.DeviceRevoked);
    }

    [Fact]
    public async Task Revoquer_demande_le_droit_de_gerer_les_postes_et_un_poste_connu()
    {
        (await _world.Server.SendAsync(new RevokeSyncDeviceCommand(_world.Dossier, Guid.CreateVersion7())))
            .Errors.Should().ContainSingle(e => e.Code == ErrorCodes.Sync.DeviceUnknown);

        DeviceNode device = _world.Device();
        await device.SyncAsync();
        _world.Server.Permissions.Granted.Remove(Permission.PostesGerer);
        (await _world.Server.SendAsync(new RevokeSyncDeviceCommand(_world.Dossier, DeviceIdOf(device)))).IsFailure.Should().BeTrue();
        (await device.SyncAsync()).State.Should().Be(SyncState.UpToDate);
    }

    [Fact]
    public async Task Au_dela_de_la_duree_fixee_par_le_cabinet_le_poste_ne_valide_plus_jusqu_au_prochain_contact()
    {
        (await _world.Server.SendAsync(new SaveSyncPolicyCommand(_world.Dossier, new SaveSyncPolicyRequest { MaxOfflineHours = 24 })))
            .Value.MaxOfflineHours.Should().Be(24);
        DeviceNode device = _world.Device();
        (await device.SyncAsync()).State.Should().Be(SyncState.UpToDate);
        device.Probe.Status = new NetworkStatus(false, NetworkKind.None);

        _world.Clock.Advance(TimeSpan.FromHours(23));
        (await ValidateAsync(device)).IsSuccess.Should().BeTrue("encore dans la durée permise");

        _world.Clock.Advance(TimeSpan.FromHours(2));
        (await ValidateAsync(device)).Errors.Should().ContainSingle(e => e.Code == ErrorCodes.Sync.OfflineTooLong);

        device.Probe.Status = new NetworkStatus(true, NetworkKind.Wifi);
        (await device.SyncAsync()).State.Should().Be(SyncState.UpToDate);
        (await ValidateAsync(device)).IsSuccess.Should().BeTrue("le contact remet le compte à zéro");
    }

    [Fact]
    public async Task Sans_reglage_le_poste_valide_sept_jours_hors_ligne()
    {
        (await _world.Server.QueryAsync(new GetSyncPolicyQuery(_world.Dossier))).Value.MaxOfflineHours.Should().Be(OfflinePolicy.DefaultMaxOfflineHours);
        DeviceNode device = _world.Device();
        await device.SyncAsync();
        device.Probe.Status = new NetworkStatus(false, NetworkKind.None);

        _world.Clock.Advance(TimeSpan.FromDays(6));
        (await ValidateAsync(device)).IsSuccess.Should().BeTrue();
        _world.Clock.Advance(TimeSpan.FromDays(2));
        (await ValidateAsync(device)).Errors.Should().ContainSingle(e => e.Code == ErrorCodes.Sync.OfflineTooLong);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(721)]
    public async Task Une_duree_hors_des_bornes_est_refusee(int hours)
    {
        (await _world.Server.SendAsync(new SaveSyncPolicyCommand(_world.Dossier, new SaveSyncPolicyRequest { MaxOfflineHours = hours })))
            .Errors.Should().ContainSingle(e => e.Code == ErrorCodes.Sync.PolicyInvalid);
    }

    [Fact]
    public async Task La_regle_part_avec_la_derniere_page_seulement()
    {
        for (int i = 0; i < 3; i++)
        {
            await CreateAsync(_world.Server, Martin() with { Name = $"Client {i}" });
        }

        Result<SyncChangesResponse> first = await _world.Server.RunAsync(sp => sp.GetRequiredService<Server.SyncServer>().PullAsync(null, 2, default));
        first.Value.HasMore.Should().BeTrue();
        first.Value.Policy.Should().BeNull();
        Result<SyncChangesResponse> last = await _world.Server.RunAsync(sp => sp.GetRequiredService<Server.SyncServer>().PullAsync(first.Value.Token, 50, default));
        last.Value.HasMore.Should().BeFalse();
        last.Value.Policy.Should().Be(new SyncPolicyResponse(OfflinePolicy.DefaultMaxOfflineHours));
    }
}
