using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Integrity;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Integrity;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>Horloge réglable : les clôtures portent sur des mois écoulés.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    /// <summary>Comme un vrai <see cref="TimeProvider"/> : toujours en UTC (PostgreSQL refuse un autre décalage).</summary>
    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
}

/// <summary>Chaîne anti-fraude : scénarios joués à l'identique sur chaque provider.</summary>
internal static class IntegrityScenarios
{
    public static DocumentLifecycleEvent Invoice(string number, decimal ttc, JournalEntryKind kind = JournalEntryKind.InvoiceValidated, Guid? id = null) => new(
        kind,
        kind is JournalEntryKind.CreditNoteValidated or JournalEntryKind.CreditNoteIssued ? JournaledDocumentKind.CreditNote : JournaledDocumentKind.Invoice,
        id ?? Guid.CreateVersion7(),
        NumberSeriesCodes.Invoices,
        number,
        new DateOnly(2026, 8, 10),
        Guid.CreateVersion7(),
        ttc / 1.2m,
        ttc - (ttc / 1.2m),
        ttc,
        "EUR",
        DocumentFingerprint.Compute("invoice-v1", number, IntegrityHash.Field(ttc)));

    /// <summary>Validation telle que le module des pièces la fait : numéro réservé puis entrée au journal, dans une transaction.</summary>
    public static async Task<long> ValidateAsync(IServiceProvider services, decimal ttc, JournalEntryKind kind = JournalEntryKind.InvoiceValidated)
    {
        IIntegrityJournal journal = services.GetRequiredService<IIntegrityJournal>();
        long sequence = 0;
        Result<string> validated = await services.GetRequiredService<IDocumentNumbering>().ExecuteWithNextNumberAsync(
            NumberSeriesCodes.Invoices,
            async (number, token) => (await journal.AppendAsync(Invoice(number, ttc, kind), token)).Map(s =>
            {
                sequence = s;
                return number;
            }),
            default);
        validated.IsSuccess.Should().BeTrue();
        return sequence;
    }

    /// <summary>
    /// La transaction de validation du module des pièces, telle que le contrat la décrit : le numéro est réservé, puis
    /// l'entrée est ajoutée au journal dans la même transaction. Jouée en parallèle : la chaîne reste continue, la
    /// numérotation aussi, et le contrôle ne trouve rien.
    /// </summary>
    public static async Task ConcurrentValidationsAsync(TenantHarness harness, DatabaseProvider provider, string connectionString, int parallel = 12)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        await using (AsyncServiceScope warmUp = await harness.OpenAsync(dossier))
        {
            await warmUp.ServiceProvider.GetRequiredService<IIntegrityStore>().ListClosingsAsync(default);
        }

        await Task.WhenAll(Enumerable.Range(0, parallel).Select(async i =>
        {
            await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
            IIntegrityJournal journal = scope.ServiceProvider.GetRequiredService<IIntegrityJournal>();
            Result<string> validated = await scope.ServiceProvider.GetRequiredService<IDocumentNumbering>().ExecuteWithNextNumberAsync(
                NumberSeriesCodes.Invoices,
                async (number, token) => (await journal.AppendAsync(Invoice(number, 120m + i), token)).Map(_ => number),
                default);
            validated.IsSuccess.Should().BeTrue();
        }));

