using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lettra.Infrastructure.Persistence.Sqlite;

/// <summary>Configure EF Core pour Sqlite ; les migrations de ce provider vivent dans cet assembly.</summary>
public sealed class SqliteConfigurator : IDbProviderConfigurator
{
    public static readonly string MigrationsAssembly = typeof(SqliteConfigurator).Assembly.GetName().Name!;

    public DatabaseProvider Provider => DatabaseProvider.Sqlite;

    public void Configure(DbContextOptionsBuilder builder, string connectionString, TenantDescriptor tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        builder.UseSqlite(connectionString, o => o.MigrationsAssembly(MigrationsAssembly));
    }
}

/// <summary>Utilisé uniquement par <c>dotnet ef migrations add</c>.</summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<LettraDbContext>
{
    public LettraDbContext CreateDbContext(string[] args)
    {
        TenantDescriptor design = new(
            new Domain.SharedKernel.DossierId(Guid.Empty), default, DatabaseProvider.Sqlite, "design");
        DbContextOptionsBuilder<LettraDbContext> builder = new();
        new SqliteConfigurator().Configure(builder, "Data Source=design.db", design);
        return new LettraDbContext(builder.Options, design);
    }
}
