using Lettra.Application.Abstractions.External;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Domain.Vigilance;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Application.Tests;

/// <summary>Banque du test : répond ce qu'on lui dit, ou tombe en panne ; garde ce qu'on lui a demandé.</summary>
internal sealed class FakePayeeBank : IPayeeVerification
{
    public PayeeVerificationResult Answer { get; set; } = new(PayeeMatch.Match);

    public bool Down { get; set; }

    public List<(string Iban, string Name)> Asked { get; } = [];

    public Task<Result<PayeeVerificationResult>> VerifyAsync(Iban iban, string name, CancellationToken cancellationToken)
    {
        Asked.Add((iban.Value, name));
        return Task.FromResult(Down ? Result<PayeeVerificationResult>.Failure(DirectoryErrors.Unavailable) : Answer);
    }
}

/// <summary>Nom et IBAN vérifiés : vérification du bénéficiaire gardée sur la fiche, et liste des bénéficiaires à contrôler.</summary>
public sealed class PayeeCheckUseCaseTests : IAsyncLifetime
{
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTenantContext _tenant = new()
    {
        Descriptor = new TenantDescriptor(Dossier, new OrganisationId(Guid.CreateVersion7()), DatabaseProvider.Sqlite, "config:test"),
    };

    private readonly FakePermissions _permissions = new();
    private readonly InMemoryCustomers _unitOfWork = new();
    private readonly InMemoryPartiesAndCatalog _store = new();
    private readonly FakePayeeBank _bank = new();
    private ServiceProvider _root = null!;
    private AsyncServiceScope _scope;

    public Task InitializeAsync()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<IPayeeVerification>(_bank);
        services.AddSingleton<TimeProvider>(new FixedClock(Now));
        services.AddSingleton<ITenantContext>(_tenant);
        services.AddSingleton<ICurrentUser>(new FakeUser());
        services.AddSingleton<IPermissionService>(_permissions);
        services.AddSingleton<ICustomerRepository>(_unitOfWork);
        services.AddSingleton<ICustomerReadStore>(_unitOfWork);
        services.AddSingleton<IUnitOfWork>(_unitOfWork);
        services.AddPartiesAndCatalogFakes(_store, new FakeDirectories());
        services.AddIntegrityFakes(new InMemoryIntegrity(Dossier));
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

    private async Task<SupplierId> CreateAsync(string name, string? iban = "FR7630006000011234567890189", string? holder = null) =>
        (await Commands.SendAsync(
            new CreateSupplierCommand(Dossier, new Supplier.Draft(name, null, null, null, null, iban, "2 chemin du Moulin", null, "1000", "Bruxelles", "BE", holder)),
            default)).Value;

    private async Task<IReadOnlyList<PayeeCheckRowResponse>> ListAsync(bool onlyToReview = true, string? search = null, PayeeCheckSortDto sort = PayeeCheckSortDto.Risk) =>
        (await Queries.QueryAsync(new ListPayeeChecksQuery(Dossier, onlyToReview, search, Sort: sort), default)).Value.Items;

    [Fact]
    public async Task La_banque_recoit_l_iban_et_le_nom_du_titulaire_et_sa_reponse_reste_sur_la_fiche()
    {
        SupplierId id = await CreateAsync("Minoterie Dupuis", holder: "Jean Dupuis");
        _bank.Answer = new PayeeVerificationResult(PayeeMatch.CloseMatch, "Jean Dupuy");

        Result<PayeeCheckResponse> checkedPayee = await Commands.SendAsync(new VerifySupplierPayeeCommand(Dossier, id), default);

        checkedPayee.Value.Should().Be(new PayeeCheckResponse(PayeeMatchDto.CloseMatch, Now, "Jean Dupuy"));
        _bank.Asked.Should().Equal(("FR7630006000011234567890189", "Jean Dupuis"));
        SupplierResponse read = (await Queries.QueryAsync(new GetSupplierQuery(Dossier, id), default)).Value;
        read.PayeeCheck.Should().Be(checkedPayee.Value);
        read.AccountHolderName.Should().Be("Jean Dupuis");
        read.HolderNameMatch.Should().Be(PayeeMatchDto.NoMatch, "le gérant n'est pas la société : à contrôler avant de payer");
    }

