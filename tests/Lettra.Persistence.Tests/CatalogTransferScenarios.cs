using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Réversibilité sur un serveur à catalogue (modes 2, 3 et 5) : un dossier importé garde son identifiant, un identifiant
/// déjà connu (même effacé) est refusé, un import manqué est retiré sans trace, et la DEK du dossier importé est neuve.
/// </summary>
internal static class CatalogTransferScenarios
{
    public static async Task ExportDiscardAndImportAsync(CatalogHarness harness, Func<string, Task<bool>> databaseExists)
    {
        IServiceProvider services = harness.Services;
        await services.InitializeCatalogAsync(migrate: true, default);
        IDossierProvisioner provisioner = services.GetRequiredService<IDossierProvisioner>();
        ITenantCatalog catalog = services.GetRequiredService<ITenantCatalog>();
        IDossierDbContextFactory factory = services.GetRequiredService<IDossierDbContextFactory>();
        OrganisationId cabinet = CatalogHarness.Organisation;

        DossierId dossier = (await provisioner.CreateAsync(cabinet, "Dossier à rapatrier", default)).Value;
        CatalogDossier original = await harness.DossierAsync(dossier);
        await using (LettraDbContext db = await factory.CreateAsync((await catalog.FindAsync(dossier, default))!, verifyStamp: true, default))
        {
            db.Add(Supplier.Create(SupplierId.New(), SensitiveFieldScenarios.Dupuis, DateTimeOffset.UtcNow).Value);
            await db.SaveChangesAsync();
        }

        IDossierTransfer transfer = services.GetRequiredService<IDossierTransfer>();
        Result<DossierPackage> exported = await transfer.ExportAsync(cabinet, dossier, DossierTransferScenarios.Passphrase, default);
        exported.Errors.Should().BeEmpty();
        using MemoryStream package = new();
        await using (Stream content = exported.Value.Content)
        {
            await content.CopyToAsync(package);
        }

        (await provisioner.CreateAsync(cabinet, dossier, "Doublon", default)).Errors
            .Should().ContainSingle().Which.Code.Should().Be(DossierErrors.AlreadyExistsCode);

        // Retiré : base supprimée, registre sans trace, identifiant de nouveau libre.
        await provisioner.DiscardAsync(dossier, default);
        (await databaseExists(original.DatabaseName!)).Should().BeFalse();
        await using (CatalogDbContext db = await services.GetRequiredService<IDbContextFactory<CatalogDbContext>>().CreateDbContextAsync())
        {
            (await db.Dossiers.AnyAsync(d => d.Id == dossier.Value)).Should().BeFalse();
        }

        package.Position = 0;
        Result<DossierSummary> imported = await transfer.ImportAsync(cabinet, package, DossierTransferScenarios.Passphrase, default);
        imported.Errors.Should().BeEmpty();
        imported.Value.Should().Be(new DossierSummary(dossier.Value, "Dossier à rapatrier"));
        CatalogDossier arrived = await harness.DossierAsync(dossier);
        arrived.State.Should().Be(DossierState.Active);
        arrived.WrappedDek.Should().NotEqual(original.WrappedDek, "le dossier importé a sa propre DEK");
        await using (LettraDbContext db = await factory.CreateAsync((await catalog.FindAsync(dossier, default))!, verifyStamp: true, default))
        {
            (await db.Set<Supplier>().AsNoTracking().SingleAsync()).Iban!.Value.Should().Be("FR7630006000011234567890189");
            (await SensitiveFieldScenarios.RawAsync(db, arrived.Provider, "Suppliers", "Iban")).Single().Single()
                .Should().StartWith(SensitiveField.EncryptedPrefix);
        }

        // Un dossier effacé ici garde sa trace au registre : son identifiant n'est pas repris.
        await harness.UpdateDossierAsync(dossier, d => d.State = DossierState.Deleted);
        (await provisioner.CreateAsync(cabinet, dossier, "Revenant", default)).Errors
            .Should().ContainSingle().Which.Code.Should().Be(DossierErrors.AlreadyExistsCode);
        (await transfer.ExportAsync(cabinet, dossier, DossierTransferScenarios.Passphrase, default)).Errors
            .Should().ContainSingle().Which.Code.Should().Be(DossierErrors.NotFoundCode, "un dossier effacé ne s'exporte plus");
    }
}
