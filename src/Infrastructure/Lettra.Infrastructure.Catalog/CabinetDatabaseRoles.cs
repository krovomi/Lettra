using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Catalog;

/// <summary>
/// Rôles de connexion par cabinet sur les serveurs PostgreSQL qui les activent (<c>Lettra:Clusters:{serveur}:CabinetRoles</c>,
/// plan du mode 5, étape 3). Le mot de passe d'un rôle n'est stocké nulle part : il se dérive de la clé du cabinet
/// (HKDF), propre à chaque serveur, et disparaît avec elle au départ du cabinet.
/// </summary>
internal sealed class CabinetDatabaseRoles(IKekStore keks, IOptionsMonitor<CatalogOptions> options)
{
    public static string RoleOf(Guid organisation) => $"c_{organisation:N}";

    public bool AppliesTo(string clusterId) =>
        options.CurrentValue.Clusters.TryGetValue(clusterId, out ClusterEntry? cluster)
        && cluster.Provider == DatabaseProvider.PostgreSql
        && cluster.CabinetRoles;

    public async ValueTask<DatabaseOwner> OwnerAsync(Guid organisation, string clusterId, CancellationToken cancellationToken)
    {
        byte[] kek = await keks.FindAsync(new OrganisationId(organisation), cancellationToken).ConfigureAwait(false)
            ?? throw new CryptographicException("Clé du cabinet indisponible : rôle de connexion impossible à ouvrir.");
        byte[] password = HKDF.DeriveKey(HashAlgorithmName.SHA256, kek, 32, salt: [], info: Encoding.UTF8.GetBytes("lettra:role-postgresql:" + clusterId));
        return new DatabaseOwner(RoleOf(organisation), Base64Url.EncodeToString(password));
    }
}
