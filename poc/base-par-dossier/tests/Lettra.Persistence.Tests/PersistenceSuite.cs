using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain;
using Lettra.Infrastructure.Persistence.Invoicing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lettra.Persistence.Tests;

/// La même suite de persistance sur SQLite chiffré, PostgreSQL et SQL Server (annexe C, phase 2).
public sealed class PersistenceSuite : IAsyncLifetime
{
    private readonly TestEnvironment _env = new();
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => _env.DisposeAsync();

    public static TheoryData<DatabaseProvider> Providers => [DatabaseProvider.Sqlite, DatabaseProvider.PostgreSql, DatabaseProvider.SqlServer];

    [Theory, MemberData(nameof(Providers))]
    public async Task Une_nouvelle_base_est_migree_et_estampillee_au_nom_du_dossier(DatabaseProvider provider)
    {
        var tenant = await _env.NewDossierAsync(provider);

        await using var db = await _env.OpenAsync(tenant);
        var applied = await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["20261014000001_Initial", "20261014000002_AddInvoiceDueDate"], applied);
        var identity = await db.DossierIdentity.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(tenant.DossierId, identity.DossierId);
        Assert.Equal(tenant.OrganisationId, identity.OrganisationId);
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task Les_types_neutres_font_l_aller_retour_sans_perte(DatabaseProvider provider)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = await _env.NewDossierAsync(provider);
        var customer = new Customer("Boulangerie Martin", "123456789");
        var invoice = new Invoice(customer.Id, "brouillon");
        invoice.AddLine("Pain", 3m, 33.3333m);
        invoice.AddLine("Remise", 1m, -0.0001m);
        invoice.SetDueDate(new DateOnly(2026, 11, 30));

        await using (var db = await _env.OpenAsync(tenant))
        {
            db.Add(customer);
            db.Add(invoice);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = await _env.OpenAsync(tenant))
        {
            var read = await db.Invoices.Include(i => i.Lines).SingleAsync(ct);
            Assert.Equal(99.9998m, read.TotalExclTax);
            Assert.Equal([99.9999m, -0.0001m], read.Lines.OrderBy(l => l.Position).Select(l => l.Amount));
            Assert.Equal(new DateOnly(2026, 11, 30), read.DueDate);
            Assert.Equal(invoice.IssueDate.ToUnixTimeMilliseconds(), read.IssueDate.ToUnixTimeMilliseconds());
            Assert.Equal(TimeSpan.Zero, read.IssueDate.Offset);
            Assert.Equal(customer.Id, read.CustomerId);
        }
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task La_numerotation_reste_sans_trou_ni_doublon_sous_concurrence(DatabaseProvider provider)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = await _env.NewDossierAsync(provider);
        const int count = 120;
        var ids = new List<Guid>();
        await using (var db = await _env.OpenAsync(tenant))
        {
            var c = new Customer("Client", null);
            db.Add(c);
            for (var i = 0; i < count; i++)
            {
                var inv = new Invoice(c.Id, $"r{i}");
                inv.AddLine("Prestation", 1, 100);
                db.Add(inv);
                ids.Add(inv.Id);
            }
            await db.SaveChangesAsync(ct);
        }

        await Parallel.ForEachAsync(ids, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (id, token) =>
        {
            await using var db = await _env.OpenAsync(tenant);
            await InvoiceValidation.ValidateAsync(db, id, ct: token);
        });

