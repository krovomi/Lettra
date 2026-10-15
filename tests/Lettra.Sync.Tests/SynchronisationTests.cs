using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Features.Catalog;
using Lettra.Application.Features.Customers;
using Lettra.Application.Features.Parties;
using Lettra.Application.Mapping;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Suppliers;
using Lettra.Contracts.Sync;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Sync.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Sync.Tests;

/// <summary>
/// Tiers et catalogue saisis hors ligne sur un poste, puis synchronisés avec l'accord de l'utilisateur (ADR-0008) :
/// création avec le même identifiant, fusion champ par champ, conflits, coupures à chaque étape, droits du moment.
/// </summary>
public sealed class SynchronisationTests : IAsyncLifetime
{
    private readonly SyncWorld _world = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _world.DisposeAsync();

    internal static CreateCustomerRequest Martin() => new()
    {
        Kind = CustomerKindDto.Company,
        Name = "Boulangerie Martin",
        Siret = "73282932000074",
        Email = "compta@martin.fr",
        AddressLine1 = "1 rue de la Paix",
        PostalCode = "75002",
        City = "Paris",
    };

    internal static CreateSupplierRequest Dupuis() => new()
    {
        Name = "Minoterie Dupuis",
        Siret = "73282932000074",
        AddressLine1 = "2 chemin du Moulin",
        PostalCode = "45000",
        City = "Orléans",
    };

    internal static SaveCatalogItemRequest Conseil(string reference = "CONSEIL") => new()
    {
        Reference = reference,
        Designation = "Heure de conseil",
        Kind = CatalogItemKindDto.Service,
        UnitCode = "HUR",
        UnitPriceExclTax = 85m,
        VatRateCode = "FR-20",
    };

