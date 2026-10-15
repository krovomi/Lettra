using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Integrity;
using Lettra.Contracts.Integrity;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lettra.Application.Tests;

public sealed class IntegrityUseCaseTests : IAsyncLifetime
{
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());

    private readonly FakeTenantContext _tenant = new()
    {
        Descriptor = new TenantDescriptor(Dossier, new OrganisationId(Guid.CreateVersion7()), DatabaseProvider.Sqlite, "config:test"),
    };

    private readonly FakePermissions _permissions = new();
    private readonly InMemoryIntegrity _integrity = new(Dossier);
    private readonly CapturingLoggerProvider _logs = new();
    private ServiceProvider _root = null!;
    private AsyncServiceScope _scope;

    public Task InitializeAsync()
    {
        ServiceCollection services = new();
        services.AddLogging(b => b.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
        services.AddApplication();
        services.AddSingleton<ITenantContext>(_tenant);
        services.AddSingleton<ICurrentUser>(new FakeUser());
        services.AddSingleton<IPermissionService>(_permissions);
        InMemoryCustomers customers = new();
        services.AddSingleton<ICustomerRepository>(customers);
        services.AddSingleton<ICustomerReadStore>(customers);
        services.AddSingleton<IUnitOfWork>(customers);
        services.AddPartiesAndCatalogFakes(new InMemoryPartiesAndCatalog(), new FakeDirectories());
        services.AddIntegrityFakes(_integrity);
        services.AddSingleton<IDossierProvisioner>(new RecordingDossierProvisioner());
        _root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _root.CreateAsyncScope();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _root.DisposeAsync();
    }

    private ICommandDispatcher Commands => _scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

    private async Task JournalAsync(int count)
    {
        IIntegrityJournal journal = _scope.ServiceProvider.GetRequiredService<IIntegrityJournal>();
        for (int i = 1; i <= count; i++)
        {
            string number = $"FAC-{i:D6}";
            (await journal.AppendAsync(
                new DocumentLifecycleEvent(
                    JournalEntryKind.InvoiceValidated, JournaledDocumentKind.Invoice, Guid.CreateVersion7(), "FAC", number,
                    new DateOnly(2026, 10, 1), Guid.CreateVersion7(), 100m, 20m, 120m, "EUR",
                    DocumentFingerprint.Compute("invoice-v1", number, "Boulangerie Martin")),
                default)).IsSuccess.Should().BeTrue();
        }

        _integrity.Series["FAC"] = count + 1;
    }

    [Fact]
    public async Task Une_chaine_intacte_leve_les_deux_alertes()
    {
        await JournalAsync(3);

        Result<IntegrityReportResponse> report = await Commands.SendAsync(new VerifyDossierIntegrityCommand(Dossier), default);

        report.Value.Intact.Should().BeTrue();
        report.Value.EntriesChecked.Should().Be(3);
        _integrity.Signals.Should().HaveCount(2).And.OnlyContain(s => !s.Firing && s.Dossier == Dossier);
    }

    [Fact]
    public async Task Une_entree_retouchee_declenche_l_alerte_de_chaine_sans_donnee_metier()
    {
        await JournalAsync(3);
        JournalEntry original = _integrity.Entries[1];
        _integrity.Entries[1] = JournalEntry.Restore(
            original.Sequence, original.OccurredAt, original.Kind, original.DocumentKind, original.DocumentId, original.SeriesCode,
            original.Number, original.IssueDate, original.CustomerId, (10m, 2m, 12m), original.Currency, original.ContentHash,
            original.RelatedDocumentId, (original.PreviousHash, original.Hash, original.Seal));

        Result<IntegrityReportResponse> report = await Commands.SendAsync(new VerifyDossierIntegrityCommand(Dossier), default);

        report.Value.Intact.Should().BeFalse();
        report.Value.Findings.Should().ContainSingle().Which.Should().Be(new IntegrityFindingResponse("EntryAltered", 2, null, null));
        _integrity.Signals.Should().ContainSingle(s => s.Rule == DossierAlertRule.IntegrityChainCompromised && s.Firing);
        _integrity.Signals.Should().ContainSingle(s => s.Rule == DossierAlertRule.NumberingGap && !s.Firing);
        foreach (string entry in _logs.Entries)
        {
            entry.Should().NotContainAny("FAC-000002", "Boulangerie", "120", "12.0000");
        }
    }

    [Fact]
    public async Task Un_numero_consomme_sans_entree_au_journal_est_un_trou()
    {
        await JournalAsync(2);
        _integrity.Series["FAC"] = 4;

        Result<IntegrityReportResponse> report = await Commands.SendAsync(new VerifyDossierIntegrityCommand(Dossier), default);

        report.Value.Findings.Should().ContainSingle().Which.Should().Be(new IntegrityFindingResponse("NumberingGap", null, null, "FAC"));
        _integrity.Signals.Should().ContainSingle(s => s.Rule == DossierAlertRule.NumberingGap && s.Firing);
        _integrity.Signals.Should().ContainSingle(s => s.Rule == DossierAlertRule.IntegrityChainCompromised && !s.Firing);
    }

    [Fact]
    public async Task Controler_exige_sa_permission()
    {
        _permissions.Granted.Remove(Permission.IntegriteVerifier);

        (await Commands.SendAsync(new VerifyDossierIntegrityCommand(Dossier), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
        _integrity.Signals.Should().BeEmpty();
    }

    [Fact]
    public async Task Cloturer_et_exporter_exigent_leurs_permissions()
    {
        _permissions.Granted.Remove(Permission.ClotureEffectuer);
        _permissions.Granted.Remove(Permission.ArchivesExporter);
        IQueryDispatcher queries = _scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();

        (await Commands.SendAsync(new CloseDuePeriodsCommand(Dossier), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
        (await queries.QueryAsync(new GetSealedArchiveQuery(Dossier, 1), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
        (await queries.QueryAsync(new ListClosingsQuery(Dossier), default)).IsSuccess.Should().BeTrue();
    }
}
