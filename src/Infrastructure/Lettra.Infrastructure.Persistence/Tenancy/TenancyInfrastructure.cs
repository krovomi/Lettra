using Lettra.Application.Abstractions.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Tenancy;

/// <summary>Résout une référence de secret (<c>ConnectionSecretRef</c>) en chaîne de connexion. Jamais de secret en clair dans le catalogue.</summary>
public interface ISecretStore
{
    ValueTask<string> GetConnectionStringAsync(string secretRef, CancellationToken cancellationToken);
}

/// <summary>Stratégie par provider : configure EF pour SQLite, PostgreSQL ou SQL Server.</summary>
public interface IDbProviderConfigurator
{
    DatabaseProvider Provider { get; }

    void Configure(DbContextOptionsBuilder builder, string connectionString, TenantDescriptor tenant);
}

/// <summary>Dossier courant (scoped). Installé par <see cref="TenantInitializer"/>.</summary>
public sealed class TenantContext : ITenantContext
{
    private TenantDescriptor? _current;

    public TenantDescriptor Current =>
        _current ?? throw new InvalidOperationException("Aucun dossier actif.");

    public bool IsResolved => _current is not null;

    internal void Set(TenantDescriptor descriptor) => _current = descriptor;
}
