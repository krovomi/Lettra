using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Application.Tests;

/// <summary>
/// Séparation des tâches branchée sur la facturation (annexe F §3.2) : qui saisit ne valide ni n'émet, et un avoir
/// au-delà du seuil du cabinet attend un manager.
/// </summary>
public sealed class BillingSegregationOfDutiesTests : IAsyncLifetime
{
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());
    private static readonly OrganisationId Organisation = new(Guid.CreateVersion7());

    private static readonly Customer.Draft Martin = new(
        CustomerKind.Company, "Boulangerie Martin", "732829320", null, "FR44732829320", null, "1 rue de la Paix", null, "75002", "Paris", "FR");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Lettra", "SAS", 10_000m, "552100554", null, "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", false, false, 30, null, 10m, "Pas d'escompte", null, null);

    private readonly Guid _saisie = Guid.CreateVersion7();
    private readonly Guid _validation = Guid.CreateVersion7();
    private readonly Guid _manager = Guid.CreateVersion7();

    private readonly FakeTenantContext _tenant = new()
    {
        Descriptor = new TenantDescriptor(Dossier, Organisation, DatabaseProvider.Sqlite, "config:test"),
    };

    private readonly FakeUser _user = new();
    private readonly FakePermissions _permissions = new();
    private readonly InMemoryPolicies _policies = new();
    private readonly InMemoryCustomers _customers = new();
    private readonly InMemoryBilling _billing = new();
    private ServiceProvider _root = null!;
    private AsyncServiceScope _scope;

    public Task InitializeAsync()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IOrganisationPolicyStore>(_policies);
        services.AddApplication();
        services.AddSingleton<ITenantContext>(_tenant);
        services.AddSingleton<ICurrentUser>(_user);
        services.AddSingleton<IPermissionService>(_permissions);
        services.AddSingleton<ICustomerRepository>(_customers);
        services.AddSingleton<ICustomerReadStore>(_customers);
        services.AddSingleton<IUnitOfWork>(_customers);
        services.AddIntegrityFakes(new InMemoryIntegrity(Dossier));
        services.AddPartiesAndCatalogFakes(new InMemoryPartiesAndCatalog(), new FakeDirectories(), _billing);
        services.AddSingleton<IDossierProvisioner>(new RecordingDossierProvisioner());
        services.AddSingleton<IAuthorizationDirectory>(new EmptyAuthorizationDirectory());
        services.AddSingleton<IDossierDirectory>(new EmptyDossierDirectory());
        _root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _root.CreateAsyncScope();
        _policies.Policies[Organisation] = new SegregationOfDutiesPolicy(true, 100m);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _root.DisposeAsync();
    }

    private ICommandDispatcher Commands => _scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

    private IQueryDispatcher Queries => _scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();

    private Task<Result> As(Guid user, ICommand<Result> command)
    {
        _user.UserId = user;
        return Commands.SendAsync(command, default);
    }

    private async Task<InvoiceResponse> ReadAsync(InvoiceId id) => (await Queries.QueryAsync(new GetInvoiceQuery(Dossier, id), default)).Value;

    /// <summary>Facture de 360 € TTC saisie par <see cref="_saisie"/>.</summary>
    private async Task<InvoiceId> DraftInvoiceAsync()
    {
        _user.UserId = _saisie;
        (await Commands.SendAsync(new SaveCompanyProfileCommand(Dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
        Guid customer = (await Commands.SendAsync(new CreateCustomerCommand(Dossier, Martin), default)).Value.Value;
        SaveInvoiceRequest request = new()
        {
            CustomerId = customer,
            Lines = [new DocumentLineRequest { Designation = "Conseil", Quantity = 2, UnitPriceExclTax = 150m, UnitCode = "HUR", Kind = CatalogItemKindDto.Service }],
        };
        return (await Commands.SendAsync(new CreateInvoiceCommand(Dossier, request), default)).Value;
    }

    /// <summary>Avoir total (360 € TTC, au-delà du seuil de 100 €) saisi par <see cref="_saisie"/> sur une facture validée.</summary>
    private async Task<InvoiceId> DraftCreditNoteAsync()
    {
        InvoiceId invoice = await DraftInvoiceAsync();
        (await As(_validation, new ValidateInvoiceCommand(Dossier, invoice, null))).IsSuccess.Should().BeTrue();
        _user.UserId = _saisie;
        return (await Commands.SendAsync(new CreateCreditNoteCommand(Dossier, invoice, new CreateCreditNoteRequest()), default)).Value;
    }

    private void SansDroitDApprouver() => _permissions.Granted.Remove(Permission.AvoirApprouver);

    [Fact]
    public async Task Qui_a_saisi_la_facture_ne_la_valide_pas_et_aucun_numero_n_est_consomme()
    {
        InvoiceId id = await DraftInvoiceAsync();

        Result refused = await As(_saisie, new ValidateInvoiceCommand(Dossier, id, null));

        refused.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.SegregationOfDuties.EnteredByIssuer);
        _billing.NextNumbers.Should().BeEmpty();
        _billing.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task Le_dernier_modificateur_du_brouillon_ne_le_valide_pas_non_plus()
    {
        InvoiceId id = await DraftInvoiceAsync();
        InvoiceResponse draft = await ReadAsync(id);
        SaveInvoiceRequest changed = new()
        {
            CustomerId = draft.CustomerId,
            Lines = [new DocumentLineRequest { Designation = "Conseil", Quantity = 3, UnitPriceExclTax = 150m, UnitCode = "HUR", Kind = CatalogItemKindDto.Service }],
        };
        (await As(_validation, new UpdateInvoiceCommand(Dossier, id, changed, null))).IsSuccess.Should().BeTrue();

        Result refused = await As(_validation, new ValidateInvoiceCommand(Dossier, id, null));

        refused.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.SegregationOfDuties.EnteredByIssuer);
    }

    [Fact]
    public async Task Un_autre_collaborateur_valide_puis_l_auteur_de_la_saisie_ne_peut_pas_emettre()
    {
        InvoiceId id = await DraftInvoiceAsync();

        (await As(_validation, new ValidateInvoiceCommand(Dossier, id, null))).IsSuccess.Should().BeTrue();
        Result refused = await As(_saisie, new IssueInvoiceCommand(Dossier, id, null));
        Result issued = await As(_validation, new IssueInvoiceCommand(Dossier, id, null));

        refused.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.SegregationOfDuties.EnteredByIssuer);
        issued.IsSuccess.Should().BeTrue();
        (await ReadAsync(id)).Status.Should().Be(InvoiceStatusDto.Issued);
    }

    [Fact]
    public async Task Politique_inactive_l_auteur_valide_et_emet_lui_meme()
    {
        _policies.Policies[Organisation] = SegregationOfDutiesPolicy.Disabled;
        InvoiceId id = await DraftInvoiceAsync();

        (await As(_saisie, new ValidateInvoiceCommand(Dossier, id, null))).IsSuccess.Should().BeTrue();
        (await As(_saisie, new IssueInvoiceCommand(Dossier, id, null))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Un_avoir_au_dela_du_seuil_attend_un_manager_avant_sa_validation()
    {
        InvoiceId credit = await DraftCreditNoteAsync();
        SansDroitDApprouver();
        long numbersBefore = _billing.NextNumbers.Values.Sum();

        Result refused = await As(_validation, new ValidateInvoiceCommand(Dossier, credit, null));

        refused.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.SegregationOfDuties.ManagerApprovalRequired);
        _billing.NextNumbers.Values.Sum().Should().Be(numbersBefore);
        (await ReadAsync(credit)).Status.Should().Be(InvoiceStatusDto.Draft);
    }

    [Fact]
    public async Task Approuve_par_un_manager_l_avoir_se_valide_par_un_collaborateur()
    {
        InvoiceId credit = await DraftCreditNoteAsync();

        Result approved = await As(_manager, new ApproveCreditNoteCommand(Dossier, credit, null));
        SansDroitDApprouver();
        Result validated = await As(_validation, new ValidateInvoiceCommand(Dossier, credit, null));

        approved.IsSuccess.Should().BeTrue();
        validated.IsSuccess.Should().BeTrue(string.Join(", ", validated.Errors.Select(e => e.Code)));
        InvoiceResponse response = await ReadAsync(credit);
        response.Status.Should().Be(InvoiceStatusDto.Validated);
        response.CreditApprovedBy.Should().Be(_manager);
        response.CreditApprovedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Le_manager_qui_valide_l_avoir_l_approuve_du_meme_coup()
    {
        InvoiceId credit = await DraftCreditNoteAsync();

        Result validated = await As(_manager, new ValidateInvoiceCommand(Dossier, credit, null));

        validated.IsSuccess.Should().BeTrue(string.Join(", ", validated.Errors.Select(e => e.Code)));
        (await ReadAsync(credit)).CreditApprovedBy.Should().Be(_manager);
    }

    [Fact]
    public async Task L_auteur_d_un_avoir_ne_peut_pas_l_approuver()
    {
        InvoiceId credit = await DraftCreditNoteAsync();

        Result refused = await As(_saisie, new ApproveCreditNoteCommand(Dossier, credit, null));

        refused.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.SegregationOfDuties.ApproverIsCreator);
        (await ReadAsync(credit)).CreditApprovedBy.Should().BeNull();
    }

    [Fact]
    public async Task Sans_le_droit_Avoir_Approuver_l_approbation_est_refusee()
    {
        InvoiceId credit = await DraftCreditNoteAsync();
        SansDroitDApprouver();

        Result refused = await As(_manager, new ApproveCreditNoteCommand(Dossier, credit, null));

        refused.Errors.Should().ContainSingle().Which.Kind.Should().Be(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Seul_un_avoir_s_approuve()
    {
        InvoiceId invoice = await DraftInvoiceAsync();

        Result refused = await As(_manager, new ApproveCreditNoteCommand(Dossier, invoice, null));

        refused.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.SegregationOfDuties.NotACreditNote);
    }

    [Fact]
    public async Task Un_avoir_sous_le_seuil_suit_le_circuit_normal()
    {
        _policies.Policies[Organisation] = new SegregationOfDutiesPolicy(true, 1000m);
        InvoiceId credit = await DraftCreditNoteAsync();
        SansDroitDApprouver();

        Result validated = await As(_validation, new ValidateInvoiceCommand(Dossier, credit, null));

        validated.IsSuccess.Should().BeTrue(string.Join(", ", validated.Errors.Select(e => e.Code)));
        (await ReadAsync(credit)).CreditApprovedBy.Should().BeNull();
    }
}
