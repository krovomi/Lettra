using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.External;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Customers;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Vigilance;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Domain.Vigilance;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Application.Tests;

/// <summary>Les quatre registres du test : répondent ce qu'on leur dit, ou tombent en panne ; gardent ce qu'on leur a demandé.</summary>
internal sealed class FakeOfficialRegisters : IBeneficialOwnerRegister, IAssetFreezeRegister, ILegalNoticeFeed, IUrssafAttestationCheck
{
    public Dictionary<string, IReadOnlyList<RegisteredOwner>> Owners { get; } = [];

    public List<FrozenEntry> Frozen { get; } = [new("Viktor Fictivenko", ["V. Fictivenko"]), new("Groupe Chimère Export", [])];

    public Dictionary<string, IReadOnlyList<LegalNotice>> Notices { get; } = [];

    public UrssafAnswer Urssaf { get; set; } = UrssafAnswer.Recognized;

    public bool Down { get; set; }

    public bool FreezeDown { get; set; }

    public List<string> Asked { get; } = [];

    public Task<Result<IReadOnlyList<RegisteredOwner>>> FindAsync(Siren siren, CancellationToken cancellationToken)
    {
        Asked.Add($"owners:{siren.Value}");
        return Task.FromResult(Down
            ? Result<IReadOnlyList<RegisteredOwner>>.Failure(DirectoryErrors.Unavailable)
            : Result<IReadOnlyList<RegisteredOwner>>.Success(Owners.GetValueOrDefault(siren.Value) ?? []));
    }

    public Task<Result<IReadOnlyList<FrozenEntry>>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult(FreezeDown
            ? Result<IReadOnlyList<FrozenEntry>>.Failure(DirectoryErrors.Unavailable)
            : Result<IReadOnlyList<FrozenEntry>>.Success(Frozen));

    public Task<Result<IReadOnlyList<LegalNotice>>> ListAsync(Siren siren, CancellationToken cancellationToken)
    {
        Asked.Add($"bodacc:{siren.Value}");
        return Task.FromResult(Result<IReadOnlyList<LegalNotice>>.Success(Notices.GetValueOrDefault(siren.Value) ?? []));
    }

    public Task<Result<UrssafAnswer>> VerifyAsync(Siren siren, string securityCode, CancellationToken cancellationToken)
    {
        Asked.Add($"urssaf:{siren.Value}:{securityCode}");
        return Task.FromResult(Down ? Result<UrssafAnswer>.Failure(DirectoryErrors.Unavailable) : Result<UrssafAnswer>.Success(Urssaf));
    }
}

/// <summary>Vérifier les tiers auprès des registres officiels, et reprendre du registre les bénéficiaires de la fiche anti-blanchiment.</summary>
public sealed class ThirdPartyCheckUseCaseTests : IAsyncLifetime
{
    private const string SupplierSiren = "542051180";
    private const string CustomerSiren = "732829320";
    private const string CompanySiren = "552100554";

