using System.Security.Cryptography;
using System.Text;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Tenancy;
using Npgsql;

namespace Lettra.Infrastructure.Persistence.PostgreSql;

/// <summary>
/// Un rôle de connexion par cabinet et par serveur. La base d'un dossier appartient au rôle de son cabinet, et le droit
/// de s'y connecter est retiré à tous les autres rôles (PUBLIC) : une connexion faite avec le rôle d'un autre cabinet
/// est refusée par le serveur lui-même. Le mot de passe part déjà haché (SCRAM-SHA-256) : il n'apparaît jamais en
/// clair, ni dans la requête, ni dans un journal du serveur. Le rôle d'administration de Lettra devient membre du rôle,
/// pour lui transférer la base et pouvoir la supprimer ensuite.
/// </summary>
public sealed class PostgreSqlCabinetRoleAdministrator : ICabinetRoleAdministrator
{
    public DatabaseProvider Provider => DatabaseProvider.PostgreSql;

    public async Task AssignOwnerAsync(string adminConnectionString, DatabaseOwner owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        NpgsqlConnectionStringBuilder builder = new(adminConnectionString) { Pooling = false };
        string database = builder.Database ?? throw new ArgumentException("Nom de base manquant.", nameof(adminConnectionString));
        string role = PostgreSqlNames.Quote(owner.Role);
        string target = PostgreSqlNames.Quote(database);

        builder.Database = "postgres";
        NpgsqlConnection server = new(builder.ConnectionString);
        await using (server.ConfigureAwait(false))
        {
            await server.OpenAsync(cancellationToken).ConfigureAwait(false);
            bool exists = await PostgreSqlNames.ExistsAsync(server, "SELECT 1 FROM pg_roles WHERE rolname = $1", owner.Role, cancellationToken).ConfigureAwait(false);
            string verifier = ScramVerifier(owner.Password);
            await ExecuteAsync(server, exists ? $"ALTER ROLE {role} WITH LOGIN PASSWORD '{verifier}'" : $"CREATE ROLE {role} LOGIN PASSWORD '{verifier}'", cancellationToken)
                .ConfigureAwait(false);
            await ExecuteAsync(server, $"GRANT {role} TO CURRENT_USER", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(server, $"ALTER DATABASE {target} OWNER TO {role}", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(server, $"REVOKE ALL ON DATABASE {target} FROM PUBLIC", cancellationToken).ConfigureAwait(false);
        }

        // Les objets que l'administrateur a créés dans la base (bases d'avant les rôles par cabinet) passent au rôle.
        // Une table emporte ses index et ses séquences liées ; une séquence libre change de propriétaire seule.
        builder.Database = database;
        NpgsqlConnection inside = new(builder.ConnectionString);
        await using (inside.ConfigureAwait(false))
        {
            await inside.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(inside, $"""
                DO $$
                DECLARE o record;
                BEGIN
                  FOR o IN
                    SELECT c.relname, c.relkind FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'public' AND c.relowner = (SELECT oid FROM pg_roles WHERE rolname = current_user)
                      AND (c.relkind IN ('r', 'p', 'v', 'm')
                        OR (c.relkind = 'S' AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = c.oid AND d.deptype IN ('a', 'i'))))
                  LOOP
                    EXECUTE format('ALTER %s public.%I OWNER TO %s',
                      CASE o.relkind WHEN 'S' THEN 'SEQUENCE' WHEN 'v' THEN 'VIEW' WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE 'TABLE' END,
                      o.relname, '{role}');
                  END LOOP;
                END $$
                """, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DropOwnerAsync(string adminConnectionString, string role, CancellationToken cancellationToken)
    {
        NpgsqlConnectionStringBuilder builder = new(adminConnectionString) { Pooling = false, Database = "postgres" };
        NpgsqlConnection server = new(builder.ConnectionString);
        await using (server.ConfigureAwait(false))
        {
            await server.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(server, $"DROP ROLE IF EXISTS {PostgreSqlNames.Quote(role)}", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Le vérificateur SCRAM-SHA-256 que PostgreSQL garde (RFC 5802 / 7677) : le serveur ne reçoit pas le mot de passe.
    /// Son sel se tire du mot de passe : le même mot de passe donne le même vérificateur. Chaque nouvelle base du cabinet
    /// redonne le mot de passe de son rôle ; un sel neuf changerait le vérificateur sous PgBouncer, qui garde les clés
    /// SCRAM des connexions en cours, et ses connexions suivantes au serveur seraient refusées.
    /// </summary>
    internal static string ScramVerifier(string password)
    {
        const int Iterations = 4096;
        byte[] secret = Encoding.UTF8.GetBytes(password);
        byte[] salt = HMACSHA256.HashData(secret, "lettra:sel-scram"u8)[..16];
        byte[] salted = Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, HashAlgorithmName.SHA256, 32);
        byte[] storedKey = SHA256.HashData(HMACSHA256.HashData(salted, "Client Key"u8));
        byte[] serverKey = HMACSHA256.HashData(salted, "Server Key"u8);
        return $"SCRAM-SHA-256${Iterations}:{Convert.ToBase64String(salt)}${Convert.ToBase64String(storedKey)}:{Convert.ToBase64String(serverKey)}";
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
#pragma warning disable CA2100 // Noms validés par PostgreSqlNames.Quote ; vérificateur en base64 ; aucune valeur saisie.
        NpgsqlCommand command = new(sql, connection);
#pragma warning restore CA2100
        await using (command.ConfigureAwait(false))
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>Noms de bases et de rôles : lettres, chiffres et souligné seulement, cités pour PostgreSQL.</summary>
internal static class PostgreSqlNames
{
    public static string Quote(string identifier) =>
        identifier.Length is > 0 and <= 63 && identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? $"\"{identifier}\""
            : throw new ArgumentException("Nom de base ou de rôle invalide.", nameof(identifier));

    public static async Task<bool> ExistsAsync(NpgsqlConnection connection, string sql, string value, CancellationToken cancellationToken)
    {
        NpgsqlCommand exists = new(sql, connection) { Parameters = { new() { Value = value } } };
        await using (exists.ConfigureAwait(false))
        {
            return await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
        }
    }
}
