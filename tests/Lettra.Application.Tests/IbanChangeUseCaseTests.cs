using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Application.Tests;

/// <summary>
/// Changement d'IBAN d'un fournisseur, de bout en bout dans la couche application : la modification de la fiche ouvre une
/// vérification, l'alerte du dossier se lève, la décision d'un autre collaborateur la referme.
/// </summary>
public sealed class IbanChangeUseCaseTests : IAsyncLifetime
{
    private const string FirstIban = "FR7630006000011234567890189";
    private const string SecondIban = "FR1420041010050500013M02606";
    private const string ForeignIban = "DE89370400440532013000";

    private static readonly DossierId Dossier = new(Guid.CreateVersion7());
    private static readonly OrganisationId Cabinet = new(Guid.CreateVersion7());

    private readonly FakeTenantContext _tenant = new() { Descriptor = new TenantDescriptor(Dossier, Cabinet, DatabaseProvider.Sqlite, "config:test") };
    private readonly FakePermissions _permissions = new();
    private readonly FakeUser _user = new();
    private readonly InMemoryPolicies _policies = new();
    private readonly InMemoryCustomers _unitOfWork = new();
    private readonly InMemoryPartiesAndCatalog _store = new();
    private readonly InMemoryIntegrity _integrity = new(Dossier);
    private readonly InMemoryIbanChanges _changes;
    private ServiceProvider _root = null!;
    private AsyncServiceScope _scope;

    public IbanChangeUseCaseTests() => _changes = new InMemoryIbanChanges(_store);

    public Task InitializeAsync()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IOrganisationPolicyStore>(_policies);
        services.AddApplication();
        services.AddSingleton<ITenantContext>(_tenant);
        services.AddSingleton<ICurrentUser>(_user);
        services.AddSingleton<IPermissionService>(_permissions);
        services.AddSingleton<ICustomerRepository>(_unitOfWork);
        services.AddSingleton<ICustomerReadStore>(_unitOfWork);
        services.AddSingleton<IUnitOfWork>(_unitOfWork);
        services.AddPartiesAndCatalogFakes(_store, new FakeDirectories(), ibans: _changes);
        services.AddIntegrityFakes(_integrity);
        services.AddSingleton<IDossierProvisioner>(new RecordingDossierProvisioner());
        services.AddSingleton<IAuthorizationDirectory>(new EmptyAuthorizationDirectory());
        services.AddSingleton<IDossierDirectory>(new EmptyDossierDirectory());
        services.AddCabinetFakes();
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

    private IQueryDispatcher Queries => _scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();

    private static Supplier.Draft Draft(string? iban) =>
        new("Minoterie Dupuis", null, "73282932000074", null, null, iban, "2 chemin du Moulin", null, "45000", "Orléans", "FR");

    private async Task<SupplierId> CreateSupplierAsync(string? iban = FirstIban) =>
        (await Commands.SendAsync(new CreateSupplierCommand(Dossier, Draft(iban)), default)).Value;

    private Task<Result> ChangeIbanAsync(SupplierId id, string? iban) =>
        Commands.SendAsync(new UpdateSupplierCommand(Dossier, id, Draft(iban), null), default);

    private IEnumerable<DossierAlertSignal> IbanSignals => _integrity.Signals.Where(s => s.Rule == DossierAlertRule.SupplierIbanToVerify);

    [Fact]
    public async Task Un_nouvel_iban_ouvre_une_verification_masquee_et_leve_l_alerte_du_dossier()
    {
        SupplierId id = await CreateSupplierAsync();
        _changes.Changes.Should().BeEmpty("la création d'une fiche n'est pas un changement d'IBAN");

        (await ChangeIbanAsync(id, SecondIban)).IsSuccess.Should().BeTrue();

        IbanChangeResponse pending = (await Queries.QueryAsync(new ListIbanChangesQuery(Dossier, IbanChangeStatusDto.Pending, null, 1, 50), default))
            .Value.Items.Should().ContainSingle().Subject;
        pending.SupplierName.Should().Be("Minoterie Dupuis");
        pending.PreviousIban.Should().Be("FR76 •••• 0189");
        pending.NewIban.Should().Be("FR14 •••• 2606");
        pending.CountryChanged.Should().BeFalse();
        IbanSignals.Should().ContainSingle().Which.Firing.Should().BeTrue();
    }

