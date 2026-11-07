using System.Security.Cryptography;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>
/// DEK des dossiers d'un serveur lue dans le catalogue (modes 2, 3 et 5) : un dossier créé par le catalogue chiffre ses
/// champs sensibles, une base déclarée reçoit sa DEK au démarrage, et une DEK effacée ferme le dossier.
/// </summary>
internal static class CatalogFieldKeyScenarios
{
    public static readonly Guid DeclaredDossier = new("0199e3a0-0000-7000-8000-00000000dec1");

    /// <summary>Une base déclarée du cabinet de test, jamais ouverte ici.</summary>
    public static Dictionary<string, string?> Declared(DatabaseProvider provider) => new()
    {
        ["Lettra:Dossiers:0:Id"] = DeclaredDossier.ToString(),
        ["Lettra:Dossiers:0:OrganisationId"] = CatalogHarness.Organisation.Value.ToString(),
        ["Lettra:Dossiers:0:Name"] = "Dossier déclaré",
        ["Lettra:Dossiers:0:Provider"] = provider.ToString(),
        ["Lettra:Dossiers:0:ConnectionSecretRef"] = "config:Declared",
    };

    public static async Task DossierKeysAsync(CatalogHarness harness, DatabaseProvider provider)
    {
        IServiceProvider services = harness.Services;
        await services.InitializeCatalogAsync(migrate: true, default);
        DossierId declared = new(DeclaredDossier);
        byte[] declaredDek = (await harness.DossierAsync(declared)).WrappedDek;
        declaredDek.Should().HaveCount(60, "une base déclarée reçoit sa DEK dès que la KEK du cabinet est connue");
        await services.InitializeCatalogAsync(migrate: false, default);
        (await harness.DossierAsync(declared)).WrappedDek.Should().Equal(declaredDek, "la première DEK enregistrée reste");

        Result<DossierId> created = await services.GetRequiredService<IDossierProvisioner>().CreateAsync(CatalogHarness.Organisation, "Dossier chiffré", default);
        created.Errors.Should().BeEmpty();
        DossierId dossier = created.Value;
        TenantDescriptor descriptor = (await services.GetRequiredService<ITenantCatalog>().FindAsync(dossier, default))!;
        IDossierDbContextFactory factory = services.GetRequiredService<IDossierDbContextFactory>();
        Supplier supplier = Supplier.Create(SupplierId.New(), SensitiveFieldScenarios.Dupuis, DateTimeOffset.UtcNow).Value;
        await using (LettraDbContext db = await factory.CreateAsync(descriptor, verifyStamp: true, default))
        {
            db.Add(supplier);
            await db.SaveChangesAsync();
            (await SensitiveFieldScenarios.RawAsync(db, provider, "Suppliers", "Iban", "Email")).Single()
                .Should().OnlyContain(v => v!.StartsWith(SensitiveField.EncryptedPrefix, StringComparison.Ordinal));
        }

        await using (LettraDbContext db = await factory.CreateAsync(descriptor, verifyStamp: true, default))
        {
            Supplier read = await db.Set<Supplier>().AsNoTracking().SingleAsync();
            read.Iban!.Value.Should().Be("FR7630006000011234567890189");
            read.Email!.Value.Should().Be("factures@dupuis.fr");
        }

        // Clé du dossier effacée (effacement cryptographique) : le dossier ne s'ouvre plus.
        await harness.UpdateDossierAsync(dossier, d => d.WrappedDek = []);
        services.GetRequiredService<CatalogDossierFieldKeys>().Forget(dossier.Value);
        Func<Task> open = async () => await factory.CreateAsync(descriptor, verifyStamp: true, default);
        await open.Should().ThrowAsync<CryptographicException>().WithMessage("*détruite*");
    }
}