        await using (var db = await _env.OpenAsync(tenant))
        {
            var numbers = await db.Invoices.Select(i => i.Number!).ToListAsync(ct);
            Assert.Equal(Enumerable.Range(1, count).Select(n => $"FAC-{n:D6}"), numbers.Order());
            Assert.Equal(count + 1, (await db.NumberSeries.SingleAsync(ct)).Next);
        }
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task Une_base_d_un_autre_dossier_est_refusee_avant_toute_requete(DatabaseProvider provider)
    {
        var ct = TestContext.Current.CancellationToken;
        var a = await _env.NewDossierAsync(provider);
        var b = await _env.NewDossierAsync(provider);

        // Erreur de routage simulée : le catalogue envoie le dossier A vers la base de B.
        if (provider == DatabaseProvider.Sqlite)
        {
            // En local, la clé de A n'ouvre pas le fichier de B : le routage échoue dès le déchiffrement.
            var ma = (await _env.LocalStore.FindAsync(a.DossierId, ct))!;
            var mb = (await _env.LocalStore.FindAsync(b.DossierId, ct))!;
            await _env.LocalStore.SaveAsync(ma with { DatabaseFile = mb.DatabaseFile }, ct);
            var sqlite = await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async () => await _env.OpenAsync(a));
            Assert.Equal(26, sqlite.SqliteErrorCode);   // SQLITE_NOTADB : mauvaise clé
            return;
        }

        var misrouted = a with { ConnectionSecretRef = b.ConnectionSecretRef };
        var ex = await Assert.ThrowsAsync<DossierMismatchException>(async () => await _env.OpenAsync(misrouted));
        Assert.Equal(a.DossierId, ex.Expected);
        Assert.Equal(b.DossierId, ex.Actual);

        // Le migrateur refuse lui aussi de toucher une base qui n'est pas celle du dossier.
        await Assert.ThrowsAsync<DossierMismatchException>(() => _env.Migrator.MigrateAsync(misrouted, stampIdentityIfMissing: true, ct));
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task Une_base_ancienne_monte_de_version_sans_perdre_ses_donnees(DatabaseProvider provider)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = await _env.NewEmptyDossierAsync(provider);

        // Base créée avec la seule migration 1, comme un dossier existant avant la mise à jour.
        await using (var db = await _env.Factory.CreateAsync(tenant, verifyIdentity: false, ct))
        {
            await db.GetService<IMigrator>().MigrateAsync("20261014000001_Initial", ct);
            await db.Database.ExecuteSqlRawAsync(provider switch
            {
                DatabaseProvider.PostgreSql => $"INSERT INTO \"DossierIdentity\" (\"Id\",\"DossierId\",\"OrganisationId\",\"CreatedAt\") VALUES (1,'{tenant.DossierId}','{tenant.OrganisationId}',now())",
                DatabaseProvider.SqlServer => $"INSERT INTO DossierIdentity (Id,DossierId,OrganisationId,CreatedAt) VALUES (1,'{tenant.DossierId}','{tenant.OrganisationId}',SYSDATETIMEOFFSET())",
                _ => $"INSERT INTO DossierIdentity (Id,DossierId,OrganisationId,CreatedAt) VALUES (1,'{tenant.DossierId.ToString().ToUpperInvariant()}','{tenant.OrganisationId.ToString().ToUpperInvariant()}','2026-10-14 00:00:00+00:00')",
            }, ct);
            await db.Database.ExecuteSqlRawAsync(provider == DatabaseProvider.PostgreSql
                ? "INSERT INTO \"Customers\" (\"Id\",\"Name\",\"CreatedAt\") VALUES ('0199e3a0-0000-7000-8000-000000000001','Ancien client',now())"
                : provider == DatabaseProvider.SqlServer
                    ? "INSERT INTO Customers (Id,Name,CreatedAt) VALUES ('0199e3a0-0000-7000-8000-000000000001','Ancien client',SYSDATETIMEOFFSET())"
                    : "INSERT INTO Customers (Id,Name,CreatedAt) VALUES ('0199E3A0-0000-7000-8000-000000000001','Ancien client','2026-10-14 00:00:00+00:00')", ct);
        }

        var result = await _env.Migrator.MigrateAsync(tenant, stampIdentityIfMissing: false, ct);
        Assert.Equal(["20261014000002_AddInvoiceDueDate"], result.Applied);

        await using var after = await _env.OpenAsync(tenant);
        Assert.Equal("Ancien client", (await after.Customers.SingleAsync(ct)).Name);
    }
}
