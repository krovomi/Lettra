using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lettra.Infrastructure.Persistence.PostgreSql;

/// <summary>Configure EF Core pour PostgreSql ; les migrations de ce provider vivent dans cet assembly.</summary>
public sealed class PostgreSqlConfigurator : IDbProviderConfigurator
{
    public static readonly string MigrationsAssembly = typeof(PostgreSqlConfigurator).Assembly.GetName().Name!;

    public DatabaseProvider Provider => DatabaseProvider.PostgreSql;

    public void Configure(DbContextOptionsBuilder builder, string connectionString, TenantDescriptor tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        builder.UseNpgsql(connectionString, o =>
        {
            o.MigrationsAssembly(MigrationsAssembly);
            o.EnableRetryOnFailure(maxRetryCount: 3);
            if (tenant.Schema is not null)
            {
                o.MigrationsHistoryTable("__EFMigrationsHistory", tenant.Schema);
            }
        });
    }
}

/// <summary>Utilisé uniquement par <c>dotnet ef migrations add</c>.</summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<LettraDbContext>
{
    public LettraDbContext CreateDbContext(string[] args)
    {
        TenantDescriptor design = new(
            new Domain.SharedKernel.DossierId(Guid.Empty), default, DatabaseProvider.PostgreSql, "design");
        DbContextOptionsBuilder<LettraDbContext> builder = new();
        new PostgreSqlConfigurator().Configure(builder, "Host=localhost;Database=lettra_design", design);
        return new LettraDbContext(builder.Options, design);
    }
}
