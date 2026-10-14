using System.Collections.Concurrent;
using System.Diagnostics;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Composition;
using Lettra.Domain;
using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.Persistence.Invoicing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lettra.Worker;

internal sealed class Commands(ServiceProvider sp, IConfiguration config, Options o, CancellationToken ct)
{
    private const string Cluster = "pg-a";
    private IDbContextFactory<CatalogDbContext> Catalog => sp.GetRequiredService<IDbContextFactory<CatalogDbContext>>();

    public static int Help()
    {
        Console.WriteLine("""
            Commandes :
              init       [--orgs 50] [--max 12000]                 catalogue, cluster pg-a, cabinets
              provision  --count N [--parallel 8]                  crée N dossiers (base + migrations + identité)
              migrate    [--limit N] [--parallel 16]               vague de migration (canari avec --limit)
              downgrade  [--parallel 16]                           ramène toutes les bases à la migration 1 (POC)
              bench      [--seconds 60] [--concurrency 64] [--hot N] [--single]
              isolation  [--swaps 200]                             contrôle d'isolation + routage volontairement faux
              stats                                                tailles et compteurs
            """);
        return 1;
    }

    public async Task<int> InitAsync()
    {
        await sp.MigrateCatalogAsync(ct);
        await using var db = await Catalog.CreateDbContextAsync(ct);
        if (!await db.Clusters.AnyAsync(c => c.Id == Cluster, ct))
            db.Clusters.Add(new DbCluster { Id = Cluster, CellId = "cell-1", Provider = DatabaseProvider.PostgreSql, MaxDatabases = o.Int("max", 12000) });
        var existing = await db.Organisations.CountAsync(ct);
        for (var i = existing; i < o.Int("orgs", 50); i++)
            db.Organisations.Add(new Organisation { Id = Guid.CreateVersion7(), Name = $"Cabinet {i:D3}", CellId = "cell-1" });
        await db.SaveChangesAsync(ct);
        Console.WriteLine($"Catalogue prêt : {await db.Organisations.CountAsync(ct)} cabinets, cluster {Cluster}.");
        return 0;
    }

    public async Task<int> ProvisionAsync()
    {
        var count = o.Int("count", 100);
        List<Guid> orgs;
        await using (var db = await Catalog.CreateDbContextAsync(ct))
            orgs = await db.Organisations.OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);

        var provisioner = sp.GetRequiredService<DossierProvisioner>();
        var durations = new ConcurrentBag<double>();
        var failures = 0;
        var done = 0;
        var sw = Stopwatch.StartNew();
        await Parallel.ForEachAsync(Enumerable.Range(0, count), new ParallelOptions { MaxDegreeOfParallelism = o.Int("parallel", 8), CancellationToken = ct }, async (i, token) =>
        {
            var org = orgs[i % orgs.Count];
            var one = Stopwatch.StartNew();
            try
            {
                var d = await provisioner.ProvisionAsync(org, $"Dossier {i}", Cluster, token);
                await using var db = await Catalog.CreateDbContextAsync(token);
                db.Access.Add(new DossierAccess { UserId = $"user-{org:N}", DossierId = d.Id });
                await db.SaveChangesAsync(token);
                durations.Add(one.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (Interlocked.Increment(ref failures) <= 5)
                    Console.Error.WriteLine($"Échec : {ex.Message}");
            }
            var n = Interlocked.Increment(ref done);
            if (n % 250 == 0)
                Console.WriteLine($"  {n}/{count} en {sw.Elapsed.TotalSeconds:F0} s ({n / sw.Elapsed.TotalSeconds:F1} dossiers/s)");
        });

        var sorted = durations.Order().ToArray();
        Console.WriteLine($"Créés : {sorted.Length}, échecs : {failures}, durée : {sw.Elapsed.TotalSeconds:F1} s, débit : {sorted.Length / sw.Elapsed.TotalSeconds:F1} dossiers/s");
        Console.WriteLine($"Par dossier : p50 {P(sorted, .5):F0} ms, p95 {P(sorted, .95):F0} ms, max {sorted.LastOrDefault():F0} ms");
        return failures == 0 ? 0 : 2;
    }

    public async Task<int> MigrateAsync()
    {
        var report = await sp.GetRequiredService<DossierMigrationOrchestrator>().RunWaveAsync(new MigrationWaveOptions
        {
            Limit = o.IntOrNull("limit"),
            MaxParallelism = o.Int("parallel", 16),
        }, ct);
        Console.WriteLine($"Vague : {report.Candidates} candidates, {report.Migrated} migrées, {report.Failed} échecs, {report.SkippedLeased} sous bail, arrêt={report.Aborted}");
        Console.WriteLine($"Durée {report.Elapsed.TotalSeconds:F1} s ({report.Migrated / Math.Max(report.Elapsed.TotalSeconds, 0.001):F1} bases/s) ; par base p50 {report.P50Ms:F0} ms, p95 {report.P95Ms:F0} ms, max {report.MaxMs:F0} ms");
        foreach (var (id, err) in report.Errors.Take(5))
            Console.WriteLine($"  {id} : {err}");
        return report.Failed == 0 ? 0 : 2;
    }

