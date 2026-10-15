using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Journal IA dans la base du dossier. Le schéma est appliqué sur PostgreSQL et SQL Server par les tests de migration
/// du catalogue ; le comportement est le même sur les trois providers (un seul adapter EF).
/// </summary>
public sealed class AiJournalTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-journal-ia-").FullName;

    private string Database(string name) => $"Data Source={Path.Combine(_folder, name)}.db;Pooling=False";

    [Fact]
    public async Task Le_journal_IA_est_ecrit_dans_la_base_du_dossier_et_relu_du_plus_recent_au_plus_ancien()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        DossierId dossier = harness.AddDossier(DatabaseProvider.Sqlite, Database("a"));
        Guid user = Guid.CreateVersion7();
        DateTimeOffset at = new(2026, 10, 14, 9, 0, 0, TimeSpan.FromHours(2));

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IAiAuditLog journal = scope.ServiceProvider.GetRequiredService<IAiAuditLog>();
            await journal.AppendAsync(new AiAuditEntry(at, dossier, user, AiAuditKind.Request, "Question sur [CLIENT_1]"), default);
            await journal.AppendAsync(new AiAuditEntry(at.AddSeconds(1), dossier, user, AiAuditKind.EngineCall, "1 200 jetons", "claude-eu"), default);
            await journal.AppendAsync(new AiAuditEntry(at.AddSeconds(2), dossier, user, AiAuditKind.Answer, "Réponse"), default);
        }

        // Nouvelle requête, nouveau scope : le journal survit à la requête (il n'est plus en mémoire).
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IReadOnlyList<AiAuditEntry> read = await scope.ServiceProvider.GetRequiredService<IAiAuditLog>().ReadAsync(dossier, 2, default);

            read.Should().HaveCount(2);
            read[0].Should().Be(new AiAuditEntry(at.AddSeconds(2), dossier, user, AiAuditKind.Answer, "Réponse"));
            read[1].Should().Be(new AiAuditEntry(at.AddSeconds(1), dossier, user, AiAuditKind.EngineCall, "1 200 jetons", "claude-eu"));
        }
    }

    [Fact]
    public async Task Chaque_dossier_a_son_propre_journal_et_on_ne_peut_pas_ecrire_dans_celui_d_un_autre()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        DossierId a = harness.AddDossier(DatabaseProvider.Sqlite, Database("a"));
        DossierId b = harness.AddDossier(DatabaseProvider.Sqlite, Database("b"));
        DateTimeOffset at = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

        await using (AsyncServiceScope scope = await harness.OpenAsync(a))
        {
            IAiAuditLog journal = scope.ServiceProvider.GetRequiredService<IAiAuditLog>();
            await journal.AppendAsync(new AiAuditEntry(at, a, Guid.CreateVersion7(), AiAuditKind.Request, "Question"), default);

            Func<Task> otherDossier = async () =>
                await journal.AppendAsync(new AiAuditEntry(at, b, Guid.CreateVersion7(), AiAuditKind.Request, "Question"), default);
            await otherDossier.Should().ThrowAsync<InvalidOperationException>();
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(b))
        {
            (await scope.ServiceProvider.GetRequiredService<IAiAuditLog>().ReadAsync(b, 100, default)).Should().BeEmpty();
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }
}