    [Fact]
    public async Task Sans_iban_inconnu_ou_banque_en_panne_rien_n_est_garde()
    {
        SupplierId withoutIban = await CreateAsync("Minoterie Dupuis", iban: null);
        (await Commands.SendAsync(new VerifySupplierPayeeCommand(Dossier, withoutIban), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(SupplierErrors.IbanMissingCode);
        (await Commands.SendAsync(new VerifySupplierPayeeCommand(Dossier, SupplierId.New()), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(SupplierErrors.NotFoundCode);

        SupplierId id = await CreateAsync("Papeterie Durand");
        _bank.Down = true;
        (await Commands.SendAsync(new VerifySupplierPayeeCommand(Dossier, id), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(DirectoryErrors.UnavailableCode);
        _store.Suppliers[id].PayeeCheckResult.Should().BeNull();
        _bank.Asked.Should().ContainSingle("ni la fiche sans IBAN ni la fiche inconnue n'interrogent la banque");
    }

    [Fact]
    public async Task Verifier_exige_le_droit_de_modifier_les_tiers()
    {
        SupplierId id = await CreateAsync("Papeterie Durand");
        _permissions.Granted.Remove(Permission.TiersModifier);

        (await Commands.SendAsync(new VerifySupplierPayeeCommand(Dossier, id), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
        _bank.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task Les_beneficiaires_a_controler_passent_du_plus_risque_au_plus_sur()
    {
        SupplierId verified = await CreateAsync("Assurance de la Vallée");
        await Commands.SendAsync(new VerifySupplierPayeeCommand(Dossier, verified), default);
        SupplierId close = await CreateAsync("Minoterie Dupuis");
        _bank.Answer = new PayeeVerificationResult(PayeeMatch.CloseMatch, "Minoterie Dupuy");
        await Commands.SendAsync(new VerifySupplierPayeeCommand(Dossier, close), default);
        SupplierId never = await CreateAsync("Papeterie Durand");
        SupplierId strangeHolder = await CreateAsync("Hébergeur Les Pins", holder: "Schmidt Handels GmbH");
        SupplierId changed = await CreateAsync("Location de salles");
        _store.PendingIbanChanges.Add(changed);
        await CreateAsync("Sans IBAN", iban: null);

        (await ListAsync()).Select(r => r.SupplierId).Should().Equal(strangeHolder.Value, changed.Value, close.Value, never.Value);
        (await ListAsync(onlyToReview: false)).Select(r => r.Name).Should().Contain("Assurance de la Vallée").And.NotContain("Sans IBAN");

        PayeeCheckRowResponse row = (await ListAsync())[0];
        row.HolderNameMatch.Should().Be(PayeeMatchDto.NoMatch);
        row.MaskedIban.Should().Be("FR76 •••• 0189");
        row.ToReview.Should().BeTrue();
    }

    [Fact]
    public async Task Un_fournisseur_sans_attestation_de_vigilance_valable_passe_a_controler_apres_un_iban_change()
    {
        SupplierId changed = await CreateAsync("Location de salles");
        _store.PendingIbanChanges.Add(changed);
        await Commands.SendAsync(new VerifySupplierPayeeCommand(Dossier, changed), default);
        SupplierId subcontractor = await CreateAsync("Maçonnerie Roux");
        await Commands.SendAsync(new VerifySupplierPayeeCommand(Dossier, subcontractor), default);
        SupplierId small = await CreateAsync("Papeterie Durand");
        await Commands.SendAsync(new VerifySupplierPayeeCommand(Dossier, small), default);
        _bank.Answer = new PayeeVerificationResult(PayeeMatch.CloseMatch, "Minoterie Dupuy");
        SupplierId close = await CreateAsync("Minoterie Dupuis");
        await Commands.SendAsync(new VerifySupplierPayeeCommand(Dossier, close), default);

        (await SaveAttestationAsync(subcontractor, new SubcontractorAttestation.Draft(18_000m, null, null, null))).IsSuccess.Should().BeTrue();
        (await SaveAttestationAsync(small, new SubcontractorAttestation.Draft(1_200m, null, null, null))).IsSuccess.Should().BeTrue();

        (await ListAsync()).Select(r => r.SupplierId).Should().Equal(changed.Value, subcontractor.Value, close.Value);
        (await ListAsync())[1].AttestationDue.Should().BeTrue();
        (await ListAsync(onlyToReview: false)).Single(r => r.SupplierId == small.Value).AttestationDue.Should().BeFalse("sous le seuil");

        long version = (await Queries.QueryAsync(new ListAttestationsQuery(Dossier), default)).Value.Rows.Single(r => r.SupplierId == subcontractor.Value).Version!.Value;
        DateOnly issued = new(2026, 10, 1);
        (await SaveAttestationAsync(subcontractor, new SubcontractorAttestation.Draft(18_000m, issued, "AB12CD34EF56", issued), version + 1))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(ConcurrencyErrors.VersionMismatchCode);
        (await SaveAttestationAsync(subcontractor, new SubcontractorAttestation.Draft(18_000m, issued, "AB12CD34EF56", issued), version)).IsSuccess.Should().BeTrue();
        (await ListAsync()).Select(r => r.SupplierId).Should().Equal(changed.Value, close.Value);
    }

    [Fact]
    public async Task L_attestation_d_un_fournisseur_inconnu_supprime_ou_sans_droit_est_refusee()
    {
        SubcontractorAttestation.Draft draft = new(18_000m, null, null, null);
        SupplierId deleted = await CreateAsync("Ancien sous-traitant");
        await Commands.SendAsync(new DeleteSupplierCommand(Dossier, deleted, null), default);

        (await SaveAttestationAsync(SupplierId.New(), draft)).Errors.Should().ContainSingle().Which.Code.Should().Be(AttestationErrors.SupplierNotFoundCode);
        (await SaveAttestationAsync(deleted, draft)).Errors.Should().ContainSingle().Which.Code.Should().Be(AttestationErrors.SupplierNotFoundCode);
        (await SaveAttestationAsync(await CreateAsync("Maçonnerie Roux"), draft with { ContractAmountExclTax = -1m }))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(AttestationErrors.AmountInvalidCode);

        _permissions.Granted.Remove(Permission.TiersModifier);
        (await SaveAttestationAsync(await CreateAsync("Charpente Petit"), draft)).Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
        _permissions.Granted.Remove(Permission.TiersConsulter);
        (await Queries.QueryAsync(new ListAttestationsQuery(Dossier), default)).Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
    }

    private Task<Result> SaveAttestationAsync(SupplierId supplier, SubcontractorAttestation.Draft draft, long? version = null) =>
        Commands.SendAsync(new SaveAttestationCommand(Dossier, supplier, draft, version), default);

    [Fact]
    public async Task La_recherche_ignore_accents_et_casse_et_le_tri_par_nom_s_inverse()
    {
        await CreateAsync("Hébergeur Les Pins");
        await CreateAsync("Papeterie Durand", holder: "Famille Durand");
        await CreateAsync("Atelier Martin");

        (await ListAsync(search: "hebergeur")).Select(r => r.Name).Should().Equal("Hébergeur Les Pins");
        (await ListAsync(search: "famille")).Should().ContainSingle("le titulaire compte aussi").Which.Name.Should().Be("Papeterie Durand");
        (await ListAsync(sort: PayeeCheckSortDto.Name)).Select(r => r.Name).Should().Equal("Atelier Martin", "Hébergeur Les Pins", "Papeterie Durand");
        (await Queries.QueryAsync(new ListPayeeChecksQuery(Dossier, Sort: PayeeCheckSortDto.Name, Direction: SortDirectionDto.Descending), default))
            .Value.Items.Select(r => r.Name).Should().Equal("Papeterie Durand", "Hébergeur Les Pins", "Atelier Martin");
    }

    [Fact]
    public async Task La_liste_valide_sa_pagination_sa_recherche_et_son_tri()
    {
        (await Queries.QueryAsync(new ListPayeeChecksQuery(Dossier, Page: 0), default)).IsFailure.Should().BeTrue();
        (await Queries.QueryAsync(new ListPayeeChecksQuery(Dossier, Search: new string('x', 101)), default)).IsFailure.Should().BeTrue();
        (await Queries.QueryAsync(new ListPayeeChecksQuery(Dossier, Sort: (PayeeCheckSortDto)9), default)).IsFailure.Should().BeTrue();

        _permissions.Granted.Remove(Permission.TiersConsulter);
        (await Queries.QueryAsync(new ListPayeeChecksQuery(Dossier), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
    }

    [Fact]
    public async Task Sans_banque_branchee_la_verification_n_est_pas_possible()
    {
        ServiceCollection services = new();
        services.AddApplication();

        IPayeeVerification none = services.BuildServiceProvider().GetRequiredService<IPayeeVerification>();

        (await none.VerifyAsync(Iban.Create("FR7630006000011234567890189").Value, "X", default)).Value.Result.Should().Be(PayeeMatch.NotPossible);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
