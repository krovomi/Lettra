using System.Diagnostics;
using Lettra.Application.Abstractions.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Persistence.Tests;

/// Coût d'ouverture d'un dossier local chiffré (dérivation de clé à chaque connexion physique).
public sealed class SqliteOpenCostTests(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly TestEnvironment _env = new();
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => _env.DisposeAsync();

    [Fact]
    public async Task Ouvrir_un_dossier_local_chiffre_reste_sous_la_seconde()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = await _env.NewDossierAsync(DatabaseProvider.Sqlite);

        var cold = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            SqliteConnection.ClearAllPools();
            var sw = Stopwatch.StartNew();
            await using var db = await _env.OpenAsync(tenant);
            _ = await db.Customers.CountAsync(ct);
            cold.Add(sw.Elapsed.TotalMilliseconds);
        }

        var warm = Stopwatch.StartNew();
        for (var i = 0; i < 50; i++)
        {
            await using var db = await _env.OpenAsync(tenant);
            _ = await db.Customers.CountAsync(ct);
        }

        output.WriteLine($"Ouverture à froid (KDF + garde) : médiane {cold.Order().ElementAt(2):F1} ms ; à chaud (pool) : {warm.Elapsed.TotalMilliseconds / 50:F2} ms");
        Assert.True(cold.Order().ElementAt(2) < 1000);
    }
}