        await using AsyncServiceScope check = await harness.OpenAsync(dossier);
        Result<IntegrityReport> report = await check.ServiceProvider.GetRequiredService<DossierIntegrityService>().VerifyAsync(default);
        report.IsSuccess.Should().BeTrue();
        report.Value.EntriesChecked.Should().Be(parallel);
        report.Value.Findings.Should().BeEmpty();
        harness.Alerts.Signals.Where(s => s.Dossier == dossier).Should().OnlyContain(s => !s.Firing);
    }

    /// <summary>
    /// Sortie J2 : une ligne du journal modifiée directement en base (déclencheurs d'ajout seul retirés, comme le
    /// ferait un administrateur de base) est détectée, et l'alerte S1 « chaîne compromise » part vers la Supervision.
    /// </summary>
    public static async Task TamperDetectedAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            (await ValidateAsync(scope.ServiceProvider, 120m)).Should().Be(1);
            (await ValidateAsync(scope.ServiceProvider, 240m)).Should().Be(2);
            (await ValidateAsync(scope.ServiceProvider, 360m)).Should().Be(3);
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            LettraDbContext db = await scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>().CreateAsync(default);
            await using (db)
            {
                // Sans retirer les déclencheurs, la base refuse la modification.
                Func<Task> update = () => db.Database.ExecuteSqlRawAsync(TamperSql(provider));
                await update.Should().ThrowAsync<Exception>();

                foreach (string sql in DisableAppendOnly(provider))
                {
                    await db.Database.ExecuteSqlRawAsync(sql);
                }

                (await db.Database.ExecuteSqlRawAsync(TamperSql(provider))).Should().Be(1);
            }
        }

        await using AsyncServiceScope check = await harness.OpenAsync(dossier);
        Result<IntegrityReport> report = await check.ServiceProvider.GetRequiredService<DossierIntegrityService>().VerifyAsync(default);

        report.Value.ChainCompromised.Should().BeTrue();
        report.Value.Findings.Should().ContainSingle().Which.Should().Be(new IntegrityFinding(IntegrityFindingKind.EntryAltered, 2));
        harness.Alerts.Signals.Should().ContainSingle(s => s.Dossier == dossier && s.Rule == DossierAlertRule.IntegrityChainCompromised && s.Firing);
    }

    /// <summary>
    /// Clôtures mensuelles signées, total perpétuel, archive scellée vérifiable avec la seule clé publique ; une
    /// archive retouchée ne se vérifie plus. Le contrôle couvre ensuite la chaîne des clôtures.
    /// </summary>
    public static async Task ClosingsAndSealedArchiveAsync(TenantHarness harness, ManualClock clock, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        clock.Now = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.FromHours(2));
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            await ValidateAsync(scope.ServiceProvider, 120m);
            await ValidateAsync(scope.ServiceProvider, 240m);
            await ValidateAsync(scope.ServiceProvider, 60m, JournalEntryKind.CreditNoteValidated);
        }

        clock.Now = new DateTimeOffset(2026, 9, 30, 23, 30, 0, TimeSpan.FromHours(2));
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            await ValidateAsync(scope.ServiceProvider, 600m);
        }

        clock.Now = new DateTimeOffset(2026, 10, 14, 3, 0, 0, TimeSpan.FromHours(2));
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            Result<IReadOnlyList<PeriodClosing>> closed = await scope.ServiceProvider.GetRequiredService<DossierIntegrityService>().CloseDuePeriodsAsync(default);
            closed.Value.Should().HaveCount(2);
            PeriodClosing august = closed.Value[0];
            august.PeriodStart.Should().Be(new DateOnly(2026, 8, 1));
            august.PeriodEnd.Should().Be(new DateOnly(2026, 8, 31));
            (august.FirstSequence, august.LastSequence).Should().Be((1L, 3L));
            august.TotalInclTax.Should().Be(300m);
            PeriodClosing september = closed.Value[1];
            (september.FirstSequence, september.LastSequence).Should().Be((4L, 4L));
            september.CumulativeInclTax.Should().Be(900m);

            // Rejouer la clôture ne crée rien de plus.
            (await scope.ServiceProvider.GetRequiredService<DossierIntegrityService>().CloseDuePeriodsAsync(default)).Value.Should().BeEmpty();
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            Result<IntegrityReport> report = await scope.ServiceProvider.GetRequiredService<DossierIntegrityService>().VerifyAsync(default);
            report.Value.Findings.Should().BeEmpty();
            report.Value.ClosingsChecked.Should().Be(2);
            report.Value.EntriesChecked.Should().Be(6);

            Result<IIntegrityKeys> keys = await scope.ServiceProvider.GetRequiredService<IIntegrityKeyring>()
                .GetAsync(harness.Descriptor(dossier).OrganisationId, default);
            Result<SealedArchive> archive = await scope.ServiceProvider.GetRequiredService<ISealedArchiveBuilder>().BuildAsync(1, default);
            SealedArchiveFormat.Verify(archive.Value.Content, keys.Value.KeyId, out string? failure).Should().BeTrue(failure);
            SealedArchiveFormat.Verify(archive.Value.Content, "p256-0000000000000000", out _).Should().BeFalse("une autre clé n'est pas celle du cabinet");

            // L'archive est régénérée avec le même contenu ; seule la signature ECDSA, aléatoire, change.
            Result<SealedArchive> again = await scope.ServiceProvider.GetRequiredService<ISealedArchiveBuilder>().BuildAsync(1, default);
            ReadEntry(again.Value.Content, SealedArchiveFormat.ManifestFile).Should().Be(ReadEntry(archive.Value.Content, SealedArchiveFormat.ManifestFile));
            SealedArchiveFormat.Verify(again.Value.Content, keys.Value.KeyId, out failure).Should().BeTrue(failure);
            (await scope.ServiceProvider.GetRequiredService<ISealedArchiveBuilder>().BuildAsync(9, default)).Errors
                .Should().ContainSingle().Which.Code.Should().Be(IntegrityErrors.ClosingNotFoundCode);
        }
    }

    private static string ReadEntry(byte[] archive, string name)
    {
        using System.IO.Compression.ZipArchive zip = new(new MemoryStream(archive), System.IO.Compression.ZipArchiveMode.Read);
        using StreamReader reader = new(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private static string TamperSql(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.Sqlite => "UPDATE \"IntegrityJournal\" SET \"TotalInclTax\" = '24.0000' WHERE \"Sequence\" = 2",
        DatabaseProvider.PostgreSql => "UPDATE \"IntegrityJournal\" SET \"TotalInclTax\" = 24 WHERE \"Sequence\" = 2",
        _ => "UPDATE [IntegrityJournal] SET [TotalInclTax] = 24 WHERE [Sequence] = 2",
    };

    private static string[] DisableAppendOnly(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.Sqlite => ["DROP TRIGGER \"IntegrityJournal_NoUpdate\""],
        DatabaseProvider.PostgreSql => ["ALTER TABLE \"IntegrityJournal\" DISABLE TRIGGER USER"],
        _ => ["DISABLE TRIGGER [IntegrityJournal_AppendOnly] ON [IntegrityJournal]"],
    };
}