    [Fact]
    public async Task Modifier_la_fiche_sans_toucher_a_l_iban_ne_declenche_rien()
    {
        SupplierId id = await CreateSupplierAsync();

        (await ChangeIbanAsync(id, FirstIban)).IsSuccess.Should().BeTrue();

        _changes.Changes.Should().BeEmpty();
        IbanSignals.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_second_changement_remplace_le_premier_et_signale_le_changement_de_pays()
    {
        SupplierId id = await CreateSupplierAsync();
        await ChangeIbanAsync(id, SecondIban);

        await ChangeIbanAsync(id, ForeignIban);

        IReadOnlyList<IbanChangeResponse> history = (await Queries.QueryAsync(new ListSupplierIbanChangesQuery(Dossier, id), default)).Value;
        history.Select(c => c.Status).Should().BeEquivalentTo([IbanChangeStatusDto.Pending, IbanChangeStatusDto.Superseded]);
        history.Single(c => c.Status == IbanChangeStatusDto.Pending).CountryChanged.Should().BeTrue();
    }

    [Fact]
    public async Task Confirmer_le_changement_referme_l_alerte()
    {
        SupplierId id = await CreateSupplierAsync();
        await ChangeIbanAsync(id, SecondIban);
        IbanChange change = _changes.Changes.Values.Single();

        Result confirmed = await Commands.SendAsync(
            new ConfirmIbanChangeCommand(Dossier, change.Id, IbanVerificationMethodDto.KnownPhoneNumber, "Rappel au 02 38 00 00 00", change.Version), default);

        confirmed.IsSuccess.Should().BeTrue();
        change.Status.Should().Be(IbanChangeStatus.Confirmed);
        change.Method.Should().Be(IbanVerificationMethod.KnownPhoneNumber);
        IbanSignals.Last().Firing.Should().BeFalse();
    }

    [Fact]
    public async Task Refuser_exige_une_note_puis_referme_l_alerte()
    {
        SupplierId id = await CreateSupplierAsync();
        await ChangeIbanAsync(id, SecondIban);
        IbanChangeId changeId = _changes.Changes.Keys.Single();

        (await Commands.SendAsync(new RejectIbanChangeCommand(Dossier, changeId, "  ", null), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(IbanChangeErrors.NoteRequiredCode);

        (await Commands.SendAsync(new RejectIbanChangeCommand(Dossier, changeId, "Le fournisseur n'a rien demandé.", null), default))
            .IsSuccess.Should().BeTrue();
        _changes.Changes[changeId].Status.Should().Be(IbanChangeStatus.Rejected);
        IbanSignals.Last().Firing.Should().BeFalse();
    }

    [Fact]
    public async Task Une_decision_deja_prise_ou_inconnue_est_refusee()
    {
        SupplierId id = await CreateSupplierAsync();
        await ChangeIbanAsync(id, SecondIban);
        IbanChangeId changeId = _changes.Changes.Keys.Single();
        await Commands.SendAsync(new ConfirmIbanChangeCommand(Dossier, changeId, IbanVerificationMethodDto.InPerson, null, null), default);

        (await Commands.SendAsync(new RejectIbanChangeCommand(Dossier, changeId, "Trop tard", null), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(IbanChangeErrors.AlreadyDecidedCode);
        (await Commands.SendAsync(new ConfirmIbanChangeCommand(Dossier, new IbanChangeId(Guid.CreateVersion7()), IbanVerificationMethodDto.InPerson, null, null), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(IbanChangeErrors.NotFoundCode);
    }

    [Fact]
    public async Task Avec_la_separation_des_taches_l_auteur_du_changement_ne_le_confirme_pas()
    {
        _policies.Policies[Cabinet] = new SegregationOfDutiesPolicy(true, null);
        SupplierId id = await CreateSupplierAsync();
        await ChangeIbanAsync(id, SecondIban);
        IbanChangeId changeId = _changes.Changes.Keys.Single();

        (await Commands.SendAsync(new ConfirmIbanChangeCommand(Dossier, changeId, IbanVerificationMethodDto.KnownPhoneNumber, null, null), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(IbanChangeErrors.SameUserCode);

        _user.UserId = Guid.CreateVersion7();
        (await Commands.SendAsync(new ConfirmIbanChangeCommand(Dossier, changeId, IbanVerificationMethodDto.KnownPhoneNumber, null, null), default))
            .IsSuccess.Should().BeTrue("un autre collaborateur vérifie");
    }

    [Fact]
    public async Task Consulter_et_decider_exigent_leurs_droits()
    {
        SupplierId id = await CreateSupplierAsync();
        await ChangeIbanAsync(id, SecondIban);
        IbanChangeId changeId = _changes.Changes.Keys.Single();

        _permissions.Granted.Remove(Permission.TiersModifier);
        (await Commands.SendAsync(new RejectIbanChangeCommand(Dossier, changeId, "Non", null), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);

        _permissions.Granted.Remove(Permission.TiersConsulter);
        (await Queries.QueryAsync(new ListIbanChangesQuery(Dossier, null, null, 1, 50), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
    }

    [Fact]
    public async Task La_liste_valide_sa_pagination_et_transmet_ses_criteres()
    {
        (await Queries.QueryAsync(new ListIbanChangesQuery(Dossier, null, null, 0, 50), default)).IsFailure.Should().BeTrue();
        (await Queries.QueryAsync(new ListIbanChangesQuery(Dossier, (IbanChangeStatusDto)42, null, 1, 50), default)).IsFailure.Should().BeTrue();
        (await Queries.QueryAsync(new ListIbanChangesQuery(Dossier, null, new string('x', 101), 1, 50), default)).IsFailure.Should().BeTrue();

        (await Queries.QueryAsync(
            new ListIbanChangesQuery(Dossier, IbanChangeStatusDto.Confirmed, " Dupuis ", 2, 20, IbanChangeSortDto.Supplier, SortDirectionDto.Ascending), default))
            .IsSuccess.Should().BeTrue();
        _changes.LastFilter.Should().Be(new IbanChangeListFilter(IbanChangeStatusDto.Confirmed, "Dupuis", 2, 20, IbanChangeSortDto.Supplier, SortDirectionDto.Ascending));
    }
}
