using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Exports;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.Review;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Cabinet;
using Lettra.Infrastructure.Persistence.EInvoicing;
using Lettra.Infrastructure.Persistence.Sqlite;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Vue cabinet sur SQLite : les sources d'indicateurs de la persistance (factures en retard, rejets de la plateforme,
/// revue à faire), le recalcul dossier par dossier et la passe de nuit. Données fictives.
/// </summary>
public sealed class CabinetIndicatorTests : IDisposable
{
    private static readonly Customer.Draft Durand = new(
        CustomerKind.Company, "Atelier Durand", "732829320", "73282932000074", "FR44732829320", "compta@durand.example",
        "3 rue des Lilas", null, "69001", "Lyon", "FR");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Fictif", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: false, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: "FR7630006000011234567890189", Bic: "AGRIFRPP");

    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-cabinet-").FullName;

    private string Database(string name) => $"Data Source={Path.Combine(_folder, name)}.db;Pooling=False";

    [Fact]
    public async Task Factures_en_retard_hors_brouillons_refus_et_encaissements_complets()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        DossierId dossier = harness.AddDossier(DatabaseProvider.Sqlite, Database("retard"));
        InvoiceId partial, refused, collected, paid;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
            Guid durand = (await commands.SendAsync(new CreateCustomerCommand(dossier, Durand), default)).Value.Value;
            OverdueInvoicesIndicatorSource source = Source<OverdueInvoicesIndicatorSource>(scope);
            (await source.ComputeAsync(new DateOnly(2100, 1, 1), default)).Should().Equal(
                new DossierIndicator(DossierIndicatorKeys.OverdueInvoices, 0, IndicatorLevel.Ok));

            partial = await ValidatedAsync(commands, dossier, durand, 100m);
            refused = await ValidatedAsync(commands, dossier, durand, 200m);
            collected = await ValidatedAsync(commands, dossier, durand, 50m);
            paid = await ValidatedAsync(commands, dossier, durand, 80m);

            // Un brouillon à échéance n'est jamais en retard.
            SaveInvoiceRequest draft = Request(durand, 10m);
            draft.DueDate = LegalCalendar.DateOf(DateTimeOffset.UtcNow).AddDays(1);
            (await commands.SendAsync(new CreateInvoiceCommand(dossier, draft), default)).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            LettraDbContext db = await scope.ServiceProvider.GetRequiredService<TenantDbSession>().GetAsync(default);
            DateOnly due = (await db.Set<Invoice>().AsNoTracking().Where(i => i.Id == partial).Select(i => i.DueDate).SingleAsync())!.Value;
            OverdueInvoicesIndicatorSource source = Source<OverdueInvoicesIndicatorSource>(scope);

            // Le jour de l'échéance, rien n'est encore en retard ; le lendemain, les quatre pièces validées le sont.
            (await source.ComputeAsync(due, default)).Should().Equal(new DossierIndicator(DossierIndicatorKeys.OverdueInvoices, 0, IndicatorLevel.Ok));
            (await source.ComputeAsync(due.AddDays(1), default)).Should().Equal(
                new DossierIndicator(DossierIndicatorKeys.OverdueInvoices, 4, IndicatorLevel.Warning, due));

            // Refusée par l'acheteur, encaissée en totalité par la plateforme, réglée en base : plus dues ; un encaissement partiel ne suffit pas.
            decimal collectedTotal = await db.Set<Invoice>().Where(i => i.Id == collected).Select(i => i.TotalInclTax).SingleAsync();
            db.Add(Transmission(refused.Value, 1, (PlatformStatusDto.Deposited, null), (PlatformStatusDto.Refused, null)));
            db.Add(Transmission(collected.Value, 1, (PlatformStatusDto.Collected, collectedTotal / 2), (PlatformStatusDto.Collected, collectedTotal / 2)));
            db.Add(Transmission(partial.Value, 1, (PlatformStatusDto.Collected, 10m), (PlatformStatusDto.Collected, null)));
            await db.SaveChangesAsync();
            await db.Set<Invoice>().Where(i => i.Id == paid).ExecuteUpdateAsync(u => u.SetProperty(i => i.PaidInclTax, i => i.TotalInclTax));

