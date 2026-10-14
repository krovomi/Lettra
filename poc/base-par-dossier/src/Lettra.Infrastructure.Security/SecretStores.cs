using Lettra.Application.Abstractions.Security;
using Microsoft.Extensions.Configuration;

namespace Lettra.Infrastructure.Security;

/// Références de connexion stockées dans le catalogue : <c>cluster:{clusterId}/{base}</c> (via PgBouncer)
/// ou <c>cluster-admin:{clusterId}/{base}</c> (connexion directe pour CREATE / DROP DATABASE).
/// Les identifiants du cluster viennent de la configuration (variables d'environnement injectées depuis
/// le coffre de la cellule) : le catalogue ne contient aucun mot de passe.
public sealed class ClusterSecretStore(IConfiguration configuration) : ISecretStore
{
    public const string AppPrefix = "cluster:";
    public const string AdminPrefix = "cluster-admin:";

    public static string AppRef(string clusterId, string database) => $"{AppPrefix}{clusterId}/{database}";
    public static string AdminRef(string clusterId, string database) => $"{AdminPrefix}{clusterId}/{database}";

    public ValueTask<string> GetConnectionStringAsync(string secretRef, CancellationToken ct = default)
    {
        var admin = secretRef.StartsWith(AdminPrefix, StringComparison.Ordinal);
        var body = secretRef[(admin ? AdminPrefix : AppPrefix).Length..];
        var slash = body.IndexOf('/', StringComparison.Ordinal);
        var clusterId = body[..slash];
        var database = body[(slash + 1)..];

        var section = configuration.GetSection($"Lettra:Clusters:{clusterId}");
        var template = section[admin ? "AdminConnectionString" : "ConnectionString"]
                       ?? throw new InvalidOperationException($"Cluster {clusterId} inconnu de cette cellule.");
        var provider = section["Provider"] ?? "PostgreSql";
        var key = provider == "SqlServer" ? "Initial Catalog" : "Database";
        return ValueTask.FromResult($"{template.TrimEnd(';')};{key}={database}");
    }
}

/// Aiguille une référence vers le coffre qui sait la résoudre.
public sealed class CompositeSecretStore(IEnumerable<(string Prefix, ISecretStore Store)> routes) : ISecretStore
{
    private readonly (string Prefix, ISecretStore Store)[] _routes = [.. routes.OrderByDescending(r => r.Prefix.Length)];

    public ValueTask<string> GetConnectionStringAsync(string secretRef, CancellationToken ct = default)
    {
        foreach (var (prefix, store) in _routes)
            if (secretRef.StartsWith(prefix, StringComparison.Ordinal))
                return store.GetConnectionStringAsync(secretRef, ct);
        throw new InvalidOperationException("Référence de secret non prise en charge.");
    }
}