    private static readonly DossierId Dossier = new(Guid.CreateVersion7());
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 28);

    private readonly FakeTenantContext _tenant = new()
    {
        Descriptor = new TenantDescriptor(Dossier, new OrganisationId(Guid.CreateVersion7()), DatabaseProvider.Sqlite, "config:test"),
    };

    private readonly FakePermissions _permissions = new();
    private readonly FakeUser _user = new();
    private readonly InMemoryCustomers _unitOfWork = new();
    private readonly InMemoryPartiesAndCatalog _store = new();
    private readonly InMemoryAttestationStore _attestations = new();
    private readonly InMemoryThirdPartyCheckStore _checks = new();
    private readonly InMemoryBilling _billing = new();
    private readonly FakeOfficialRegisters _registers = new();
    private ServiceProvider _root = null!;
    private AsyncServiceScope _scope;

    public Task InitializeAsync()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<TimeProvider>(new FixedClock(Now));
        services.AddSingleton<ITenantContext>(_tenant);
        services.AddSingleton<ICurrentUser>(_user);
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
        services.AddBillingFakes(_billing);
        services.AddSingleton<IAttestationStore>(_attestations);
        services.AddSingleton<IThirdPartyCheckStore>(_checks);
        services.AddSingleton<IBeneficialOwnerRegister>(_registers);
        services.AddSingleton<IAssetFreezeRegister>(_registers);
        services.AddSingleton<ILegalNoticeFeed>(_registers);
        services.AddSingleton<IUrssafAttestationCheck>(_registers);
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

    private async Task<SupplierId> SupplierAsync(string name, string? siren = SupplierSiren, string? holder = null, string? code = null)
    {
        SupplierId id = (await Commands.SendAsync(
            new CreateSupplierCommand(Dossier, siren is null
                ? new Supplier.Draft(name, null, null, null, null, null, "2 chemin du Moulin", null, "1000", "Bruxelles", "BE", holder)
                : new Supplier.Draft(name, siren, null, null, null, null, "2 chemin du Moulin", null, "69003", "Lyon", "FR", holder)),
            default)).Value;
        _checks.Parties.Add((ThirdPartyKind.Supplier, id.Value, name, siren is null ? null : Siren.Create(siren).Value));
        if (code is not null)
        {
            await _attestations.AddAsync(
                SubcontractorAttestation.Create(id, new SubcontractorAttestation.Draft(12_000m, new DateOnly(2026, 10, 1), code, null), Today, Now).Value, default);
        }

        return id;
    }

    private async Task<Guid> CustomerAsync(string name, string? siren = CustomerSiren)
    {
        Guid id = (await Commands.SendAsync(
            new CreateCustomerCommand(Dossier, new Customer.Draft(siren is null ? CustomerKind.Individual : CustomerKind.Company, name, siren, null, null, null, "1 rue de la Paix", null, "75002", "Paris", "FR")),
            default)).Value.Value;
        _checks.Parties.Add((ThirdPartyKind.Customer, id, name, siren is null ? null : Siren.Create(siren).Value));
        return id;
    }

    private async Task<Result<ThirdPartyCheckRow>> CheckAsync(ThirdPartyKind kind, Guid id) =>
        await Commands.SendAsync(new CheckThirdPartyCommand(Dossier, kind, id), default);

    [Fact]
    public async Task Un_fournisseur_sans_alerte_est_verifie_sur_les_quatre_registres_et_son_attestation_notee()
    {
        SupplierId id = await SupplierAsync("Menuiserie Dupuis", code: "AB12CD34EF56");
        _registers.Owners[SupplierSiren] = [new RegisteredOwner("Jean Dupuis", OwnershipKind.Capital, 100m)];

        ThirdPartyCheckRow row = (await CheckAsync(ThirdPartyKind.Supplier, id.Value)).Value;

        row.Should().Be(new ThirdPartyCheckRow(
            ThirdPartyKindDto.Supplier, id.Value, "Menuiserie Dupuis", SupplierSiren, ThirdPartyCheckStatusDto.Clear, Now, 1,
            FreezeScreeningDto.Clear, null, null, null, null, UrssafAnswerDto.Recognized));
        _registers.Asked.Should().Equal($"owners:{SupplierSiren}", $"bodacc:{SupplierSiren}", $"urssaf:{SupplierSiren}:AB12CD34EF56");
        (await _attestations.FindAsync(id, default))!.VerifiedOn.Should().Be(Today, "un code reconnu note la vérification de l'attestation");
        _checks.Checks.Should().ContainSingle().Which.CheckedBy.Should().Be(_user.UserId);
    }

    [Fact]
    public async Task Un_beneficiaire_gele_une_procedure_et_un_code_inconnu_demandent_un_examen()
    {
        SupplierId id = await SupplierAsync("Import Lumière", code: "ZZ99XY12AB34");
        _registers.Owners[SupplierSiren] = [new RegisteredOwner("Viktor Fictivenko", OwnershipKind.Control, null)];
        _registers.Notices[SupplierSiren] =
        [
            new LegalNotice(LegalNoticeKind.StruckOff, new DateOnly(2025, 1, 2), "Radiation"),
            new LegalNotice(LegalNoticeKind.CollectiveProceeding, new DateOnly(2026, 9, 15), "Jugement d'ouverture d'une procédure de redressement judiciaire"),
        ];
        _registers.Urssaf = UrssafAnswer.NotRecognized;

        ThirdPartyCheckRow row = (await CheckAsync(ThirdPartyKind.Supplier, id.Value)).Value;

        row.Status.Should().Be(ThirdPartyCheckStatusDto.ToReview);
        row.Freeze.Should().Be(FreezeScreeningDto.PossibleMatch);
        row.FreezeMatch.Should().Be("Viktor Fictivenko");
        row.Notice.Should().Be(LegalNoticeKindDto.CollectiveProceeding, "la plus récente annonce l'emporte");
        row.NoticeOn.Should().Be(new DateOnly(2026, 9, 15));
        row.Urssaf.Should().Be(UrssafAnswerDto.NotRecognized);
        (await _attestations.FindAsync(id, default))!.VerifiedOn.Should().BeNull();

        ThirdPartyCheckOverview.Indicator([.. _checks.Checks]).Should().Be(
            new DossierIndicator(DossierIndicatorKeys.ThirdPartiesToReview, 1, IndicatorLevel.Critical));
    }

    [Fact]
    public async Task Le_titulaire_du_compte_est_crible_et_un_client_sans_siren_n_interroge_que_la_liste_des_gels()
    {
        SupplierId supplier = await SupplierAsync("Transports Rapides", holder: "V. Fictivenko");
        Guid customer = await CustomerAsync("Groupe Chimère Export", siren: null);

        (await CheckAsync(ThirdPartyKind.Supplier, supplier.Value)).Value.FreezeMatch.Should().Be("V. Fictivenko");
        ThirdPartyCheckRow row = (await CheckAsync(ThirdPartyKind.Customer, customer)).Value;

        row.Should().BeEquivalentTo(new
        {
            Kind = ThirdPartyKindDto.Customer,
            Siren = (string?)null,
            OwnersFound = (int?)null,
            Freeze = FreezeScreeningDto.PossibleMatch,
            FreezeMatch = "Groupe Chimère Export",
            Urssaf = (UrssafAnswerDto?)null,
        });
        _registers.Asked.Should().NotContain(a => a.Contains(CustomerSiren, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reverifier_remplace_la_reponse_et_la_liste_met_les_tiers_a_examiner_en_tete()
    {
        Guid clear = await CustomerAsync("Boulangerie Martin");
        Guid never = await CustomerAsync("Atelier Zola", siren: null);
        SupplierId flagged = await SupplierAsync("Béton Armé");
        _registers.Notices[SupplierSiren] = [new LegalNotice(LegalNoticeKind.StruckOff, new DateOnly(2026, 9, 1), "Radiation")];
        await CheckAsync(ThirdPartyKind.Customer, clear);
        await CheckAsync(ThirdPartyKind.Supplier, flagged.Value);

        IReadOnlyList<ThirdPartyCheckRow> rows = (await Queries.QueryAsync(new ListThirdPartyChecksQuery(Dossier), default)).Value.Rows;

        rows.Select(r => (r.PartyId, r.Status)).Should().Equal(
            (flagged.Value, ThirdPartyCheckStatusDto.ToReview),
            (never, ThirdPartyCheckStatusDto.NeverChecked),
            (clear, ThirdPartyCheckStatusDto.Clear));

        _registers.Notices.Clear();
        (await CheckAsync(ThirdPartyKind.Supplier, flagged.Value)).Value.Status.Should().Be(ThirdPartyCheckStatusDto.Clear);
        _checks.Checks.Should().HaveCount(2, "une seule vérification gardée par tiers, la dernière");
    }

    [Fact]
    public async Task Un_tiers_inconnu_ou_archive_un_registre_en_panne_et_le_droit_manquant_sont_refuses()
    {
        (await CheckAsync(ThirdPartyKind.Customer, Guid.CreateVersion7()))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(ThirdPartyCheckErrors.PartyNotFoundCode);
        (await CheckAsync(ThirdPartyKind.Supplier, Guid.CreateVersion7()))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(ThirdPartyCheckErrors.PartyNotFoundCode);
        (await CheckAsync((ThirdPartyKind)9, Guid.CreateVersion7()))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(ThirdPartyCheckErrors.PartyNotFoundCode);

        SupplierId id = await SupplierAsync("Papeterie Durand", code: "AB12CD34EF56");
        _registers.Down = true;
        (await CheckAsync(ThirdPartyKind.Supplier, id.Value)).Errors.Should().ContainSingle().Which.Code.Should().Be(DirectoryErrors.UnavailableCode);
        _registers.Down = false;
        _registers.FreezeDown = true;
        (await CheckAsync(ThirdPartyKind.Supplier, id.Value)).Errors.Should().ContainSingle().Which.Code.Should().Be(DirectoryErrors.UnavailableCode);
        _checks.Checks.Should().BeEmpty("rien n'est gardé quand un registre ne répond pas");

        _permissions.Granted.Remove(Permission.VigilanceGerer);
        (await CheckAsync(ThirdPartyKind.Supplier, id.Value)).Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
        _permissions.Granted.Remove(Permission.VigilanceConsulter);
        (await Queries.QueryAsync(new ListThirdPartyChecksQuery(Dossier), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
    }

    [Fact]
    public async Task Le_code_d_une_attestation_reconnu_par_l_urssaf_note_la_verification()
    {
        SupplierId id = await SupplierAsync("Menuiserie Dupuis", code: "AB12CD34EF56");

        (await Commands.SendAsync(new VerifyAttestationCodeCommand(Dossier, id), default)).Value.Should().Be(UrssafAnswer.Recognized);
        (await _attestations.FindAsync(id, default))!.VerifiedOn.Should().Be(Today);

        SupplierId unknown = await SupplierAsync("Import Lumière", siren: "123456782", code: "ZZ99XY12AB34");
        _registers.Urssaf = UrssafAnswer.NotRecognized;
        (await Commands.SendAsync(new VerifyAttestationCodeCommand(Dossier, unknown), default)).Value.Should().Be(UrssafAnswer.NotRecognized);
        (await _attestations.FindAsync(unknown, default))!.VerifiedOn.Should().BeNull();
    }

    [Fact]
    public async Task Verifier_un_code_exige_le_fournisseur_le_code_et_le_siren()
    {
        (await Commands.SendAsync(new VerifyAttestationCodeCommand(Dossier, SupplierId.New()), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(AttestationErrors.SupplierNotFound.Code);
        SupplierId withoutCode = await SupplierAsync("Papeterie Durand");
        (await Commands.SendAsync(new VerifyAttestationCodeCommand(Dossier, withoutCode), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(AttestationErrors.VerificationNeedsCode.Code);
        SupplierId withoutSiren = await SupplierAsync("Plomberie Sans Siren", siren: null, code: "AB12CD34EF56");
        (await Commands.SendAsync(new VerifyAttestationCodeCommand(Dossier, withoutSiren), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(AttestationErrors.SirenMissing.Code);

        SupplierId id = await SupplierAsync("Menuiserie Dupuis", siren: "123456790", code: "AB12CD34EF56");
        _registers.Down = true;
        (await Commands.SendAsync(new VerifyAttestationCodeCommand(Dossier, id), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(DirectoryErrors.UnavailableCode);
        _registers.Asked.Should().ContainSingle("seul le fournisseur complet interroge l'URSSAF");
    }

    [Fact]
    public async Task Les_beneficiaires_de_l_entreprise_viennent_du_registre_et_sont_cribles()
    {
        LookupRegisteredOwnersQuery query = new(Dossier);
        (await Queries.QueryAsync(query, default)).Value.Should().BeEquivalentTo(new RegisteredOwnersResponse(null, [], null), "sans fiche entreprise, rien n'est demandé");

        await _billing.AddAsync(CompanyProfile.Create(
            new CompanyProfile.Draft(
                "Groupe Chimère Export", "SAS", null, CompanySiren, null, null, null, "1 rue des Exemples", null, "69002", "Lyon", "FR",
                VatFranchise: false, VatOnDebits: false, PaymentDays: 30, null, null, null, null, null),
            Now).Value, default);
        _registers.Owners[CompanySiren] =
        [
            new RegisteredOwner("Claire Martin", OwnershipKind.Capital, 60m),
            new RegisteredOwner("Viktor Fictivenko", OwnershipKind.Capital, 40m),
        ];

        RegisteredOwnersResponse response = (await Queries.QueryAsync(query, default)).Value;

        response.Siren.Should().Be(CompanySiren);
        response.Owners.Should().Equal(
            new RegisteredOwnerDto("Claire Martin", OwnershipKindDto.Capital, 60m, false),
            new RegisteredOwnerDto("Viktor Fictivenko", OwnershipKindDto.Capital, 40m, true));
        response.CompanyFreezeMatch.Should().Be("Groupe Chimère Export");

        _registers.FreezeDown = true;
        (await Queries.QueryAsync(query, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(DirectoryErrors.UnavailableCode);
        _registers.Down = true;
        (await Queries.QueryAsync(query, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(DirectoryErrors.UnavailableCode);
        _permissions.Granted.Remove(Permission.VigilanceGerer);
        (await Queries.QueryAsync(query, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
    }

    [Fact]
    public void L_ecran_et_l_indicateur_se_calculent_sans_nom_de_tiers()
    {
        ThirdPartyCheckOverview.Indicator([]).Should().BeNull();
        ThirdPartyCheck notice = ThirdPartyCheck.Create(Guid.CreateVersion7(), ThirdPartyKind.Customer,
            new ThirdPartyFindings(0, FreezeScreening.Clear, null, LegalNoticeKind.StruckOff, Today, "Radiation", null), Guid.Empty, Now);
        ThirdPartyCheckOverview.Indicator([notice]).Should().Be(new DossierIndicator(DossierIndicatorKeys.ThirdPartiesToReview, 1, IndicatorLevel.Warning));
        FluentActions.Invoking(() => ThirdPartyCheckOverview.Indicator(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ThirdPartyCheckOverview.Build(null!, Today)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ThirdPartyCheckOverview.ToRow(null!, Today)).Should().Throw<ArgumentNullException>();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
