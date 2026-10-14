using Lettra.Application.Abstractions.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Tenancy;

/// Stratégie par fournisseur : branche le provider EF et son assembly de migrations.
public interface IDbProviderConfigurator
{
    DatabaseProvider Provider { get; }
    void Configure(DbContextOptionsBuilder builder, string connectionString, TenantDescriptor tenant);
}

/// Opérations propres au fournisseur qui sortent d'EF : créer ou supprimer une base de dossier.
public interface IDatabaseAdministrator
{
    DatabaseProvider Provider { get; }
    Task CreateDatabaseAsync(string connectionString, CancellationToken ct = default);
    Task DropDatabaseAsync(string connectionString, CancellationToken ct = default);
}
