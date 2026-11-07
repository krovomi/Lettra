using System.Security.Cryptography;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Catalog;

/// <summary>
/// Résout <c>config:Nom</c> dans <c>ConnectionStrings:Nom</c> : bases déclarées d'un serveur de cabinet
/// (variables d'environnement) ou de démonstration.
/// </summary>
internal sealed class ConfigurationSecretSource(IConfiguration configuration) : ISecretSource
{
    public const string ConfigPrefix = "config:";

    public string Prefix => ConfigPrefix;

    public ValueTask<string> ResolveAsync(string secretRef, CancellationToken cancellationToken)
    {
        string name = secretRef[Prefix.Length..];
        return ValueTask.FromResult(configuration.GetConnectionString(name)
            ?? throw new InvalidOperationException($"Chaîne de connexion « {name} » absente."));
    }
}

/// <summary>
/// Références des bases créées par le catalogue : <c>cluster:{serveur}/{base}</c> (connexion applicative partagée),
/// <c>cluster:{serveur}/{base}@{cabinet}</c> (connexion par le rôle du cabinet, <see cref="CabinetDatabaseRoles"/>) et
/// <c>cluster-admin:{serveur}/{base}</c> (CREATE / DROP DATABASE). Les identifiants du serveur viennent de
/// <c>Lettra:Clusters:{serveur}</c>, injectés depuis le coffre de la cellule : le catalogue ne contient aucun mot de passe.
/// </summary>
internal sealed class ClusterSecretSource(IOptionsMonitor<CatalogOptions> options, CabinetDatabaseRoles roles) : ISecretSource
{
    public const string AppPrefix = "cluster:";

    public string Prefix => AppPrefix;

    public static string AppRef(string clusterId, string database) => $"{AppPrefix}{clusterId}/{database}";

    public static string CabinetRef(string clusterId, string database, Guid organisation) => $"{AppRef(clusterId, database)}@{organisation:N}";

    public static bool IsCabinetRef(string secretRef) => secretRef.StartsWith(AppPrefix, StringComparison.Ordinal) && secretRef.Contains('@', StringComparison.Ordinal);

    public static string AdminRef(string clusterId, string database) => $"{ClusterAdminSecretSource.AdminPrefix}{clusterId}/{database}";

    public async ValueTask<string> ResolveAsync(string secretRef, CancellationToken cancellationToken)
    {
        string body = secretRef[Prefix.Length..];
        int at = body.IndexOf('@', StringComparison.Ordinal);
        if (at < 0)
        {
            return Resolve(options.CurrentValue, body, admin: false);
        }

        string location = body[..at];
        DatabaseOwner owner = await roles.OwnerAsync(Guid.ParseExact(body[(at + 1)..], "N"), location[..location.IndexOf('/', StringComparison.Ordinal)], cancellationToken)
            .ConfigureAwait(false);
        // Les derniers identifiants l'emportent sur ceux du modèle de connexion partagé.
        return $"{Resolve(options.CurrentValue, location, admin: false)};Username={owner.Role};Password={owner.Password}";
    }

    internal static string Resolve(CatalogOptions options, string body, bool admin)
    {
        int slash = body.IndexOf('/', StringComparison.Ordinal);
        string clusterId = body[..slash];
        string database = body[(slash + 1)..];
        if (!options.Clusters.TryGetValue(clusterId, out ClusterEntry? cluster))
        {
            throw new InvalidOperationException($"Serveur de bases « {clusterId} » inconnu de cette cellule.");
        }

        if (cluster.Provider == DatabaseProvider.Sqlite)
        {
            string directory = cluster.Directory ?? throw new InvalidOperationException($"Serveur « {clusterId} » : Directory manquant.");
            return $"Data Source={Path.Combine(directory, database + ".db")}";
        }

        string template = (admin ? cluster.AdminConnectionString ?? cluster.ConnectionString : cluster.ConnectionString)
            ?? throw new InvalidOperationException($"Serveur « {clusterId} » : chaîne de connexion manquante.");
        string key = cluster.Provider == DatabaseProvider.SqlServer ? "Initial Catalog" : "Database";
        return $"{template.TrimEnd(';')};{key}={database}";
    }
}

internal sealed class ClusterAdminSecretSource(IOptionsMonitor<CatalogOptions> options) : ISecretSource
{
    public const string AdminPrefix = "cluster-admin:";

    public string Prefix => AdminPrefix;

    public ValueTask<string> ResolveAsync(string secretRef, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ClusterSecretSource.Resolve(options.CurrentValue, secretRef[Prefix.Length..], admin: true));
}

/// <summary>
/// KEK des cabinets injectées par la cellule (<c>Lettra:Organisations:n:Kek</c>, depuis son coffre). Ne crée jamais
/// de clé : la KEK d'un cabinet naît à son arrivée, côté exploitation (KMS / HSM en cible, annexe F).
/// </summary>
internal sealed class ConfigurationKekStore(IOptionsMonitor<CatalogOptions> options) : IKekStore
{
    public async ValueTask<byte[]> GetOrCreateAsync(OrganisationId organisation, CancellationToken cancellationToken) =>
        await FindAsync(organisation, cancellationToken).ConfigureAwait(false)
            ?? throw new CryptographicException("Clé maîtresse du cabinet absente du coffre de la cellule.");

    public ValueTask<byte[]?> FindAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        string? kek = options.CurrentValue.Organisations.FirstOrDefault(o => o.Id == organisation.Value)?.Kek;
        return ValueTask.FromResult(kek is null ? null : Convert.FromBase64String(kek));
    }
}
