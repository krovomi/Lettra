using System.Globalization;
using System.Net.Http.Headers;
using Lettra.Application.Abstractions.Identity;
using Lettra.Bootstrap;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Exports;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Sqlite.Local;
using Lettra.UI.Abstractions;
using Lettra.UI.Gateway.Http;
using Lettra.UI.Gateway.InProcess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Lettra.Api.Tests;

/// <summary>
/// ADR-0005 : la même UI appelle la même interface, par HTTP (WASM) ou en direct (MAUI hors ligne).
/// Les deux chemins doivent produire le même résultat et les mêmes codes d'erreur.
/// </summary>
public sealed partial class GatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-maui-").FullName;

    private ServiceProvider HttpGateway(Guid dossier)
    {
        ServiceCollection services = new();
        services.AddSingleton<IDossierSelection>(new FixedDossierSelection(dossier));
        services.AddHttpGateways(new Uri("http://localhost/"));
        services.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b =>
        {
            b.PrimaryHandler = factory.Server.CreateHandler();
            b.AdditionalHandlers.Insert(0, new BearerHandler(LettraApiFactory.TokenFor(LettraApiFactory.ManagerId)));
        }));
        return services.BuildServiceProvider();
    }

    /// <summary>Le poste isolé (mode 1) : registre local, premier dossier créé au démarrage, base SQLite chiffrée.</summary>
    private ServiceProvider InProcessGateway(Guid? dossier)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [LettraBootstrap.DeploymentModeKey] = "IsolatedDesktop",
            [LettraBootstrap.LocalDataDirectoryKey] = _folder,
            ["Lettra:LocalIdentity:StorePath"] = Path.Combine(_folder, "compte-local.json"),
            ["Lettra:LocalIdentity:Pbkdf2Iterations"] = "1000",
            // Comme l'API de test : la plateforme agréée simulée (par défaut, aucune plateforme et tout envoi échoue).
            ["Lettra:EInvoicing:Platform"] = "Simulated",
        }).Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddLettra(configuration);
        services.AddSingleton<IKekStore, InMemoryKekStore>();
        services.AddSingleton<IDossierSelection>(sp => new FixedDossierSelection(dossier ?? sp.GetRequiredService<LocalDossierRegistry>().ActiveDossierId));
        services.AddInProcessGateways();
        ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        // Le poste isolé n'ouvre rien avant le déverrouillage : on crée le compte local, ce qui ouvre la session.
        ILocalAccounts accounts = provider.GetRequiredService<ILocalAccounts>();
        if (!accounts.GetStatusAsync(default).AsTask().GetAwaiter().GetResult().Exists)
        {
            accounts.CreateAsync("Poste de test", "phrase secrète de test", default).AsTask().GetAwaiter().GetResult().IsSuccess.Should().BeTrue();
        }
        else
        {
            accounts.UnlockAsync("phrase secrète de test", default).AsTask().GetAwaiter().GetResult().IsSuccess.Should().BeTrue();
        }

        provider.InitializeLettraAsync(configuration, CancellationToken.None).GetAwaiter().GetResult();
        return provider;
    }

    public static TheoryData<string> Transports => ["http", "in-process"];

    private ServiceProvider Gateway(string transport, Guid? dossier = null) =>
        transport == "http"
            ? HttpGateway(dossier ?? LettraApiFactory.DossierId)
            : InProcessGateway(dossier);

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Creer_puis_lister(string transport)
    {
        await using ServiceProvider services = Gateway(transport);
        ICustomersGateway gateway = services.GetRequiredService<ICustomersGateway>();

        OperationResult<CustomerResponse> created = await gateway.CreateAsync(CustomerApiTests.Martin(), default);
        OperationResult<PagedResponse<CustomerSummary>> list = await gateway.ListAsync(1, 50, default);

        created.IsSuccess.Should().BeTrue();
        created.Value!.Siren.Should().Be("732829320");
        list.Value!.Items.Should().Contain(c => c.Id == created.Value.Id);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Memes_codes_d_erreur_par_champ(string transport)
    {
        await using ServiceProvider services = Gateway(transport);
        CreateCustomerRequest request = CustomerApiTests.Martin();
        request.Siret = "73282932000075";
        request.PostalCode = "7500";

        OperationResult<CustomerResponse> result = await services.GetRequiredService<ICustomersGateway>().CreateAsync(request, default);

        result.Errors.Should().BeEquivalentTo(new[]
        {
            new ErrorDetail(ErrorCodes.SharedKernel.SiretInvalid, nameof(CreateCustomerRequest.Siret)),
            new ErrorDetail(ErrorCodes.SharedKernel.PostalCodeInvalid, nameof(CreateCustomerRequest.PostalCode)),
        });
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Dossier_inconnu_meme_refus(string transport)
    {
        await using ServiceProvider services = Gateway(transport, dossier: Guid.CreateVersion7());

        OperationResult<PagedResponse<CustomerSummary>> result = await services.GetRequiredService<ICustomersGateway>().ListAsync(1, 10, default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Tenancy.DossierNotFound);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Facturer_valider_emettre_puis_crediter(string transport)
    {
        await using ServiceProvider services = Gateway(transport);
        IInvoicesGateway invoices = services.GetRequiredService<IInvoicesGateway>();
        Guid customer = (await services.GetRequiredService<ICustomersGateway>().CreateAsync(CustomerApiTests.Martin(), default)).Value!.Id;
        OperationResult<CompanyProfileResponse> profile = await services.GetRequiredService<IBillingSettingsGateway>().SaveCompanyProfileAsync(
            new SaveCompanyProfileRequest
            {
                Name = "Conseil Lettra", LegalForm = "SAS", ShareCapital = 10_000m, Siren = "552100554", Rcs = "RCS Paris 552 100 554",
                VatNumber = "FR96552100554", AddressLine1 = "10 avenue de l'Opéra", PostalCode = "75001", City = "Paris",
                LatePenaltyRate = 10m, EarlyPaymentDiscount = "Pas d'escompte pour paiement anticipé",
            },
            null,
            default);
        profile.IsSuccess.Should().BeTrue(string.Join(", ", profile.Errors.Select(e => e.Code)));

        InvoiceResponse draft = (await invoices.CreateAsync(
            new SaveInvoiceRequest
            {
                CustomerId = customer,
                Lines = [new DocumentLineRequest { Designation = "Conseil", Quantity = 3, UnitPriceExclTax = 100m, UnitCode = "HUR", Kind = CatalogItemKindDto.Service }],
            },
            default)).Value!;
        OperationResult<InvoiceResponse> stale = await invoices.ValidateAsync(draft.Id, draft.Version + 1, default);
        OperationResult<InvoiceResponse> validated = await invoices.ValidateAsync(draft.Id, draft.Version, default);
        OperationResult<InvoiceResponse> issued = await invoices.IssueAsync(draft.Id, validated.Value!.Version, default);
        OperationResult<InvoiceResponse> credit = await invoices.CreateCreditNoteAsync(
            draft.Id, new CreateCreditNoteRequest { Reason = "Une heure offerte", Lines = [new CreditNoteLineRequest(1, 1m)] }, default);

        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        stale.Current!.Version.Should().Be(draft.Version, "le refus porte la pièce actuelle, sur les deux transports");
        validated.Value.Number.Should().StartWith("FAC-");
        validated.Value.ContentHash.Should().Be(CanonicalDocument.ComputeHash(validated.Value));
        issued.Value!.Status.Should().Be(InvoiceStatusDto.Issued);
        credit.Value!.Kind.Should().Be(InvoiceKindDto.CreditNote);
        credit.Value.Totals.TotalInclTax.Should().Be(120m);

        // Facture électronique : même XML sur les deux transports, refus identique pour un brouillon.
        OperationResult<byte[]> cii = await invoices.GetEInvoiceAsync(draft.Id, EInvoiceFormatDto.Cii, default);
        cii.IsSuccess.Should().BeTrue(string.Join(", ", cii.Errors.Select(e => e.Code)));
        System.Text.Encoding.UTF8.GetString(cii.Value!).Should().Contain("CrossIndustryInvoice").And.Contain(validated.Value.Number);
        (await invoices.GetEInvoiceAsync(credit.Value.Id, EInvoiceFormatDto.Ubl, default)).Errors
            .Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.EInvoice.NotValidated);
        (await invoices.DeleteAsync(credit.Value.Id, credit.Value.Version, default)).Value.Should().BeTrue();
        (await invoices.DeleteAsync(draft.Id, null, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Billing.NotDraft);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Transmettre_a_la_plateforme_puis_declarer_un_encaissement_partiel(string transport)
    {
        await using ServiceProvider services = Gateway(transport);
        IInvoicesGateway invoices = services.GetRequiredService<IInvoicesGateway>();
        IPlatformTransmissionGateway platform = services.GetRequiredService<IPlatformTransmissionGateway>();
        Guid customer = (await services.GetRequiredService<ICustomersGateway>().CreateAsync(CustomerApiTests.Martin(), default)).Value!.Id;
        await SaveCompanyProfileAsync(services);
        InvoiceResponse draft = (await invoices.CreateAsync(
            new SaveInvoiceRequest
            {
                CustomerId = customer,
                Lines = [new DocumentLineRequest { Designation = "Conseil", Quantity = 3, UnitPriceExclTax = 100m, UnitCode = "HUR", Kind = CatalogItemKindDto.Service }],
            },
            default)).Value!;
        (await platform.GetAsync(draft.Id, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Platform.NotTransmitted);
        InvoiceResponse validated = (await invoices.ValidateAsync(draft.Id, draft.Version, default)).Value!;

        OperationResult<PlatformTransmissionResponse> sent = await platform.TransmitAsync(validated.Id, new TransmitInvoiceRequest(), default);
        sent.IsSuccess.Should().BeTrue(string.Join(", ", sent.Errors.Select(e => e.Code)));
        OperationResult<PlatformTransmissionResponse> refreshed = await platform.RefreshAsync(validated.Id, default);
        OperationResult<PlatformTransmissionResponse> paid = await platform.ReportPaymentAsync(
            validated.Id, new ReportPaymentRequest { Date = validated.IssueDate!.Value, Amount = 100m }, default);

        sent.Value!.Status.Should().Be(PlatformStatusDto.Deposited);
        sent.Value.Channel.Should().Be(PlatformChannelDto.EInvoice);
        (await invoices.GetAsync(validated.Id, default)).Value!.Status.Should().Be(InvoiceStatusDto.Issued, "le dépôt émet la pièce");
        refreshed.IsSuccess.Should().BeTrue();
        paid.Value!.CollectedAmount.Should().Be(100m);
        paid.Value.IsFinal.Should().BeFalse("360 € TTC, 100 € encaissés");
        (await platform.GetAsync(validated.Id, default)).Value!.History.Should().HaveCount(paid.Value.History.Count);

        // Le tableau de bord lit la même transmission : encaissée en partie, donc en cours, et pas encore échue.
        OperationResult<ReceivablesSummaryResponse> summary = await services.GetRequiredService<IReceivablesGateway>().GetSummaryAsync(default);
        summary.IsSuccess.Should().BeTrue(string.Join(", ", summary.Errors.Select(e => e.Code)));
        summary.Value!.InProgress.Should().BePositive();
        summary.Value.Oldest.Should().NotContain(o => o.InvoiceId == validated.Id);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Exporter_le_fec_puis_le_passer_au_validateur(string transport)
    {
        await using ServiceProvider services = Gateway(transport);
        IFecGateway fec = services.GetRequiredService<IFecGateway>();
        await SaveCompanyProfileAsync(services);
        DateOnly from = new(DateTime.Today.Year, 1, 1), to = new(DateTime.Today.Year, 12, 31);

        OperationResult<FecDocument> exported = await fec.ExportAsync(from, to, default);
        exported.IsSuccess.Should().BeTrue(string.Join(", ", exported.Errors.Select(e => e.Code)));
        OperationResult<FecValidationResponse> report = await fec.ValidateAsync(exported.Value!.FileName, exported.Value.Content, from, to, default);

        exported.Value.FileName.Should().Be(string.Create(CultureInfo.InvariantCulture, $"552100554FEC{to:yyyyMMdd}.txt"));
        report.Value!.IsValid.Should().BeTrue();
        report.Value.FileName.Should().Be(exported.Value.FileName);
        (await fec.ExportAsync(to, from, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Fec.PeriodInvalid);
    }

    private static async Task SaveCompanyProfileAsync(IServiceProvider services) =>
        (await services.GetRequiredService<IBillingSettingsGateway>().SaveCompanyProfileAsync(
            new SaveCompanyProfileRequest
            {
                Name = "Conseil Lettra", LegalForm = "SAS", ShareCapital = 10_000m, Siren = "552100554", Rcs = "RCS Paris 552 100 554",
                VatNumber = "FR96552100554", AddressLine1 = "10 avenue de l'Opéra", PostalCode = "75001", City = "Paris",
                LatePenaltyRate = 10m, EarlyPaymentDiscount = "Pas d'escompte pour paiement anticipé",
            },
            null,
            default)).IsSuccess.Should().BeTrue();

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    private sealed class BearerHandler(string token) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