    private static async Task<CustomerId> CreateAsync(DossierNode node, CreateCustomerRequest request)
    {
        Result<CustomerId> created = await node.SendAsync(request.ToCommand(node.Dossier));
        created.IsSuccess.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Code)));
        return created.Value;
    }

    private static async Task<CustomerResponse?> CustomerAsync(DossierNode node, CustomerId id) =>
        (await node.QueryAsync(new GetCustomerQuery(node.Dossier, id))) is { IsSuccess: true } r ? r.Value : null;

    private static async Task UpdateAsync(DossierNode node, CustomerId id, Func<CreateCustomerRequest, CreateCustomerRequest> change)
    {
        CustomerResponse current = (await CustomerAsync(node, id))!;
        Result updated = await node.SendAsync(change(current.ToRequest()).ToUpdateCommand(node.Dossier, id, current.Version));
        updated.IsSuccess.Should().BeTrue(string.Join(", ", updated.Errors.Select(e => e.Code)));
    }

    private async Task<PushSyncIntentsResponse> PushAsync(SyncIntent intent)
    {
        Result<PushSyncIntentsResponse> pushed = await _world.Server.RunAsync(sp =>
            sp.GetRequiredService<Server.SyncServer>().PushAsync(new PushSyncIntentsRequest(Guid.CreateVersion7(), [intent]), default));
        pushed.IsSuccess.Should().BeTrue();
        return pushed.Value;
    }

    private static async Task<int> ServerCustomerCountAsync(DossierNode node) =>
        (await node.QueryAsync(new ListCustomersQuery(node.Dossier))).Value.TotalCount;

    [Fact]
    public async Task Un_client_cree_hors_ligne_arrive_sur_le_serveur_avec_le_meme_identifiant_et_la_meme_version()
    {
        DeviceNode poste = _world.Device();
        CustomerId id = await CreateAsync(poste, Martin());
        (await poste.OutboxAsync()).Should().ContainSingle(i => i.Status == SyncOutboxStatus.Pending && i.Intent is CreateCustomerIntent);

        SyncReport report = await poste.SyncAsync();

        report.State.Should().Be(SyncState.UpToDate);
        report.Sent.Should().Be(1);
        poste.Consent.Asked.Should().Be(1);
        poste.Consent.LastSummary.Should().Be(new SyncSummary(1, 0, poste.Consent.LastSummary!.EstimatedBytes));
        CustomerResponse server = (await CustomerAsync(_world.Server, id))!;
        server.Name.Should().Be("Boulangerie Martin");
        (await CustomerAsync(poste, id))!.Version.Should().Be(server.Version);
        (await poste.OutboxAsync()).Should().OnlyContain(i => i.Status == SyncOutboxStatus.Sent);
    }

    [Fact]
    public async Task Une_commande_refusee_sur_le_poste_ne_laisse_aucune_intention()
    {
        DeviceNode poste = _world.Device();

        Result<CustomerId> refused = await poste.SendAsync((Martin() with { Name = "" }).ToCommand(poste.Dossier));

        refused.IsFailure.Should().BeTrue();
        (await poste.OutboxAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Rien_a_envoyer_le_poste_recoit_sans_question_puis_seulement_ce_qui_a_change()
    {
        CustomerId martin = await CreateAsync(_world.Server, Martin());
        (await _world.Server.SendAsync(Conseil().ToCreateCommand(_world.Server.Dossier))).IsSuccess.Should().BeTrue();
        DeviceNode poste = _world.Device();

        SyncReport first = await poste.SyncAsync();

        poste.Consent.Asked.Should().Be(0, "la demande porte sur ce qui part (confirmé par le PO le 14/10/2026)");
        first.State.Should().Be(SyncState.UpToDate);
        first.Received.Should().Be(2);
        (await CustomerAsync(poste, martin))!.Name.Should().Be("Boulangerie Martin");

        _world.Clock.Advance(TimeSpan.FromMinutes(1));
        await UpdateAsync(_world.Server, martin, r => r with { City = "Lyon", PostalCode = "69002" });
        SyncReport second = await poste.SyncAsync();

        second.Received.Should().Be(1, "seul le client modifié revient");
        CustomerResponse local = (await CustomerAsync(poste, martin))!;
        local.City.Should().Be("Lyon");
        local.Version.Should().Be((await CustomerAsync(_world.Server, martin))!.Version);
        (await poste.OutboxAsync()).Should().BeEmpty("une réception n'est pas une saisie du poste");
    }

    [Fact]
    public async Task Plus_tard_rien_ne_part_et_rien_n_est_perdu()
    {
        DeviceNode poste = _world.Device();
        CustomerId id = await CreateAsync(poste, Martin());
        poste.Consent.Answer = SyncConsent.Later;

        SyncReport report = await poste.SyncAsync();

        report.Postponed.Should().BeTrue();
        poste.Transport.Pushes.Should().Be(0);
        (await CustomerAsync(_world.Server, id)).Should().BeNull();
        (await poste.OutboxAsync()).Should().ContainSingle(i => i.Status == SyncOutboxStatus.Pending);
    }

    [Theory]
    [InlineData(SyncPreference.AutoOnWifi, NetworkKind.Wifi, 0, true)]
    [InlineData(SyncPreference.AutoOnWifi, NetworkKind.Cellular, 1, true)]
    [InlineData(SyncPreference.NeverOnCellular, NetworkKind.Cellular, 0, false)]
    [InlineData(SyncPreference.NeverOnCellular, NetworkKind.Ethernet, 1, true)]
    public async Task Les_preferences_decident_de_la_question_selon_le_reseau(SyncPreference preference, NetworkKind network, int asked, bool sent)
    {
        DeviceNode poste = _world.Device();
        CustomerId id = await CreateAsync(poste, Martin());
        poste.Preference.Current = preference;
        poste.Probe.Status = new NetworkStatus(true, network);

        await poste.SyncAsync();

        poste.Consent.Asked.Should().Be(asked);
        (await CustomerAsync(_world.Server, id) is not null).Should().Be(sent);
    }

    [Fact]
    public async Task Sans_serveur_joignable_rien_ne_part()
    {
        DeviceNode poste = _world.Device();
        await CreateAsync(poste, Martin());
        poste.Probe.Status = new NetworkStatus(false, NetworkKind.Wifi);

        SyncReport report = await poste.SyncAsync();

        report.State.Should().Be(SyncState.Offline);
        poste.Consent.Asked.Should().Be(0, "un portail captif ne déclenche pas de question");
    }

    [Fact]
    public async Task Une_modification_hors_ligne_et_une_modification_au_bureau_sur_d_autres_champs_sont_fusionnees()
    {
        DeviceNode poste = _world.Device();
        CustomerId id = await CreateAsync(_world.Server, Martin());
        await poste.SyncAsync();

        await UpdateAsync(_world.Server, id, r => r with { City = "Lyon", PostalCode = "69002" });
        await UpdateAsync(poste, id, r => r with { Email = "factures@martin.fr" });
        SyncReport report = await poste.SyncAsync();

        report.Conflicts.Should().Be(0);
        CustomerResponse server = (await CustomerAsync(_world.Server, id))!;
        server.City.Should().Be("Lyon");
        server.Email.Should().Be("factures@martin.fr");
        CustomerResponse local = (await CustomerAsync(poste, id))!;
        local.Should().BeEquivalentTo(server, "la réplique reprend l'état fusionné du serveur");
    }

    [Fact]
    public async Task Un_meme_champ_change_des_deux_cotes_garde_la_valeur_du_serveur_et_attend_l_utilisateur()
    {
        DeviceNode poste = _world.Device();
        CustomerId id = await CreateAsync(_world.Server, Martin());
        await poste.SyncAsync();

        await UpdateAsync(_world.Server, id, r => r with { Name = "Boulangerie Martin et Fils" });
        await UpdateAsync(poste, id, r => r with { Name = "Martin SARL", Email = "factures@martin.fr" });
        SyncReport report = await poste.SyncAsync();

        report.State.Should().Be(SyncState.ResolvingConflicts);
        report.Conflicts.Should().Be(1);
        CustomerResponse server = (await CustomerAsync(_world.Server, id))!;
        server.Name.Should().Be("Boulangerie Martin et Fils", "le champ disputé garde la valeur du serveur");
        server.Email.Should().Be("factures@martin.fr", "les champs sans conflit sont appliqués");

        SyncConflictRecord conflict = (await poste.ConflictsAsync()).Should().ContainSingle().Subject;
        conflict.EntityId.Should().Be(id.Value);
        conflict.Conflict.Field.Should().Be(nameof(CreateCustomerRequest.Name));
        conflict.Conflict.Base.GetString().Should().Be("Boulangerie Martin");
        conflict.Conflict.Mine.GetString().Should().Be("Martin SARL");
        conflict.Conflict.Server.GetString().Should().Be("Boulangerie Martin et Fils");
        (await CustomerAsync(poste, id))!.Name.Should().Be("Boulangerie Martin et Fils");
    }

    [Fact]
    public async Task Deux_modifications_hors_ligne_du_meme_client_arrivent_dans_l_ordre()
    {
        DeviceNode poste = _world.Device();
        CustomerId id = await CreateAsync(poste, Martin());
        await UpdateAsync(poste, id, r => r with { City = "Lyon", PostalCode = "69002" });
        await UpdateAsync(poste, id, r => r with { City = "Lille", PostalCode = "59000" });

        SyncReport report = await poste.SyncAsync();

        report.Sent.Should().Be(3);
        (await CustomerAsync(_world.Server, id))!.City.Should().Be("Lille");
    }

    [Theory]
    [InlineData(Cut.PushRequest)]
    [InlineData(Cut.PushResponse)]
    [InlineData(Cut.PullRequest)]
    [InlineData(Cut.PullResponse)]
    public async Task Une_coupure_a_chaque_etape_reprend_sans_doublon(Cut cut)
    {
        DeviceNode poste = _world.Device();
        CustomerId[] ids = [await CreateAsync(poste, Martin()), await CreateAsync(poste, Martin() with { Name = "Épicerie Durand" })];
        CustomerId updated = ids[0];
        await UpdateAsync(poste, updated, r => r with { City = "Lyon", PostalCode = "69002" });
        (await poste.SendAsync(Conseil().ToCreateCommand(poste.Dossier))).IsSuccess.Should().BeTrue();
        poste.Transport.CutNext = cut;

        SyncReport broken = await poste.SyncAsync();
        SyncReport resumed = await poste.SyncAsync();

        broken.State.Should().Be(SyncState.Failed);
        broken.Failure.Should().Be(SyncFailure.ConnectionLost);
        resumed.State.Should().Be(SyncState.UpToDate);
        (await ServerCustomerCountAsync(_world.Server)).Should().Be(2);
        (await CustomerAsync(_world.Server, updated))!.City.Should().Be("Lyon");
        (await poste.OutboxAsync()).Should().HaveCount(4).And.OnlyContain(i => i.Status == SyncOutboxStatus.Sent);
        foreach (CustomerId id in ids)
        {
            (await CustomerAsync(poste, id))!.Version.Should().Be((await CustomerAsync(_world.Server, id))!.Version);
        }
    }

    [Fact]
    public async Task Un_renvoi_apres_une_reponse_perdue_rend_la_reponse_gardee()
    {
        DeviceNode poste = _world.Device();
        CustomerId id = await CreateAsync(poste, Martin());
        poste.Transport.CutNext = Cut.PushResponse;
        await poste.SyncAsync();

        SyncOutboxItem pending = (await poste.OutboxAsync()).Single();
        pending.Status.Should().Be(SyncOutboxStatus.Pending, "la réponse n'est jamais arrivée");
        await poste.SyncAsync();

        (await poste.OutboxAsync()).Single().Status.Should().Be(SyncOutboxStatus.Sent);
        SyncReceipt receipt = (await _world.Server.RunAsync(sp => sp.GetRequiredService<ISyncReceipts>().FindAsync(pending.Intent.IdempotencyKey, default)))!;
        receipt.Result.Outcome.Should().Be(SyncIntentOutcome.Applied, "le renvoi rend la réponse du premier passage");
        (await CustomerAsync(_world.Server, id)).Should().NotBeNull();
    }

    [Fact]
    public async Task La_meme_cle_avec_un_autre_contenu_est_refusee()
    {
        Guid key = Guid.CreateVersion7();
        Guid id = Guid.CreateVersion7();
        PushSyncIntentsResponse first = await PushAsync(new CreateCustomerIntent(key, id, _world.Clock.Now, Martin()));
        PushSyncIntentsResponse again = await PushAsync(new CreateCustomerIntent(key, id, _world.Clock.Now, Martin()));
        PushSyncIntentsResponse altered = await PushAsync(new CreateCustomerIntent(key, id, _world.Clock.Now, Martin() with { Name = "Autre" }));

        first.Results.Single().Outcome.Should().Be(SyncIntentOutcome.Applied);
        again.Results.Single().Should().BeEquivalentTo(first.Results.Single());
        altered.Results.Single().Outcome.Should().Be(SyncIntentOutcome.Rejected);
        altered.Results.Single().Errors.Should().ContainSingle(e => e.Code == ErrorCodes.Sync.IdempotencyKeyReused);
        (await CustomerAsync(_world.Server, new CustomerId(id)))!.Name.Should().Be("Boulangerie Martin");
    }

    [Fact]
    public async Task Une_intention_est_rejouee_avec_les_droits_du_moment_et_non_ceux_de_la_saisie()
    {
        DeviceNode poste = _world.Device();
        CustomerId id = await CreateAsync(poste, Martin());
        _world.Server.Permissions.Granted.Remove(Permission.TiersCreer);

        SyncReport report = await poste.SyncAsync();

        report.Rejected.Should().Be(1);
        (await CustomerAsync(_world.Server, id)).Should().BeNull();
        SyncOutboxItem item = (await poste.OutboxAsync()).Single();
        item.Status.Should().Be(SyncOutboxStatus.Rejected);
        item.Detail.Should().Be(ErrorCodes.Security.Forbidden);
    }

    [Fact]
    public async Task Sans_droit_de_lire_le_catalogue_le_poste_ne_recoit_que_les_tiers()
    {
        await CreateAsync(_world.Server, Martin());
        (await _world.Server.SendAsync(Conseil().ToCreateCommand(_world.Server.Dossier))).IsSuccess.Should().BeTrue();
        _world.Server.Permissions.Granted.Remove(Permission.CatalogueConsulter);
        DeviceNode poste = _world.Device();

        SyncReport report = await poste.SyncAsync();

        report.Received.Should().Be(1);
        (await poste.QueryAsync(new ListCatalogItemsQuery(poste.Dossier, IncludeArchived: true))).Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Fournisseurs_et_articles_suivent_le_meme_chemin_y_compris_l_archivage()
    {
        DeviceNode poste = _world.Device();
        Result<SupplierId> supplier = await poste.SendAsync(Dupuis().ToCommand(poste.Dossier));
        Result<CatalogItemId> item = await poste.SendAsync(Conseil().ToCreateCommand(poste.Dossier));
        supplier.IsSuccess.Should().BeTrue();
        item.IsSuccess.Should().BeTrue();
        CatalogItemResponse local = (await poste.QueryAsync(new GetCatalogItemQuery(poste.Dossier, item.Value))).Value;
        (await poste.SendAsync(new SetCatalogItemArchivedCommand(poste.Dossier, item.Value, true, local.Version))).IsSuccess.Should().BeTrue();

        SyncReport report = await poste.SyncAsync();

        report.Sent.Should().Be(3);
        (await _world.Server.QueryAsync(new GetSupplierQuery(_world.Server.Dossier, supplier.Value))).Value.Name.Should().Be("Minoterie Dupuis");
        CatalogItemResponse server = (await _world.Server.QueryAsync(new GetCatalogItemQuery(_world.Server.Dossier, item.Value))).Value;
        server.IsArchived.Should().BeTrue();
        (await poste.QueryAsync(new GetCatalogItemQuery(poste.Dossier, item.Value))).Value.Version.Should().Be(server.Version);
    }

    [Fact]
    public async Task Un_article_archive_au_bureau_est_repris_par_la_replique()
    {
        DeviceNode poste = _world.Device();
        CatalogItemId item = (await _world.Server.SendAsync(Conseil().ToCreateCommand(_world.Server.Dossier))).Value;
        await poste.SyncAsync();
        long version = (await _world.Server.QueryAsync(new GetCatalogItemQuery(_world.Server.Dossier, item))).Value.Version;
        (await _world.Server.SendAsync(new SetCatalogItemArchivedCommand(_world.Server.Dossier, item, true, version))).IsSuccess.Should().BeTrue();

        await poste.SyncAsync();

        CatalogItemResponse local = (await poste.QueryAsync(new GetCatalogItemQuery(poste.Dossier, item))).Value;
        local.IsArchived.Should().BeTrue();
        local.Version.Should().Be(version + 1);
    }

    [Fact]
    public async Task L_instantane_se_recoit_par_pages_et_un_second_poste_voit_les_saisies_du_premier()
    {
        for (int i = 0; i < 5; i++)
        {
            (await _world.Server.SendAsync(Conseil($"ART-{i}").ToCreateCommand(_world.Server.Dossier))).IsSuccess.Should().BeTrue();
        }

        DeviceNode premier = _world.Device();
        DeviceNode second = _world.Device(pageSize: 2);
        CustomerId id = await CreateAsync(premier, Martin());
        await premier.SyncAsync();

        SyncReport report = await second.SyncAsync();

        report.Received.Should().Be(6);
        second.Transport.Pulls.Should().BeGreaterThan(3, "6 éléments par pages de 2, puis le journal");
        (await CustomerAsync(second, id))!.Name.Should().Be("Boulangerie Martin");
    }

    [Fact]
    public async Task Une_saisie_locale_pas_encore_partie_n_est_pas_ecrasee_par_la_reception()
    {
        DeviceNode poste = _world.Device();
        CustomerId id = await CreateAsync(_world.Server, Martin());
        await poste.SyncAsync();
        await UpdateAsync(poste, id, r => r with { Email = "factures@martin.fr" });
        await UpdateAsync(_world.Server, id, r => r with { City = "Lyon", PostalCode = "69002" });

        SyncApplyReport applied = await poste.RunAsync(async sp =>
        {
            ISyncReplica replica = sp.GetRequiredService<ISyncReplica>();
            SyncChangesResponse changes = await poste.Transport.PullAsync(poste.Dossier.Value, await replica.GetTokenAsync(default), 100, default);
            return await replica.ApplyAsync(changes, default);
        });

        applied.KeptLocal.Should().Be(1);
        (await CustomerAsync(poste, id))!.Email.Should().Be("factures@martin.fr");
    }
}