            (await source.ComputeAsync(due.AddDays(1), default)).Should().Equal(
                new DossierIndicator(DossierIndicatorKeys.OverdueInvoices, 1, IndicatorLevel.Warning, due));
            (await source.ComputeAsync(due.AddDays(OverdueInvoicesIndicatorSource.CriticalAfterDays), default)).Should().ContainSingle().Which.Should().Be(
                new DossierIndicator(DossierIndicatorKeys.OverdueInvoices, 1, IndicatorLevel.Warning, due), "trente jours pile restent à surveiller");
            (await source.ComputeAsync(due.AddDays(OverdueInvoicesIndicatorSource.CriticalAfterDays + 1), default)).Should().Equal(
                new DossierIndicator(DossierIndicatorKeys.OverdueInvoices, 1, IndicatorLevel.Critical, due));

            // La dernière pièce due est refusée à son tour : plus rien en retard.
            db.Add(Transmission(partial.Value, 2, (PlatformStatusDto.Refused, null)));
            await db.SaveChangesAsync();
            (await source.ComputeAsync(due.AddDays(1), default)).Should().Equal(new DossierIndicator(DossierIndicatorKeys.OverdueInvoices, 0, IndicatorLevel.Ok));
        }
    }

    [Fact]
    public async Task Rejets_de_la_plateforme_selon_la_derniere_transmission_et_son_dernier_statut()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        DossierId dossier = harness.AddDossier(DatabaseProvider.Sqlite, Database("rejets"));
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        PlatformRejectionsIndicatorSource source = Source<PlatformRejectionsIndicatorSource>(scope);
        DateOnly today = new(2026, 11, 2);
        (await source.ComputeAsync(today, default)).Should().Equal(new DossierIndicator(DossierIndicatorKeys.PlatformRejections, 0, IndicatorLevel.Ok));

        LettraDbContext db = await scope.ServiceProvider.GetRequiredService<TenantDbSession>().GetAsync(default);
        Guid retransmitted = Guid.CreateVersion7();
        db.AddRange(
            Transmission(Guid.CreateVersion7(), 1, (PlatformStatusDto.Rejected, null)),
            Transmission(Guid.CreateVersion7(), 1, (PlatformStatusDto.Deposited, null), (PlatformStatusDto.Rejected, null)),
            Transmission(retransmitted, 1, (PlatformStatusDto.Rejected, null)),
            Transmission(retransmitted, 2, (PlatformStatusDto.Deposited, null)),
            Transmission(Guid.CreateVersion7(), 1, (PlatformStatusDto.Rejected, null), (PlatformStatusDto.Deposited, null)),
            Transmission(Guid.CreateVersion7(), 1));
        await db.SaveChangesAsync();

        (await source.ComputeAsync(today, default)).Should().Equal(
            new DossierIndicator(DossierIndicatorKeys.PlatformRejections, 2, IndicatorLevel.Critical));
    }

    [Fact]
    public async Task Revue_du_mois_ecoule_a_faire_avant_le_15_puis_a_surveiller()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        DossierId dossier = harness.AddDossier(DatabaseProvider.Sqlite, Database("revue"));
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        ReviewPendingIndicatorSource source = Source<ReviewPendingIndicatorSource>(scope);
        int all = DossierReviewChecklist.Items.Count;

        (await source.ComputeAsync(new DateOnly(2026, 11, 15), default)).Should().Equal(
            new DossierIndicator(DossierIndicatorKeys.ReviewPending, all, IndicatorLevel.Info, new DateOnly(2026, 11, 15)));
        (await source.ComputeAsync(new DateOnly(2026, 11, 16), default)).Should().Equal(
            new DossierIndicator(DossierIndicatorKeys.ReviewPending, all, IndicatorLevel.Warning, new DateOnly(2026, 11, 15)));

        IDossierReviewStore marks = scope.ServiceProvider.GetRequiredService<IDossierReviewStore>();
        ReviewPeriod october = ReviewPeriod.OfMonth(2026, 10);
        foreach (string item in DossierReviewChecklist.Items.Skip(1))
        {
            await marks.SaveAsync(DossierReviewMark.Check(october, item, Guid.CreateVersion7(), DateTimeOffset.UtcNow, null).Value, default);
        }

        // Une coche d'une autre période ne compte pas pour octobre.
        await marks.SaveAsync(DossierReviewMark.Check(ReviewPeriod.OfMonth(2026, 9), DossierReviewChecklist.Items[0], Guid.CreateVersion7(), DateTimeOffset.UtcNow, null).Value, default);
        (await source.ComputeAsync(new DateOnly(2026, 11, 3), default)).Should().Equal(
            new DossierIndicator(DossierIndicatorKeys.ReviewPending, 1, IndicatorLevel.Info, new DateOnly(2026, 11, 15)));

        await marks.SaveAsync(DossierReviewMark.Check(october, DossierReviewChecklist.Items[0], Guid.CreateVersion7(), DateTimeOffset.UtcNow, null).Value, default);
        (await source.ComputeAsync(new DateOnly(2026, 11, 20), default)).Should().Equal(
            new DossierIndicator(DossierIndicatorKeys.ReviewPending, 0, IndicatorLevel.Ok));

        // En janvier, c'est décembre de l'année précédente que l'on revoit.
        (await source.ComputeAsync(new DateOnly(2027, 1, 10), default)).Should().Equal(
            new DossierIndicator(DossierIndicatorKeys.ReviewPending, all, IndicatorLevel.Info, new DateOnly(2027, 1, 15)));
    }

    [Fact]
    public async Task Recalcul_range_les_indicateurs_du_jour_a_Paris_et_compte_les_dossiers_inconnus_en_echec()
    {
        // 31 octobre 23 h 30 UTC : déjà le 1er novembre à Paris, la revue à faire est donc celle d'octobre.
        ManualClock clock = new(new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero));
        await using TenantHarness harness = new(new SqliteConfigurator(), clock);
        DossierId dossier = harness.AddDossier(DatabaseProvider.Sqlite, Database("recalcul"));
        await (await harness.OpenAsync(dossier)).DisposeAsync();
        DossierId unknown = new(Guid.CreateVersion7());

        IndicatorRefreshOutcome outcome = await harness.Services.GetRequiredService<IDossierIndicatorRefresher>().RefreshAsync([unknown, dossier], default);

        outcome.Should().Be(new IndicatorRefreshOutcome(1, 1));
        IReadOnlyList<StoredDossierIndicators> stored = await harness.Services.GetRequiredService<IDossierIndicatorStore>().ReadAsync([dossier, unknown], default);
        StoredDossierIndicators row = stored.Should().ContainSingle().Subject;
        row.Dossier.Should().Be(dossier);
        row.ComputedAt.Should().Be(clock.Now);
        row.Indicators.Should().Contain(new DossierIndicator(DossierIndicatorKeys.OverdueInvoices, 0, IndicatorLevel.Ok));
        row.Indicators.Should().Contain(new DossierIndicator(DossierIndicatorKeys.PlatformRejections, 0, IndicatorLevel.Ok));
        row.Indicators.Should().Contain(new DossierIndicator(
            DossierIndicatorKeys.ReviewPending, DossierReviewChecklist.Items.Count, IndicatorLevel.Info, new DateOnly(2026, 11, 15)));
    }

    [Fact]
    public async Task Une_source_en_echec_n_empeche_pas_les_autres()
    {
        await using TenantHarness harness = new(new SqliteConfigurator(), configure: services =>
            services.AddScoped<IDossierIndicatorSource>(_ => new FailingSource(new InvalidOperationException("base illisible"))));
        DossierId dossier = harness.AddDossier(DatabaseProvider.Sqlite, Database("source-ko"));
        await (await harness.OpenAsync(dossier)).DisposeAsync();

        (await harness.Services.GetRequiredService<IDossierIndicatorRefresher>().RefreshAsync([dossier], default))
            .Should().Be(new IndicatorRefreshOutcome(1, 0));
        StoredDossierIndicators row = (await harness.Services.GetRequiredService<IDossierIndicatorStore>().ReadAsync([dossier], default)).Single();
        row.Indicators.Select(i => i.Key).Should().Contain([DossierIndicatorKeys.OverdueInvoices, DossierIndicatorKeys.PlatformRejections, DossierIndicatorKeys.ReviewPending]);
    }

    [Fact]
    public async Task Une_annulation_dans_une_source_interrompt_le_recalcul()
    {
        await using TenantHarness harness = new(new SqliteConfigurator(), configure: services =>
            services.AddScoped<IDossierIndicatorSource>(_ => new FailingSource(new OperationCanceledException())));
        DossierId dossier = harness.AddDossier(DatabaseProvider.Sqlite, Database("annule"));
        await (await harness.OpenAsync(dossier)).DisposeAsync();

        Func<Task> refresh = () => harness.Services.GetRequiredService<IDossierIndicatorRefresher>().RefreshAsync([dossier], default);

        await refresh.Should().ThrowAsync<OperationCanceledException>();
        (await harness.Services.GetRequiredService<IDossierIndicatorStore>().ReadAsync([dossier], default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Un_dossier_impossible_a_ranger_compte_en_echec_sans_arreter_les_suivants()
    {
        FlakyStore store = new(failFirst: true);
        await using TenantHarness harness = new(new SqliteConfigurator(), configure: services => services.AddSingleton<IDossierIndicatorStore>(store));
        DossierId first = harness.AddDossier(DatabaseProvider.Sqlite, Database("range-1"));
        DossierId second = harness.AddDossier(DatabaseProvider.Sqlite, Database("range-2"));
        await (await harness.OpenAsync(first)).DisposeAsync();
        await (await harness.OpenAsync(second)).DisposeAsync();

        (await harness.Services.GetRequiredService<IDossierIndicatorRefresher>().RefreshAsync([first, second], default))
            .Should().Be(new IndicatorRefreshOutcome(1, 1));
        store.Saved.Should().Equal(second);
    }

    [Fact]
    public async Task Refresher_refuse_une_liste_nulle()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        Func<Task> refresh = () => harness.Services.GetRequiredService<IDossierIndicatorRefresher>().RefreshAsync(null!, default);
        await refresh.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Magasin_en_memoire_remplace_et_ne_rend_que_les_dossiers_connus()
    {
        InMemoryDossierIndicatorStore store = new();
        DossierId dossier = new(Guid.CreateVersion7());
        DateTimeOffset at = new(2026, 11, 2, 3, 0, 0, TimeSpan.Zero);
        await store.ReplaceAsync(dossier, [new DossierIndicator(DossierIndicatorKeys.OverdueInvoices, 3, IndicatorLevel.Warning)], at, default);
        await store.ReplaceAsync(dossier, [new DossierIndicator(DossierIndicatorKeys.PlatformRejections, 1, IndicatorLevel.Critical)], at.AddDays(1), default);

        StoredDossierIndicators row = (await store.ReadAsync([dossier, new DossierId(Guid.CreateVersion7())], default)).Should().ContainSingle().Subject;
        row.ComputedAt.Should().Be(at.AddDays(1));
        row.Indicators.Should().Equal(new DossierIndicator(DossierIndicatorKeys.PlatformRejections, 1, IndicatorLevel.Critical));
        Func<Task> read = () => store.ReadAsync(null!, default);
        await read.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Passe_de_nuit_recalcule_les_dossiers_du_catalogue()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        DossierId dossier = harness.AddDossier(DatabaseProvider.Sqlite, Database("nuit"));
        await (await harness.OpenAsync(dossier)).DisposeAsync();
        TenantDescriptor[] dossiers = [harness.Descriptor(dossier), harness.Descriptor(dossier) with { DossierId = new DossierId(Guid.CreateVersion7()) }];

        IndicatorRefreshOutcome outcome = await Job(harness, (_, _) => Task.FromResult<IReadOnlyList<TenantDescriptor>>(dossiers)).RunPassAsync(default);

        outcome.Should().Be(new IndicatorRefreshOutcome(1, 1));
        (await harness.Services.GetRequiredService<IDossierIndicatorStore>().ReadAsync([dossier], default)).Should().ContainSingle();
    }

    [Fact]
    public async Task Passe_de_nuit_avec_un_catalogue_illisible_ne_recalcule_rien()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());

        IndicatorRefreshOutcome outcome = await Job(harness, (_, _) => throw new InvalidOperationException("catalogue hors ligne")).RunPassAsync(default);

        outcome.Should().Be(new IndicatorRefreshOutcome(0, 0));
    }

    [Fact]
    public async Task Passe_de_nuit_annulee_laisse_passer_l_annulation()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        Func<Task> pass = () => Job(harness, (_, token) => Task.FromCanceled<IReadOnlyList<TenantDescriptor>>(new CancellationToken(true))).RunPassAsync(default);

        await pass.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Passe_de_nuit_desactivee_ne_lit_pas_le_catalogue()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        int calls = 0;
        using CabinetOverviewJob job = Job(harness, (_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<IReadOnlyList<TenantDescriptor>>([]);
        }, new CabinetOverviewJobOptions { Enabled = false, InitialDelay = TimeSpan.Zero });

        await job.StartAsync(default);
        await job.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await job.StopAsync(default);

        calls.Should().Be(0);
    }

    [Fact]
    public async Task Passe_de_nuit_activee_tourne_au_demarrage_puis_s_arrete_proprement()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        TaskCompletionSource ran = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CabinetOverviewJob job = Job(harness, (_, _) =>
        {
            ran.TrySetResult();
            return Task.FromResult<IReadOnlyList<TenantDescriptor>>([]);
        }, new CabinetOverviewJobOptions { InitialDelay = TimeSpan.Zero, Interval = TimeSpan.FromHours(1) });

        await job.StartAsync(default);
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await job.StopAsync(default);

        job.ExecuteTask!.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void Enregistrement_de_la_passe_lit_ses_reglages_sous_Lettra_CabinetOverview()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lettra:CabinetOverview:Enabled"] = "false",
            ["Lettra:CabinetOverview:Interval"] = "06:00:00",
            ["Lettra:CabinetOverview:InitialDelay"] = "00:00:05",
        }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddCabinetOverviewJob(configuration, (_, _) => Task.FromResult<IReadOnlyList<TenantDescriptor>>([]));
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().Should().ContainSingle().Which.Should().BeOfType<CabinetOverviewJob>();
        CabinetOverviewJobOptions options = provider.GetRequiredService<IOptions<CabinetOverviewJobOptions>>().Value;
        options.Enabled.Should().BeFalse();
        options.Interval.Should().Be(TimeSpan.FromHours(6));
        options.InitialDelay.Should().Be(TimeSpan.FromSeconds(5));

        Action withoutConfiguration = () => new ServiceCollection().AddCabinetOverviewJob(null!, (_, _) => Task.FromResult<IReadOnlyList<TenantDescriptor>>([]));
        withoutConfiguration.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Reglages_par_defaut_de_la_passe()
    {
        CabinetOverviewJobOptions options = new();
        options.Enabled.Should().BeTrue();
        options.Interval.Should().Be(TimeSpan.FromDays(1));
        options.InitialDelay.Should().Be(TimeSpan.FromMinutes(2));
        CabinetOverviewJobOptions.SectionName.Should().Be("Lettra:CabinetOverview");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    private static CabinetOverviewJob Job(
        TenantHarness harness,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> listDossiers,
        CabinetOverviewJobOptions? options = null) =>
        new(harness.Services, listDossiers, Options.Create(options ?? new CabinetOverviewJobOptions()), TimeProvider.System, NullLogger<CabinetOverviewJob>.Instance);

    private static T Source<T>(AsyncServiceScope scope) => scope.ServiceProvider.GetServices<IDossierIndicatorSource>().OfType<T>().Single();

    private static PlatformTransmissionRecord Transmission(Guid invoice, long submittedAtMs, params (PlatformStatusDto Status, decimal? Amount)[] events)
    {
        Guid id = Guid.CreateVersion7();
        return new PlatformTransmissionRecord
        {
            Id = id,
            InvoiceId = invoice,
            InvoiceNumber = "F-TEST",
            Channel = (int)PlatformChannelDto.EInvoice,
            Platform = "Simulée",
            SubmittedAtMs = submittedAtMs,
            Events = [.. events.Select((e, i) => new PlatformStatusEventRecord { TransmissionId = id, Sequence = i, Status = (int)e.Status, AtMs = submittedAtMs + i, Amount = e.Amount })],
        };
    }

    private static async Task<InvoiceId> ValidatedAsync(ICommandDispatcher commands, DossierId dossier, Guid customer, decimal price)
    {
        Result<InvoiceId> created = await commands.SendAsync(new CreateInvoiceCommand(dossier, Request(customer, price)), default);
        created.IsSuccess.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Code)));
        Result validated = await commands.SendAsync(new ValidateInvoiceCommand(dossier, created.Value, null), default);
        validated.IsSuccess.Should().BeTrue(string.Join(", ", validated.Errors.Select(e => e.Code)));
        return created.Value;
    }

    private static SaveInvoiceRequest Request(Guid customer, decimal price) => new()
    {
        CustomerId = customer,
        VatRegime = VatRegimeDto.Standard,
        Lines = [new DocumentLineRequest { Designation = "Conseil", Quantity = 1, UnitPriceExclTax = price, VatRateCode = "FR-20", Kind = CatalogItemKindDto.Service, UnitCode = "HUR" }],
    };

    private sealed class FailingSource(Exception exception) : IDossierIndicatorSource
    {
        public Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken) => Task.FromException<IReadOnlyList<DossierIndicator>>(exception);
    }

    private sealed class FlakyStore(bool failFirst) : IDossierIndicatorStore
    {
        private bool _failNext = failFirst;

        public List<DossierId> Saved { get; } = [];

        public Task ReplaceAsync(DossierId dossier, IReadOnlyList<DossierIndicator> indicators, DateTimeOffset computedAt, CancellationToken cancellationToken)
        {
            if (_failNext)
            {
                _failNext = false;
                throw new InvalidOperationException("base Catalog injoignable");
            }

            Saved.Add(dossier);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StoredDossierIndicators>> ReadAsync(IReadOnlyCollection<DossierId> dossiers, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StoredDossierIndicators>>([]);
    }
}
