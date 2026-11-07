using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Un rôle PostgreSQL par cabinet (plan du mode 5, étape 3) : chaque base de dossier appartient au rôle de son cabinet,
/// qui l'ouvre et la migre ; le rôle d'un cabinet est refusé par le serveur sur la base d'un autre ; une base créée avant
/// l'activation passe au rôle de son cabinet sans coupure.
/// </summary>
internal static class CatalogCabinetRoleScenarios
{
    /// <param name="harness">Le catalogue, sur un serveur PostgreSQL qui active les rôles par cabinet, avec deux cabinets.</param>
    /// <param name="legacy">Un dossier du cabinet de test créé avant l'activation des rôles (connexion partagée).</param>
    /// <param name="verifierOf">Le vérificateur SCRAM qu'a gardé le serveur pour un rôle.</param>
    public static async Task IsolationAndAdoptionAsync(CatalogHarness harness, DossierId legacy, Func<string, Task<string>> verifierOf)
    {
        IServiceProvider services = harness.Services;
        await services.InitializeCatalogAsync(migrate: true, default);
        IDossierProvisioner provisioner = services.GetRequiredService<IDossierProvisioner>();
        ISecretStore secrets = services.GetRequiredService<ISecretStore>();
        OrganisationId other = new(CatalogHarness.OtherCabinet);

        // Les nouvelles bases naissent au rôle de leur cabinet, qui les migre.
        CatalogDossier mine = await harness.DossierAsync((await provisioner.CreateAsync(CatalogHarness.Organisation, "Dossier du cabinet", default)).Value);
        CatalogDossier theirs = await harness.DossierAsync((await provisioner.CreateAsync(other, "Dossier d'un autre cabinet", default)).Value);
        mine.State.Should().Be(DossierState.Active);
        mine.ConnectionSecretRef.Should().Be($"cluster:srv-a/{mine.DatabaseName}@{CatalogHarness.Organisation.Value:N}");
        string myConnection = await secrets.GetConnectionStringAsync(mine.ConnectionSecretRef, default);
        (await OwnershipAsync(myConnection)).Should().Be((CabinetDatabaseRoles.RoleOf(CatalogHarness.Organisation.Value), 0));
        (await OwnershipAsync(await secrets.GetConnectionStringAsync(theirs.ConnectionSecretRef, default)))
            .Should().Be((CabinetDatabaseRoles.RoleOf(CatalogHarness.OtherCabinet), 0));

        // Le rôle du cabinet, pointé par erreur vers la base d'un autre cabinet : le serveur refuse la connexion.
        string misrouted = new NpgsqlConnectionStringBuilder(myConnection) { Database = theirs.DatabaseName }.ConnectionString;
        Func<Task> open = () => OwnershipAsync(misrouted);
        (await open.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);

        // La base d'avant les rôles passe au rôle de son cabinet, tables comprises, puis s'ouvre par lui.
        (await harness.DossierAsync(legacy)).ConnectionSecretRef.Should().NotContain("@");
        string role = CabinetDatabaseRoles.RoleOf(CatalogHarness.Organisation.Value);
        string verifier = await verifierOf(role);
        (await services.AdoptCabinetDatabaseRolesAsync(default)).Should().Be(1);
        (await verifierOf(role)).Should().Be(verifier, "redonner le rôle à une base ne change pas son vérificateur, gardé par PgBouncer");
        CatalogDossier adopted = await harness.DossierAsync(legacy);
        adopted.ConnectionSecretRef.Should().EndWith($"@{CatalogHarness.Organisation.Value:N}");
        (await OwnershipAsync(await secrets.GetConnectionStringAsync(adopted.ConnectionSecretRef, default)))
            .Should().Be((role, 0));
        (await services.AdoptCabinetDatabaseRolesAsync(default)).Should().Be(0, "une base déjà passée n'est pas reprise");

        // Le dossier s'ouvre sous son identité par le rôle de son cabinet.
        TenantDescriptor descriptor = (await services.GetRequiredService<ITenantCatalog>().FindAsync(legacy, default))!;
        await using LettraDbContext opened = await services.GetRequiredService<IDossierDbContextFactory>().CreateAsync(descriptor, verifyStamp: true, default);
        (await opened.Customers.CountAsync()).Should().Be(0);
    }

    /// <summary>Le rôle de la connexion et le nombre de tables de la base qui ne lui appartiennent pas.</summary>
    private static async Task<(string Role, long Foreign)> OwnershipAsync(string connectionString)
    {
        await using NpgsqlConnection connection = new(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(
            "SELECT current_user::text, (SELECT count(*) FROM pg_tables WHERE schemaname = 'public' AND tableowner <> current_user)", connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetInt64(1));
    }
}
