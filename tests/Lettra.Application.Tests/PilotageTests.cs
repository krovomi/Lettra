using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Kpi;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Dossiers;
using Lettra.Application.Features.Kpi;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Pilotage;
using Lettra.Domain.Billing;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Tests;

/// <summary>
/// Tableaux de bord dans Lettra : le dossier ouvert se calcule à l'instant ; « Mes dossiers » et « Administration »
/// lisent la dernière collecte du serveur et n'en rendent que les dossiers ouverts à l'utilisateur.
/// </summary>
public sealed class PilotageTests
{
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());
    private static readonly OrganisationId Cabinet = new(Guid.CreateVersion7());
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Les_valeurs_de_la_collecte_deviennent_une_ligne_par_dossier_avec_ses_douze_mois()
    {
        KpiSample[] samples =
        [
            new(KpiMetrics.DaysSalesOutstanding, 42.4, Dossier),
            new(KpiMetrics.OverdueReceivables, 0.25, Dossier),
            new(KpiMetrics.Collection, 0.9, Dossier),
            new(KpiMetrics.RevenueTrend, -0.12, Dossier),
            new(KpiMetrics.ActiveCustomers, 7, Dossier),
            new(KpiMetrics.TopCustomerShare, double.NaN, Dossier),
            new(KpiMetrics.QuotesPending, 2.6, Dossier),
            new(KpiMetrics.InvoicesIssued, 3, Dossier, new KpiLabel(KpiMetrics.Labels.Month, "2026-10")),
            new(KpiMetrics.RevenueIndex, 120, Dossier, new KpiLabel(KpiMetrics.Labels.Month, "2026-10")),
            new(KpiMetrics.InvoicesIssued, 1, Dossier, new KpiLabel(KpiMetrics.Labels.Month, "2026-09")),
        ];

        DossierPilotageDto row = PilotageMapper.ToDossier(Dossier.Value, "Atelier Durand", samples);

        row.Name.Should().Be("Atelier Durand");
        row.DaysSalesOutstanding.Should().Be(42.4);
        row.OverdueReceivablesRatio.Should().Be(0.25);
        row.RevenueTrendRatio.Should().Be(-0.12);
        row.ActiveCustomers.Should().Be(7);
        row.TopCustomerShareRatio.Should().BeNull("une valeur non finie n'est pas affichée");
        row.QuoteConversionRatio.Should().BeNull("aucun devis tranché");
        row.QuotesPending.Should().Be(3, "un compteur s'arrondit");
        row.QuotesExpiringSoon.Should().Be(0);
        row.Months.Should().Equal(new MonthlyActivityDto("2026-09", null, 1), new MonthlyActivityDto("2026-10", 120, 3));
    }

    [Fact]
    public void Les_valeurs_d_exploitation_comptent_les_postes_par_etat()
    {
        KpiSample[] samples =
        [
            new(KpiMetrics.DatabaseSize, 1_572_864, Dossier),
            new(KpiMetrics.Devices, 3, Dossier, new KpiLabel(KpiMetrics.Labels.State, "active")),
            new(KpiMetrics.Devices, 1, Dossier, new KpiLabel(KpiMetrics.Labels.State, "revoked")),
            new(KpiMetrics.SilentDevices, 1, Dossier),
            new(KpiMetrics.OldestDeviceSyncAge, 900_000, Dossier),
        ];

        DossierOperationsDto row = PilotageMapper.ToOperations(Dossier.Value, "Atelier Durand", samples);

        row.Should().Be(new DossierOperationsDto(Dossier.Value, "Atelier Durand", 1_572_864, 3, 1, 1, 900_000, null));
    }

    [Fact]
    public async Task Le_dossier_ouvert_se_calcule_a_l_instant_avec_ce_qui_reste_a_faire()
    {
        InMemoryCabinetStores stores = new();
        await stores.ReplaceAsync(Dossier, [new DossierIndicator(DossierIndicatorKeys.OverdueInvoices, 2, IndicatorLevel.Warning)], Now.AddHours(-8), default);
        InvoiceKpiFact sold = new(InvoiceKind.Invoice, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Guid.CreateVersion7(), 100m, 120m, 0m, 0m);
        DossierPilotageHandler handler = new(
            new FixedBilling(new BillingKpiFacts([sold], [])),
            stores,
            new FakeTenantContext { Descriptor = new TenantDescriptor(Dossier, Cabinet, DatabaseProvider.Sqlite, "config:test") },
            new NamedDossiers(new DossierSummary(Dossier.Value, "Atelier Durand")),
            new FixedClock(Now));

        DossierPilotageResponse response = (await handler.HandleAsync(new GetDossierPilotageQuery(Dossier), default)).Value;

        response.ComputedAt.Should().Be(Now);
        response.Dossier.Name.Should().Be("Atelier Durand");
        response.Dossier.ActiveCustomers.Should().Be(1);
        response.Dossier.Months.Should().HaveCount(12).And.Contain(m => m.Month == "2026-10" && m.InvoicesIssued == 1);
        response.ToDoComputedAt.Should().Be(Now.AddHours(-8));
        response.ToDo.Should().ContainSingle(i => i.Key == DossierIndicatorKeys.OverdueInvoices && i.Count == 2);
    }

    [Fact]
    public async Task Sans_collecte_sur_ce_poste_les_vues_du_cabinet_se_disent_indisponibles()
    {
        CabinetPilotageHandlers handlers = Handlers();

        (await handlers.HandleAsync(new GetCabinetPilotageQuery(), default)).Value.Should().Be(new CabinetPilotageResponse(false, null, []));
        (await handlers.HandleAsync(new GetAdministrationPilotageQuery(), default)).Value.Should().Be(new AdministrationPilotageResponse(false, null, []));
    }

    [Fact]
    public async Task Une_collecte_ne_rend_rien_a_qui_n_a_aucun_droit_sur_ses_dossiers()
    {
        KpiSnapshot snapshot = new(Now, [new KpiDossier(Dossier, "Atelier Durand", "Cabinet")], [new KpiSample(KpiMetrics.DaysSalesOutstanding, 30, Dossier)]);
        CabinetPilotageHandlers handlers = Handlers(new FixedSnapshot(snapshot));

        CabinetPilotageResponse cabinet = (await handlers.HandleAsync(new GetCabinetPilotageQuery(), default)).Value;
        AdministrationPilotageResponse administration = (await handlers.HandleAsync(new GetAdministrationPilotageQuery(), default)).Value;

        cabinet.Should().Be(new CabinetPilotageResponse(true, Now, []));
        administration.Should().Be(new AdministrationPilotageResponse(true, Now, []));
    }

    [Fact]
    public void Chaque_vue_exige_sa_permission()
    {
        new GetDossierPilotageQuery(Dossier).Permission.Should().Be(Permission.DossierLire);
        new GetCabinetPilotageQuery().Permission.Should().Be(Permission.DossierLire);
        new GetAdministrationPilotageQuery().Permission.Should().Be(Permission.DossierAdministrer);
    }

    private static CabinetPilotageHandlers Handlers(params IKpiSnapshotProvider[] providers) => new(
        new AccessibleDossiers(new FakeUser(), new EmptyAuthorizationDirectory(), new NamedDossiers(), TimeProvider.System),
        providers);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedBilling(BillingKpiFacts facts) : IBillingKpiReader
    {
        public Task<BillingKpiFacts> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(facts);
    }

    private sealed class FixedSnapshot(KpiSnapshot snapshot) : IKpiSnapshotProvider
    {
        public Task<KpiSnapshot> GetAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class NamedDossiers(params DossierSummary[] dossiers) : IDossierDirectory
    {
        public Task<IReadOnlyList<DossierSummary>> ListAsync(OrganisationId organisation, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DossierSummary>>(dossiers);
    }
}
