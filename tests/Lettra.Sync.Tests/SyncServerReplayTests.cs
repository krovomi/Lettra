using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Persistence;
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
using Lettra.Infrastructure.Persistence.Sync;
using Lettra.Sync.Server;
using Microsoft.Extensions.DependencyInjection;
using static Lettra.Sync.Tests.SynchronisationTests;

namespace Lettra.Sync.Tests;

/// <summary>
/// Le serveur de synchronisation appelé directement, sans poste : lots refusés, accès refusés, et chaque intention
/// rejouée (tiers et catalogue) dans ses cas de bord — élément absent, déjà appliqué, refusé par les règles, et
/// collègue qui écrit pendant la fusion (version relue périmée).
/// </summary>
public sealed class SyncServerReplayTests : IAsyncLifetime
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-sync-serveur-").FullName;
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));
    private readonly StaleReads _stale = new();
    private readonly ReplayServerNode _server;
    private readonly Guid _device = Guid.CreateVersion7();

    public SyncServerReplayTests()
    {
        _server = new ReplayServerNode(new DossierId(Guid.CreateVersion7()), Path.Combine(_folder, "serveur.db"), _clock, _stale);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private DossierId Dossier => _server.Dossier;

    private Task<Result<PushSyncIntentsResponse>> PushAsync(PushSyncIntentsRequest request) =>
        _server.RunAsync(sp => sp.GetRequiredService<SyncServer>().PushAsync(request, default));

    private async Task<SyncIntentResult> PushOneAsync(SyncIntent intent)
    {
        Result<PushSyncIntentsResponse> pushed = await PushAsync(new PushSyncIntentsRequest(_device, [intent]));
        pushed.IsSuccess.Should().BeTrue(string.Join(", ", pushed.Errors.Select(e => e.Code)));
        return pushed.Value.Results.Single();
    }

    private static IEnumerable<string> Codes<T>(Result<T> result) => result.Errors.Select(e => e.Code);

    private DateTimeOffset Now => _clock.Now;

    private async Task<CustomerResponse> CustomerAsync(CustomerId id) =>
        (await _server.QueryAsync(new GetCustomerQuery(Dossier, id))).Value;

    private async Task<SupplierId> CreateSupplierAsync()
    {
        Result<SupplierId> created = await _server.SendAsync(Dupuis().ToCommand(Dossier));
        created.IsSuccess.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Code)));
        return created.Value;
    }

    private async Task<CatalogItemId> CreateItemAsync()
    {
        Result<CatalogItemId> created = await _server.SendAsync(Conseil().ToCreateCommand(Dossier));
        created.IsSuccess.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Code)));
        return created.Value;
    }

    [Fact]
    public async Task Un_lot_vide_trop_gros_sans_poste_ou_avec_un_trou_est_refuse()
    {
        SyncIntent one = new CreateCustomerIntent(Guid.CreateVersion7(), Guid.CreateVersion7(), Now, Martin());

        Codes(await PushAsync(new PushSyncIntentsRequest(Guid.Empty, [one]))).Should().Equal(ErrorCodes.Sync.BatchInvalid);
        Codes(await PushAsync(new PushSyncIntentsRequest(_device, []))).Should().Equal(ErrorCodes.Sync.BatchInvalid);
        Codes(await PushAsync(new PushSyncIntentsRequest(_device, null!))).Should().Equal(ErrorCodes.Sync.BatchInvalid);
        Codes(await PushAsync(new PushSyncIntentsRequest(_device, [one, null!]))).Should().Equal(ErrorCodes.Sync.BatchInvalid);
        Codes(await PushAsync(new PushSyncIntentsRequest(_device, [.. Enumerable.Repeat(one, SyncHttp.MaxBatch + 1)])))
            .Should().Equal(ErrorCodes.Sync.BatchInvalid);

        Func<Task> withoutRequest = () => _server.RunAsync(sp => sp.GetRequiredService<SyncServer>().PushAsync(null!, default));
        await withoutRequest.Should().ThrowAsync<ArgumentNullException>();
        (await _server.QueryAsync(new ListCustomersQuery(Dossier))).Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Une_intention_inconnue_des_contrats_ne_se_serialise_pas_et_fait_echouer_le_lot()
    {
        // Le rejeu a une branche « intention inconnue » (BatchInvalid), mais l'empreinte est calculée avant le rejeu, sur
        // le JSON polymorphe des contrats : un type non déclaré lève avant d'y arriver. Comportement actuel, épinglé.
        Func<Task> push = () => PushAsync(new PushSyncIntentsRequest(_device, [new UnknownIntent(Guid.CreateVersion7(), Guid.CreateVersion7(), Now)]));

        await push.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task Creations_deja_passees_sont_deja_appliquees_avec_la_version_du_serveur()
    {
        CustomerId customer = await CreateAsync(_server, Martin());
        SupplierId supplier = await CreateSupplierAsync();
        CatalogItemId item = await CreateItemAsync();

        SyncIntentResult c = await PushOneAsync(new CreateCustomerIntent(Guid.CreateVersion7(), customer.Value, Now, Martin()));
        SyncIntentResult s = await PushOneAsync(new CreateSupplierIntent(Guid.CreateVersion7(), supplier.Value, Now, Dupuis()));
        SyncIntentResult i = await PushOneAsync(new CreateCatalogItemIntent(Guid.CreateVersion7(), item.Value, Now, Conseil()));

        new[] { c, s, i }.Should().AllSatisfy(r =>
        {
            r.Outcome.Should().Be(SyncIntentOutcome.AlreadyApplied);
            r.ServerVersion.Should().Be(1);
            r.Errors.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Une_creation_refusee_par_les_regles_rend_les_erreurs_du_cas_d_usage()
    {
        SyncIntentResult result = await PushOneAsync(new CreateCustomerIntent(Guid.CreateVersion7(), Guid.CreateVersion7(), Now, Martin() with { Name = "" }));

        result.Outcome.Should().Be(SyncIntentOutcome.Rejected);
        result.ServerVersion.Should().BeNull();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Modifications_d_un_element_absent_du_serveur_sont_refusees()
    {
        Guid missing = Guid.CreateVersion7();
        SyncIntent[] intents =
        [
            new UpdateCustomerIntent(Guid.CreateVersion7(), missing, Now, Martin(), Martin() with { City = "Lyon" }),
            new DeleteCustomerIntent(Guid.CreateVersion7(), missing, Now, "Boulangerie Martin"),
            new UpdateSupplierIntent(Guid.CreateVersion7(), missing, Now, Dupuis(), Dupuis() with { City = "Tours" }),
            new DeleteSupplierIntent(Guid.CreateVersion7(), missing, Now),
            new UpdateCatalogItemIntent(Guid.CreateVersion7(), missing, Now, Conseil(), Conseil() with { UnitPriceExclTax = 90m }),
            new SetCatalogItemArchivedIntent(Guid.CreateVersion7(), missing, Now, false, true),
        ];

        Result<PushSyncIntentsResponse> pushed = await PushAsync(new PushSyncIntentsRequest(_device, intents));

        pushed.Value.Results.Should().HaveCount(intents.Length).And.AllSatisfy(r =>
        {
            r.Outcome.Should().Be(SyncIntentOutcome.Rejected);
            r.Errors.Select(e => e.Code).Should().Equal(ErrorCodes.Sync.EntityNotFound);
        });
        pushed.Value.Results.Select(r => r.EntityId).Should().AllBeEquivalentTo(missing);
    }

    [Fact]
    public async Task Modification_et_suppression_d_un_fournisseur_puis_rejeu_deja_applique()
    {
        SupplierId supplier = await CreateSupplierAsync();

        // Le poste part de la fiche telle que le serveur l'a enregistrée (le serveur complète la saisie : la fusion
        // depuis la saisie brute rendrait « fusionnée »).
        CreateSupplierRequest seen = (await _server.QueryAsync(new GetSupplierQuery(Dossier, supplier))).Value.ToRequest();
        SyncIntentResult updated = await PushOneAsync(new UpdateSupplierIntent(Guid.CreateVersion7(), supplier.Value, Now, seen, seen with { City = "Tours" }));
        updated.Outcome.Should().Be(SyncIntentOutcome.Applied);
        updated.ServerVersion.Should().Be(2);
        (await _server.QueryAsync(new GetSupplierQuery(Dossier, supplier))).Value.City.Should().Be("Tours");

        // Même valeur déjà présente au serveur : rien à écrire.
        SyncIntentResult same = await PushOneAsync(new UpdateSupplierIntent(Guid.CreateVersion7(), supplier.Value, Now, seen, seen with { City = "Tours" }));
        same.Outcome.Should().Be(SyncIntentOutcome.AlreadyApplied);
        same.ServerVersion.Should().Be(2);

        SyncIntentResult deleted = await PushOneAsync(new DeleteSupplierIntent(Guid.CreateVersion7(), supplier.Value, Now, "Minoterie Dupuis"));
        deleted.Outcome.Should().Be(SyncIntentOutcome.Applied);
        deleted.ServerVersion.Should().Be(3);
        (await _server.QueryAsync(new GetSupplierQuery(Dossier, supplier))).Value.IsArchived.Should().BeTrue();

        SyncIntentResult again = await PushOneAsync(new DeleteSupplierIntent(Guid.CreateVersion7(), supplier.Value, Now));
        again.Outcome.Should().Be(SyncIntentOutcome.AlreadyApplied);
        again.ServerVersion.Should().Be(3);
    }

    [Fact]
    public async Task Suppression_d_un_client_sur_la_version_du_moment_meme_modifie_au_bureau()
    {
        CustomerId customer = await CreateAsync(_server, Martin());
        await UpdateAsync(_server, customer, r => r with { City = "Lyon" });

        SyncIntentResult deleted = await PushOneAsync(new DeleteCustomerIntent(Guid.CreateVersion7(), customer.Value, Now, "Boulangerie Martin"));

        deleted.Outcome.Should().Be(SyncIntentOutcome.Applied);
        deleted.ServerVersion.Should().Be(3);
        (await CustomerAsync(customer)).IsArchived.Should().BeTrue();
        (await PushOneAsync(new DeleteCustomerIntent(Guid.CreateVersion7(), customer.Value, Now))).Outcome.Should().Be(SyncIntentOutcome.AlreadyApplied);
    }

    [Fact]
    public async Task Modification_d_un_article_fusionnee_avec_le_bureau_et_conflit_quand_les_deux_changent_le_meme_champ()
    {
        CatalogItemId item = await CreateItemAsync();
        Result changed = await _server.SendAsync((Conseil() with { Designation = "Heure d'expertise" }).ToUpdateCommand(Dossier, item, 1));
        changed.IsSuccess.Should().BeTrue(string.Join(", ", changed.Errors.Select(e => e.Code)));

        SyncIntentResult merged = await PushOneAsync(new UpdateCatalogItemIntent(Guid.CreateVersion7(), item.Value, Now, Conseil(), Conseil() with { UnitPriceExclTax = 90m }));
        merged.Outcome.Should().Be(SyncIntentOutcome.Merged);
        merged.ServerVersion.Should().Be(3);
        CatalogItemResponse server = (await _server.QueryAsync(new GetCatalogItemQuery(Dossier, item))).Value;
        server.Designation.Should().Be("Heure d'expertise");
        server.UnitPriceExclTax.Should().Be(90m);

        SyncIntentResult conflict = await PushOneAsync(new UpdateCatalogItemIntent(Guid.CreateVersion7(), item.Value, Now, Conseil(), Conseil() with { Designation = "Heure de conseil senior" }));
        conflict.Outcome.Should().Be(SyncIntentOutcome.Conflict);
        conflict.Conflicts.Select(f => f.Field).Should().ContainSingle();
    }

    [Fact]
    public async Task Une_modification_refusee_par_les_regles_rend_les_erreurs_du_cas_d_usage()
    {
        CustomerId customer = await CreateAsync(_server, Martin());

        SyncIntentResult result = await PushOneAsync(new UpdateCustomerIntent(Guid.CreateVersion7(), customer.Value, Now, Martin(), Martin() with { Name = "" }));

        result.Outcome.Should().Be(SyncIntentOutcome.Rejected);
        result.Errors.Should().NotBeEmpty();
        (await CustomerAsync(customer)).Name.Should().Be("Boulangerie Martin");
    }

    [Fact]
    public async Task Archivage_d_un_article_puis_rejeu_deja_applique()
    {
        CatalogItemId item = await CreateItemAsync();

        SyncIntentResult archived = await PushOneAsync(new SetCatalogItemArchivedIntent(Guid.CreateVersion7(), item.Value, Now, false, true));
        archived.Outcome.Should().Be(SyncIntentOutcome.Applied);
        archived.ServerVersion.Should().Be(2);

        SyncIntentResult again = await PushOneAsync(new SetCatalogItemArchivedIntent(Guid.CreateVersion7(), item.Value, Now, false, true));
        again.Outcome.Should().Be(SyncIntentOutcome.AlreadyApplied);
        again.ServerVersion.Should().Be(2);
    }

    [Fact]
    public async Task Un_collegue_qui_ecrit_pendant_la_fusion_fait_relire_puis_reussir()
    {
        CustomerId customer = await CreateAsync(_server, Martin());
        CatalogItemId item = await CreateItemAsync();

        CreateCustomerRequest seen = (await CustomerAsync(customer)).ToRequest();
        _stale.Remaining = 1;
        SyncIntentResult updated = await PushOneAsync(new UpdateCustomerIntent(Guid.CreateVersion7(), customer.Value, Now, seen, seen with { City = "Lyon" }));
        updated.Outcome.Should().Be(SyncIntentOutcome.Applied);
        updated.ServerVersion.Should().Be(2);
        _stale.Remaining.Should().Be(0);

        _stale.Remaining = 1;
        (await PushOneAsync(new SetCatalogItemArchivedIntent(Guid.CreateVersion7(), item.Value, Now, false, true))).Outcome.Should().Be(SyncIntentOutcome.Applied);

        _stale.Remaining = 1;
        SyncIntentResult deleted = await PushOneAsync(new DeleteCustomerIntent(Guid.CreateVersion7(), customer.Value, Now));
        deleted.Outcome.Should().Be(SyncIntentOutcome.Applied);
        deleted.ServerVersion.Should().Be(3);
    }

    [Fact]
    public async Task Trois_versions_perimees_de_suite_et_l_intention_est_refusee()
    {
        CustomerId customer = await CreateAsync(_server, Martin());
        CatalogItemId item = await CreateItemAsync();

        _stale.Remaining = int.MaxValue;
        SyncIntentResult updated = await PushOneAsync(new UpdateCustomerIntent(Guid.CreateVersion7(), customer.Value, Now, Martin(), Martin() with { City = "Lyon" }));
        SyncIntentResult deleted = await PushOneAsync(new DeleteCustomerIntent(Guid.CreateVersion7(), customer.Value, Now));
        SyncIntentResult archived = await PushOneAsync(new SetCatalogItemArchivedIntent(Guid.CreateVersion7(), item.Value, Now, false, true));
        _stale.Remaining = 0;

        new[] { updated, deleted, archived }.Should().AllSatisfy(r =>
        {
            r.Outcome.Should().Be(SyncIntentOutcome.Rejected);
            r.Errors.Select(e => e.Code).Should().Equal(ConcurrencyErrors.VersionMismatchCode);
        });
        (await CustomerAsync(customer)).Should().Match<CustomerResponse>(c => c.Version == 1 && !c.IsArchived && c.City == "Paris");
    }

    [Fact]
    public async Task Lecture_des_changements_refusee_sans_connexion_ou_sans_droit_de_lecture()
    {
        Result<SyncChangesResponse> anonymous = await _server.RunAsync(sp =>
            ActivatorUtilities.CreateInstance<SyncServer>(sp, new Anonymous()).PullAsync(null, 50, default, _device));
        Codes(anonymous).Should().Equal(ErrorCodes.Security.Unauthenticated);

        _server.Permissions.Granted.Remove(Permission.TiersConsulter);
        _server.Permissions.Granted.Remove(Permission.CatalogueConsulter);
        Codes(await _server.RunAsync(sp => sp.GetRequiredService<SyncServer>().PullAsync(null, 50, default))).Should().Equal(ErrorCodes.Security.Forbidden);

        // Le catalogue seul suffit : les tiers sont absents.
        await CreateAsync(_server, Martin());
        _server.Permissions.Granted.Add(Permission.CatalogueConsulter);
        Result<SyncChangesResponse> catalog = await _server.RunAsync(sp => sp.GetRequiredService<SyncServer>().PullAsync(null, 50, default));
        catalog.IsSuccess.Should().BeTrue();
        catalog.Value.Customers.Should().BeEmpty();
    }

    [Fact]
    public async Task Lecture_sans_poste_rend_la_regle_du_cabinet_et_aucun_deblocage()
    {
        Result<SyncChangesResponse> pulled = await _server.RunAsync(sp => sp.GetRequiredService<SyncServer>().PullAsync(null, 50, default));

        pulled.IsSuccess.Should().BeTrue();
        pulled.Value.HasMore.Should().BeFalse();
        pulled.Value.Policy.Should().NotBeNull();
        pulled.Value.Released.Should().BeNull();
    }

    [Fact]
    public async Task Une_page_intermediaire_ne_porte_pas_la_regle_du_cabinet()
    {
        await CreateAsync(_server, Martin());
        await CreateAsync(_server, Martin() with { Name = "Pâtisserie Martin" });

        Result<SyncChangesResponse> first = await _server.RunAsync(sp => sp.GetRequiredService<SyncServer>().PullAsync(null, 1, default, _device));

        first.Value.HasMore.Should().BeTrue();
        first.Value.Policy.Should().BeNull();
        first.Value.Released.Should().BeNull();
    }

    [Fact]
    public async Task Un_jeton_qui_n_est_pas_du_serveur_est_refuse()
    {
        Result<SyncChangesResponse> pulled = await _server.RunAsync(sp => sp.GetRequiredService<SyncServer>().PullAsync("pas-un-jeton", 50, default, _device));

        Codes(pulled).Should().Equal(ErrorCodes.Sync.TokenInvalid);
    }

    [Fact]
    public async Task Series_du_poste_refusees_sans_connexion_sans_poste_ou_sans_droit_de_valider()
    {
        Result<OpenDeviceSeriesResponse> anonymous = await _server.RunAsync(sp =>
            ActivatorUtilities.CreateInstance<SyncServer>(sp, new Anonymous()).OpenDeviceSeriesAsync(new OpenDeviceSeriesRequest(_device), default));
        Codes(anonymous).Should().Equal(ErrorCodes.Security.Unauthenticated);

        Codes(await _server.RunAsync(sp => sp.GetRequiredService<SyncServer>().OpenDeviceSeriesAsync(new OpenDeviceSeriesRequest(Guid.Empty), default)))
            .Should().Equal(ErrorCodes.RightsSnapshot.DeviceRequired);

        Func<Task> withoutRequest = () => _server.RunAsync(sp => sp.GetRequiredService<SyncServer>().OpenDeviceSeriesAsync(null!, default));
        await withoutRequest.Should().ThrowAsync<ArgumentNullException>();

        _server.Permissions.Granted.Remove(Permission.FactureValider);
        Codes(await _server.RunAsync(sp => sp.GetRequiredService<SyncServer>().OpenDeviceSeriesAsync(new OpenDeviceSeriesRequest(_device), default)))
            .Should().Equal(ErrorCodes.Security.Forbidden);
    }

    [Fact]
    public void L_empreinte_d_une_intention_ne_depend_que_de_son_contenu()
    {
        Guid key = Guid.CreateVersion7();
        Guid id = Guid.CreateVersion7();
        string hash = SyncServer.Hash(new CreateCustomerIntent(key, id, Now, Martin()));

        hash.Should().MatchRegex("^[0-9a-f]{64}$");
        SyncServer.Hash(new CreateCustomerIntent(key, id, Now, Martin())).Should().Be(hash);
        SyncServer.Hash(new CreateCustomerIntent(key, id, Now, Martin() with { City = "Lyon" })).Should().NotBe(hash);
    }

    /// <summary>Un type d'intention que les contrats ne déclarent pas.</summary>
    private sealed record UnknownIntent(Guid IdempotencyKey, Guid EntityId, DateTimeOffset RecordedAt) : SyncIntent(IdempotencyKey, EntityId, RecordedAt);

    private sealed class Anonymous : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public Guid UserId => Guid.Empty;
    }
}

/// <summary>Combien de lectures doivent encore rendre une version périmée (un collègue vient d'écrire).</summary>
internal sealed class StaleReads
{
    private int _remaining;

    public int Remaining
    {
        get => Volatile.Read(ref _remaining);
        set => Volatile.Write(ref _remaining, value);
    }

    public bool Take()
    {
        while (true)
        {
            int current = Remaining;
            if (current <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _remaining, current - 1, current) == current)
            {
                return true;
            }
        }
    }
}

/// <summary>
/// Le serveur des scénarios, dont les lectures de clients et d'articles peuvent rendre une version périmée : la fusion
/// écrit alors sur une version qui n'est plus celle du serveur, comme si un collègue avait écrit entre-temps.
/// </summary>
internal sealed class ReplayServerNode(DossierId dossier, string file, TestClock clock, StaleReads stale)
    : DossierNode(dossier, file, clock, services => Configure(services, stale))
{
    private static void Configure(IServiceCollection services, StaleReads stale)
    {
        services.AddSyncServer().AddSyncJournal().AddSingleton<IOrganisationPolicyStore>(new TestPolicies());
        Decorate<ICustomerReadStore>(services, inner => new StaleCustomers(inner, stale));
        Decorate<ICatalogItemReadStore>(services, inner => new StaleItems(inner, stale));
    }

    private static void Decorate<TService>(IServiceCollection services, Func<TService, TService> wrap)
        where TService : class
    {
        ServiceDescriptor original = services.Last(d => d.ServiceType == typeof(TService));
        Type implementation = original.ImplementationType!;
        services.Add(ServiceDescriptor.Describe(
            typeof(TService), sp => wrap((TService)ActivatorUtilities.CreateInstance(sp, implementation)), original.Lifetime));
    }

    private sealed class StaleCustomers(ICustomerReadStore inner, StaleReads stale) : ICustomerReadStore
    {
        public Task<PagedResponse<CustomerSummary>> ListAsync(PartyListFilter filter, CancellationToken cancellationToken) =>
            inner.ListAsync(filter, cancellationToken);

        public async Task<CustomerResponse?> GetAsync(CustomerId id, CancellationToken cancellationToken)
        {
            CustomerResponse? found = await inner.GetAsync(id, cancellationToken);
            return found is not null && stale.Take() ? found with { Version = found.Version - 1 } : found;
        }
    }

    private sealed class StaleItems(ICatalogItemReadStore inner, StaleReads stale) : ICatalogItemReadStore
    {
        public Task<PagedResponse<CatalogItemSummary>> ListAsync(CatalogItemFilter filter, CancellationToken cancellationToken) =>
            inner.ListAsync(filter, cancellationToken);

        public async Task<CatalogItemResponse?> GetAsync(CatalogItemId id, CancellationToken cancellationToken)
        {
            CatalogItemResponse? found = await inner.GetAsync(id, cancellationToken);
            return found is not null && stale.Take() ? found with { Version = found.Version - 1 } : found;
        }
    }
}
