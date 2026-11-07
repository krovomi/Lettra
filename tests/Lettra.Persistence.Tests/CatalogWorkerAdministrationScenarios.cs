using System.Security.Cryptography;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Administration des bases par le Worker seul (plan du mode 5, étape 3) : l'API d'une cellule inscrit la création d'un
/// dossier et attend que le Worker l'ait menée, marque un import manqué « à retirer », rend au Worker les créations en
/// échec. Elle ne se sert jamais de la connexion d'administration des serveurs.
/// </summary>
internal static class CatalogWorkerAdministrationScenarios
{
    /// <summary>La clé du cabinet, la même pour l'API et le Worker, comme dans une cellule.</summary>
    private static readonly string Kek = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static readonly IDictionary<string, string?> ByWorker = new Dictionary<string, string?>
    {
        ["Lettra:Organisations:0:Kek"] = Kek,
        ["Lettra:Catalog:DatabaseAdministration"] = "Worker",
    };

    /// <summary>Le même réglage, avec une API qui n'attend le Worker que 300 ms.</summary>
    public static readonly IDictionary<string, string?> ByImpatientWorker = new Dictionary<string, string?>
    {
        ["Lettra:Organisations:0:Kek"] = Kek,
        ["Lettra:Catalog:DatabaseAdministration"] = "Worker",
        ["Lettra:Catalog:ProvisioningWait"] = "00:00:00.300",
    };

    /// <param name="api">L'API de la cellule : administration au Worker, sans connexion d'administration utilisable.</param>
    /// <param name="impatient">La même API, qui n'attend le Worker que 300 ms.</param>
    /// <param name="worker">Le Worker, sur le même catalogue, seul à détenir la connexion d'administration.</param>
    /// <param name="databaseExists">Si la base d'un nom donné existe sur le serveur.</param>
    public static async Task CreateDiscardAndRetryAsync(
        CatalogHarness api, CatalogHarness impatient, CatalogHarness worker, Func<string, Task<bool>> databaseExists)
    {
        await worker.Services.InitializeCatalogAsync(migrate: true, default);
        IDossierProvisioner provisioner = api.Services.GetRequiredService<IDossierProvisioner>();
        IDossierRequestPass pass = worker.Services.GetRequiredService<IDossierRequestPass>();
        OrganisationId cabinet = CatalogHarness.Organisation;

        // 1. Création : l'API inscrit la demande, le Worker la prend et la mène, l'API rend le dossier ouvert.
        DossierId created;
        using (CancellationTokenSource stop = new())
        {
            Task running = RunWorkerAsync(pass, stop.Token);
            Result<DossierId> result = await provisioner.CreateAsync(cabinet, "Créé par le Worker", default);
            await stop.CancelAsync();
            await running;
            result.Errors.Should().BeEmpty();
            created = result.Value;
        }

        CatalogDossier active = await api.DossierAsync(created);
        active.State.Should().Be(DossierState.Active);
        (await databaseExists(active.DatabaseName!)).Should().BeTrue();
        TenantDescriptor descriptor = (await api.Services.GetRequiredService<ITenantCatalog>().FindAsync(created, default))!;
        await using (LettraDbContext opened = await api.Services.GetRequiredService<IDossierDbContextFactory>().CreateAsync(descriptor, verifyStamp: true, default))
        {
            (await opened.Customers.CountAsync()).Should().Be(0);
        }

        // 2. Import manqué : l'API marque le dossier « à retirer », le cabinet ne le voit plus, le Worker supprime sa base.
        await provisioner.DiscardAsync(created, default);
        (await api.DossierAsync(created)).State.Should().Be(DossierState.Discarding);
        (await api.Services.GetRequiredService<IDossierEraser>().ListAsync(cabinet, default)).Should().BeEmpty();
        (await databaseExists(active.DatabaseName!)).Should().BeTrue("seul le Worker supprime une base");
        (await pass.RunRequestsAsync(default)).Should().Be(1);
        (await databaseExists(active.DatabaseName!)).Should().BeFalse();
        (await CountAsync(api, created)).Should().Be(0, "le registre ne garde pas trace d'un import manqué");

        // 3. Worker en retard : l'API rend un échec, la demande lui reste et il la mène à sa passe suivante.
        (await impatient.Services.GetRequiredService<IDossierProvisioner>().CreateAsync(cabinet, "En attente du Worker", default)).Errors
            .Should().ContainSingle().Which.Code.Should().Be(DossierErrors.ProvisioningFailedCode);
        CatalogDossier waiting;
        await using (CatalogDbContext db = await api.Services.GetRequiredService<IDbContextFactory<CatalogDbContext>>().CreateDbContextAsync())
        {
            waiting = await db.Dossiers.AsNoTracking().SingleAsync(d => d.Name == "En attente du Worker");
        }

        waiting.State.Should().Be(DossierState.Requested);
        (await databaseExists(waiting.DatabaseName!)).Should().BeFalse("l'API ne crée pas la base elle-même");
        (await pass.RunRequestsAsync(default)).Should().Be(1);
        (await pass.RunRequestsAsync(default)).Should().Be(0, "une demande n'est menée qu'une fois");
        DossierId late = new(waiting.Id);
        (await api.DossierAsync(late)).State.Should().Be(DossierState.Active);

        // 4. Réessai par l'administrateur de la plateforme : la création en échec est rendue au Worker.
        await api.UpdateDossierAsync(late, d => d.State = DossierState.Failed);
        (await api.Services.GetRequiredService<ICabinetLifecycle>().RetryFailedDossiersAsync(default)).Should().Be(1);
        (await api.DossierAsync(late)).State.Should().Be(DossierState.Requested);
        (await pass.RunRequestsAsync(default)).Should().Be(1);
        (await api.DossierAsync(late)).State.Should().Be(DossierState.Active);
    }

    private static async Task RunWorkerAsync(IDossierRequestPass pass, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            await pass.RunRequestsAsync(default);
            await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken.None);
        }
    }

    private static async Task<int> CountAsync(CatalogHarness harness, DossierId dossier)
    {
        await using CatalogDbContext db = await harness.Services.GetRequiredService<IDbContextFactory<CatalogDbContext>>().CreateDbContextAsync();
        return await db.Dossiers.CountAsync(d => d.Id == dossier.Value);
    }
}
