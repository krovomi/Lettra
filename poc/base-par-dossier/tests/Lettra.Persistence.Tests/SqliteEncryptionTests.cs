using System.Security.Cryptography;
using System.Text;
using Lettra.Application.Abstractions.Security;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain;
using Lettra.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// Modes 1 et 4 : fichier chiffré, clé par dossier enveloppée par la clé du cabinet (ADR-0011).
public sealed class SqliteEncryptionTests : IAsyncLifetime
{
    private readonly TestEnvironment _env = new();
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => _env.DisposeAsync();

    [Fact]
    public async Task Le_fichier_du_dossier_est_illisible_sans_sa_cle()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = await _env.NewDossierAsync(DatabaseProvider.Sqlite);
        await using (var db = await _env.OpenAsync(tenant))
        {
            db.Add(new Customer("Secret SARL", "987654321"));
            await db.SaveChangesAsync(ct);
        }
        SqliteConnection.ClearAllPools();

        var file = (await _env.LocalStore.FindAsync(tenant.DossierId, ct))!.DatabaseFile;
        var bytes = await File.ReadAllBytesAsync(file, ct);
        Assert.NotEqual("SQLite format 3\0", Encoding.ASCII.GetString(bytes, 0, 16));
        Assert.DoesNotContain("Secret SARL", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);

        Assert.ThrowsAny<SqliteException>(() => Count(new SqliteConnectionStringBuilder { DataSource = file }.ToString()));
        Assert.ThrowsAny<SqliteException>(() => Count(new SqliteConnectionStringBuilder { DataSource = file, Password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) }.ToString()));

        var cs = await _env.Services.GetRequiredService<ISecretStore>().GetConnectionStringAsync(tenant.ConnectionSecretRef, ct);
        Assert.Equal(1, Count(cs));
        Assert.Equal("chacha20", SqliteEncryption.CipherName(cs));
    }

    [Fact]
    public async Task Detruire_la_cle_efface_le_dossier()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = await _env.NewDossierAsync(DatabaseProvider.Sqlite);
        await _env.LocalStore.DeleteKeyAsync(tenant.DossierId, ct);
        SqliteConnection.ClearAllPools();

        await Assert.ThrowsAnyAsync<CryptographicException>(async () => await _env.OpenAsync(tenant));
    }

    [Fact]
    public async Task La_cle_d_un_dossier_ne_s_ouvre_pas_sous_l_identite_d_un_autre()
    {
        var ct = TestContext.Current.CancellationToken;
        var keyring = _env.Services.GetRequiredService<IDossierKeyring>();
        var org = Guid.CreateVersion7();
        var a = Guid.CreateVersion7();
        var wrapped = await keyring.CreateWrappedDekAsync(org, a, ct);

        Assert.Equal(32, (await keyring.UnwrapDekAsync(org, a, wrapped, ct)).Length);
        await Assert.ThrowsAnyAsync<CryptographicException>(async () => await keyring.UnwrapDekAsync(org, Guid.CreateVersion7(), wrapped, ct));
        await Assert.ThrowsAnyAsync<CryptographicException>(async () => await keyring.UnwrapDekAsync(Guid.CreateVersion7(), a, wrapped, ct));
    }

    private static long Count(string cs)
    {
        SqliteEncryption.EnsureInitialized();
        using var c = new SqliteConnection(cs);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM Customers";
        return (long)cmd.ExecuteScalar()!;
    }
}
