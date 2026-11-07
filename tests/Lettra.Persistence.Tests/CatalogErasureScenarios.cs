using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.Persistence.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Effacement d'un dossier par son cabinet (plan du mode 5, étape 2) : fermé aussitôt, annulable pendant le délai de
/// rétractation, puis clé détruite et base supprimée par la passe d'effacement. Joué à l'identique sur chaque provider.
/// </summary>
internal static class CatalogErasureScenarios
{
    public static readonly DateTimeOffset Start = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    public static Action<IServiceCollection> Clock(ManualClock clock) => s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));

    /// <param name="harness">Le catalogue, avec un serveur de bases.</param>
    /// <param name="clock">L'horloge du catalogue, avancée pour franchir le délai de rétractation.</param>
    /// <param name="databaseExists">Dit si la base du dossier (son nom sur le serveur) existe encore.</param>
    public static async Task ScheduleCancelAndEraseAsync(CatalogHarness harness, ManualClock clock, Func<string, Task<bool>> databaseExists)
    {
        IServiceProvider services = harness.Services;
        await services.InitializeCatalogAsync(migrate: true, default);
        IDossierEraser eraser = services.GetRequiredService<IDossierEraser>();
        ITenantCatalog catalog = services.GetRequiredService<ITenantCatalog>();
        CatalogDossierEraser pass = services.GetRequiredService<CatalogDossierEraser>();
        OrganisationId cabinet = CatalogHarness.Organisation;
        Guid admin = Guid.CreateVersion7();
        DossierId dossier = (await services.GetRequiredService<IDossierProvisioner>().CreateAsync(cabinet, "Dossier à effacer", default)).Value;
        string database = (await harness.DossierAsync(dossier)).DatabaseName!;
        (await databaseExists(database)).Should().BeTrue();
        (await eraser.ListAsync(cabinet, default)).Should().ContainSingle(d => d.Id == dossier.Value).Which.Should()
            .Be(new CabinetDossierResponse(dossier.Value, "Dossier à effacer", CabinetDossierStateDto.Active, Erasable: true, ErasureDueAt: null));
        await harness.UpdateDossierAsync(dossier, d => d.State = DossierState.Failed);
        (await eraser.ListAsync(cabinet, default)).Should().ContainSingle(d => d.Id == dossier.Value)
            .Which.Should().Match<CabinetDossierResponse>(d => d.State == CabinetDossierStateDto.Creating && !d.Erasable, "une création en échec est reprise, pas effacée");
        await harness.UpdateDossierAsync(dossier, d => d.State = DossierState.Active);

        // Un autre cabinet ne voit pas le dossier ; un dossier inconnu non plus.
        (await eraser.ScheduleAsync(new OrganisationId(CatalogHarness.OtherCabinet), dossier, admin, default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(TenancyErrors.DossierNotFoundCode);
        (await eraser.ScheduleAsync(cabinet, new DossierId(Guid.CreateVersion7()), admin, default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(TenancyErrors.DossierNotFoundCode);

        // Demandé : fermé aussitôt, plus listé, annulable jusqu'à la fin du délai de rétractation (30 jours).
        DossierErasureResponse scheduled = (await eraser.ScheduleAsync(cabinet, dossier, admin, default)).Value;
        scheduled.Should().Be(new DossierErasureResponse(dossier.Value, "Dossier à effacer", Start.AddDays(30)));
        (await catalog.FindAsync(dossier, default)).Should().BeNull();
        (await services.GetRequiredService<IDossierDirectory>().ListAsync(cabinet, default)).Should().NotContain(d => d.Id == dossier.Value);
        (await eraser.ListScheduledAsync(cabinet, default)).Should().Equal(scheduled);
        (await eraser.ListScheduledAsync(new OrganisationId(CatalogHarness.OtherCabinet), default)).Should().BeEmpty();
        (await eraser.ListAsync(cabinet, default)).Should().ContainSingle(d => d.Id == dossier.Value).Which.Should()
            .Be(new CabinetDossierResponse(dossier.Value, "Dossier à effacer", CabinetDossierStateDto.ErasureScheduled, Erasable: false, Start.AddDays(30)));
        (await eraser.ListAsync(new OrganisationId(CatalogHarness.OtherCabinet), default)).Should().NotContain(d => d.Id == dossier.Value);
        (await harness.DossierAsync(dossier)).ErasureRequestedBy.Should().Be(admin);
        (await eraser.ScheduleAsync(cabinet, dossier, admin, default)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(TenancyErrors.DossierNotFoundCode, "un dossier déjà fermé ne se redemande pas");

        // Annulé : le dossier rouvre tel quel.
        (await eraser.CancelAsync(cabinet, dossier, default)).Value.Should().Be(new DossierSummary(dossier.Value, "Dossier à effacer"));
        (await catalog.FindAsync(dossier, default)).Should().NotBeNull();
        (await eraser.CancelAsync(cabinet, dossier, default)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(DossierErrors.ErasureNotCancellableCode);

        // Redemandé : rien n'est détruit avant la fin du délai.
        await eraser.ScheduleAsync(cabinet, dossier, admin, default);
        clock.Now = Start.AddDays(29);
        (await pass.EraseDueAsync(default)).Should().Be(0);
        (await harness.DossierAsync(dossier)).WrappedDek.Should().NotBeEmpty();

        // Délai passé : plus d'annulation, puis clé détruite, base supprimée, dossier noté effacé.
        clock.Now = Start.AddDays(30).AddMinutes(1);
        (await eraser.CancelAsync(cabinet, dossier, default)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(DossierErrors.ErasureNotCancellableCode);
        (await pass.EraseDueAsync(default)).Should().Be(1);
        CatalogDossier erased = await harness.DossierAsync(dossier);
        erased.State.Should().Be(DossierState.Deleted);
        erased.WrappedDek.Should().BeEmpty();
        erased.ErasedAt.Should().NotBeNull();
        (await databaseExists(database)).Should().BeFalse();
        (await pass.EraseDueAsync(default)).Should().Be(0, "un dossier effacé ne l'est qu'une fois");
        (await eraser.CancelAsync(cabinet, dossier, default)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(TenancyErrors.DossierNotFoundCode);
        (await eraser.ListScheduledAsync(cabinet, default)).Should().BeEmpty();
        (await eraser.ListAsync(cabinet, default)).Should().NotContain(d => d.Id == dossier.Value, "un dossier effacé n'est plus géré");
    }

    /// <summary>Une base déclarée dans la configuration ne s'efface pas par l'API.</summary>
    public static async Task DeclaredDossierRefusedAsync(CatalogHarness harness)
    {
        await harness.Services.InitializeCatalogAsync(migrate: true, default);
        Result<DossierErasureResponse> refused = await harness.Services.GetRequiredService<IDossierEraser>()
            .ScheduleAsync(CatalogHarness.Organisation, new DossierId(CatalogFieldKeyScenarios.DeclaredDossier), Guid.CreateVersion7(), default);
        refused.Errors.Should().ContainSingle().Which.Code.Should().Be(DossierErrors.DeclaredCode);
        (await harness.Services.GetRequiredService<IDossierEraser>().ListAsync(CatalogHarness.Organisation, default))
            .Should().ContainSingle(d => d.Id == CatalogFieldKeyScenarios.DeclaredDossier)
            .Which.Should().Match<CabinetDossierResponse>(d => d.State == CabinetDossierStateDto.Active && !d.Erasable);
    }
}
