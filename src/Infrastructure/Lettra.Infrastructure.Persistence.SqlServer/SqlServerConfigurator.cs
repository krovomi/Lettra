using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lettra.Infrastructure.Persistence.SqlServer;

/// <summary>Configure EF Core pour SqlServer ; les migrations de ce provider vivent dans cet assembly.</summary>
public sealed class SqlServerConfigurator : IDbProviderConfigurator
{
    public static readonly string MigrationsAssembly = typeof(SqlServerConfigurator).Assembly.GetName().Name!;

    public DatabaseProvider Provider => DatabaseProvider.SqlServer;

    public void Configure(DbContextOptionsBuilder builder, string connectionString, TenantDescriptor tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        builder.UseSqlServer(connectionString, o =>
        {
            o.MigrationsAssembly(MigrationsAssembly);
            o.EnableRetryOnFailure();
        });
    }
}

/// <summary>Utilisé uniquement par <c>dotnet ef migrations add</c>.</summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<LettraDbContext>
{
    public LettraDbContext CreateDbContext(string[] args)
    {
        TenantDescriptor design = new(
            new Domain.SharedKernel.DossierId(Guid.Empty), default, DatabaseProvider.SqlServer, "design");
        DbContextOptionsBuilder<LettraDbContext> builder = new();
        new SqlServerConfigurator().Configure(builder, "Server=localhost;Database=lettra_design;TrustServerCertificate=True", design);
        return new LettraDbContext(builder.Options, design);
    }
}