    public async Task<int> DowngradeAsync()
    {
        var dossiers = await ActiveDossiersAsync();
        var factory = sp.GetRequiredService<IDossierDbContextFactory>();
        var sw = Stopwatch.StartNew();
        await Parallel.ForEachAsync(dossiers, new ParallelOptions { MaxDegreeOfParallelism = o.Int("parallel", 16), CancellationToken = ct }, async (d, token) =>
        {
            await using var ctx = await factory.CreateAsync(TenantCatalog.ToDescriptor(d), ct: token);
            await ctx.GetService<IMigrator>().MigrateAsync("20261014000001_Initial", token);
        });
        await using (var db = await Catalog.CreateDbContextAsync(ct))
            await db.Dossiers.ExecuteUpdateAsync(s => s.SetProperty(d => d.SchemaVersion, "20261014000001_Initial"), ct);
        Console.WriteLine($"{dossiers.Count} bases ramenées à la migration 1 en {sw.Elapsed.TotalSeconds:F1} s.");
        return 0;
    }

    public async Task<int> BenchAsync()
    {
        var seconds = o.Int("seconds", 60);
        var concurrency = o.Int("concurrency", 64);
        var all = await ActiveDossiersAsync();
        var hot = o.Flag("single") ? all.Take(1).ToList() : all.OrderBy(_ => Random.Shared.Next()).Take(o.Int("hot", all.Count)).ToList();

        var resolveMs = new ConcurrentBag<double>();
        var openMs = new ConcurrentBag<double>();
        var writeMs = new ConcurrentBag<double>();
        var totalMs = new ConcurrentBag<double>();
        var errors = new ConcurrentDictionary<string, int>();
        var touched = new ConcurrentDictionary<Guid, byte>();
        long ops = 0;
        var maxServerConnections = 0;

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(TimeSpan.FromSeconds(seconds));
        var sampler = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try { maxServerConnections = Math.Max(maxServerConnections, await ServerConnectionsAsync()); } catch { /* échantillon perdu */ }
                try { await Task.Delay(500, stop.Token); } catch (OperationCanceledException) { }
            }
        });

        var sw = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var d = hot[Random.Shared.Next(hot.Count)];
                var t0 = Stopwatch.GetTimestamp();
                try
                {
                    await using var scope = sp.CreateAsyncScope();
                    var resolver = ActivatorUtilities.CreateInstance<CatalogTenantResolver>(scope.ServiceProvider, new BenchRequest($"user-{d.OrganisationId:N}", d.OrganisationId, d.Id));
                    var tenant = await resolver.ResolveAsync(CancellationToken.None);
                    scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant);
                    var t1 = Stopwatch.GetTimestamp();

                    await using var db = await scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>().CreateAsync(CancellationToken.None);
                    var t2 = Stopwatch.GetTimestamp();

                    var customer = new Customer($"Client {d.Id:N}", null);
                    var invoice = new Invoice(customer.Id, d.Id.ToString("N"));
                    invoice.AddLine("Mission comptable", 1, 1200m);
                    invoice.AddLine("Débours", 3, 12.5m);
                    db.Add(customer);
                    db.Add(invoice);
                    await db.SaveChangesAsync(CancellationToken.None);
                    await InvoiceValidation.ValidateAsync(db, invoice.Id, ct: CancellationToken.None);
                    var t3 = Stopwatch.GetTimestamp();

                    resolveMs.Add(Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds);
                    openMs.Add(Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds);
                    writeMs.Add(Stopwatch.GetElapsedTime(t2, t3).TotalMilliseconds);
                    totalMs.Add(Stopwatch.GetElapsedTime(t0, t3).TotalMilliseconds);
                    touched.TryAdd(d.Id, 0);
                    Interlocked.Increment(ref ops);
                }
                catch (Exception ex)
                {
                    var key = ex.GetType().Name + ": " + (ex.InnerException?.Message ?? ex.Message);
                    errors.AddOrUpdate(key[..Math.Min(key.Length, 160)], 1, (_, n) => n + 1);
                }
            }
        })).ToArray();
        await Task.WhenAll(workers);
        var elapsed = sw.Elapsed.TotalSeconds;
        await sampler;

        Console.WriteLine($"Banc : {seconds} s, {concurrency} fils, {hot.Count} dossiers chauds, {touched.Count} dossiers touchés");
        Console.WriteLine($"Validations : {ops} ({ops / elapsed:F0}/s), erreurs : {errors.Values.Sum()}");
        Console.WriteLine($"  résolution catalogue  p50 {P(resolveMs, .5):F2} ms  p95 {P(resolveMs, .95):F2} ms  p99 {P(resolveMs, .99):F2} ms");
        Console.WriteLine($"  ouverture + garde     p50 {P(openMs, .5):F2} ms  p95 {P(openMs, .95):F2} ms  p99 {P(openMs, .99):F2} ms");
        Console.WriteLine($"  écriture + validation p50 {P(writeMs, .5):F2} ms  p95 {P(writeMs, .95):F2} ms  p99 {P(writeMs, .99):F2} ms");
        Console.WriteLine($"  total                 p50 {P(totalMs, .5):F2} ms  p95 {P(totalMs, .95):F2} ms  p99 {P(totalMs, .99):F2} ms");
        Console.WriteLine($"Connexions serveur PostgreSQL (max observé) : {maxServerConnections}");
        using var proc = Process.GetCurrentProcess();
        Console.WriteLine($"Mémoire du processus : {proc.WorkingSet64 / 1024 / 1024} Mo, tas géré {GC.GetTotalMemory(false) / 1024 / 1024} Mo");
        foreach (var (k, v) in errors.OrderByDescending(e => e.Value).Take(5))
            Console.WriteLine($"  {v} × {k}");
        return errors.IsEmpty ? 0 : 2;
    }

    public async Task<int> IsolationAsync()
    {
        var dossiers = await ActiveDossiersAsync();
        var factory = sp.GetRequiredService<IDossierDbContextFactory>();
        long invoices = 0, foreign = 0, gaps = 0;
        var sw = Stopwatch.StartNew();
        await Parallel.ForEachAsync(dossiers, new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = ct }, async (d, token) =>
        {
            await using var db = await factory.CreateAsync(TenantCatalog.ToDescriptor(d), ct: token);
            var refs = await db.Invoices.Select(i => new { i.Reference, i.Number }).ToListAsync(token);
            Interlocked.Add(ref invoices, refs.Count);
            Interlocked.Add(ref foreign, refs.Count(r => r.Reference != d.Id.ToString("N")));
            var numbers = refs.Where(r => r.Number != null).Select(r => int.Parse(r.Number![4..])).Order().ToList();
            if (numbers.Count > 0 && (numbers[0] != 1 || numbers[^1] != numbers.Count))
                Interlocked.Increment(ref gaps);
        });
        Console.WriteLine($"Contrôle : {dossiers.Count} bases, {invoices} factures, {foreign} factures d'un autre dossier, {gaps} séries avec trou ou doublon ({sw.Elapsed.TotalSeconds:F1} s)");

        // Routage volontairement faux : le dossier A pointé vers la base de B. La garde doit tout bloquer.
        var swaps = Math.Min(o.Int("swaps", 200), dossiers.Count / 2);
        var shuffled = dossiers.OrderBy(_ => Random.Shared.Next()).ToList();
        int blocked = 0, leaked = 0;
        for (var i = 0; i < swaps; i++)
        {
            var a = TenantCatalog.ToDescriptor(shuffled[2 * i]);
            var b = TenantCatalog.ToDescriptor(shuffled[2 * i + 1]);
            try
            {
                await using var db = await factory.CreateAsync(a with { ConnectionSecretRef = b.ConnectionSecretRef }, ct: ct);
                leaked++;
            }
            catch (DossierMismatchException) { blocked++; }
        }
        Console.WriteLine($"Routage faux : {swaps} essais, {blocked} bloqués, {leaked} passés");
        return foreign == 0 && gaps == 0 && leaked == 0 ? 0 : 2;
    }

    public async Task<int> StatsAsync()
    {
        await using var db = await Catalog.CreateDbContextAsync(ct);
        var byState = await db.Dossiers.GroupBy(d => new { d.State, d.SchemaVersion }).Select(g => new { g.Key.State, g.Key.SchemaVersion, N = g.Count() }).ToListAsync(ct);
        foreach (var s in byState)
            Console.WriteLine($"{s.State,-12} {s.SchemaVersion,-40} {s.N}");
        await using var c = new NpgsqlConnection(config["Lettra:Clusters:pg-a:AdminConnectionString"] + ";Database=postgres");
        await c.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT count(*), sum(pg_database_size(datname)) FROM pg_database WHERE datname LIKE 'd\\_%'", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        var n = r.GetInt64(0);
        var bytes = r.IsDBNull(1) ? 0m : r.GetDecimal(1);
        Console.WriteLine($"Bases de dossiers sur le cluster : {n}, {bytes / 1024 / 1024 / 1024:F2} Go, {(n == 0 ? 0 : bytes / n / 1024 / 1024):F2} Mo par base");
        return 0;
    }

    private async Task<List<Dossier>> ActiveDossiersAsync()
    {
        await using var db = await Catalog.CreateDbContextAsync(ct);
        return await db.Dossiers.AsNoTracking().Where(d => d.State == DossierState.Active).ToListAsync(ct);
    }

    private async Task<int> ServerConnectionsAsync()
    {
        await using var c = new NpgsqlConnection(config["Lettra:Clusters:pg-a:AdminConnectionString"] + ";Database=postgres;Pooling=false");
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE backend_type = 'client backend' AND usename = 'lettra_app'", c);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private static double P(IEnumerable<double> values, double p)
    {
        var s = values as double[] ?? values.Order().ToArray();
        return s.Length == 0 ? 0 : s[Math.Clamp((int)Math.Ceiling(p * s.Length) - 1, 0, s.Length - 1)];
    }

    private sealed class BenchRequest(string user, Guid org, Guid dossier) : ICurrentRequest
    {
        public string? UserId => user;
        public Guid? OrganisationId => org;
        public Guid? DossierId => dossier;
    }
}
