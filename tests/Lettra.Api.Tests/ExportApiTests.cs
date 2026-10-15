using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>
/// FEC, facture électronique et transmission à la plateforme agréée simulée par l'API réelle (mode 2, SQLite) :
/// validateur intégré, permissions, erreurs.
/// </summary>
public sealed class ExportApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private const string Header =
        "JournalCode\tJournalLib\tEcritureNum\tEcritureDate\tCompteNum\tCompteLib\tCompAuxNum\tCompAuxLib\tPieceRef\tPieceDate\tEcritureLib\tDebit\tCredit\tEcritureLet\tDateLet\tValidDate\tMontantdevise\tIdevise";

    private static Uri Api(string path) => new(path, UriKind.Relative);

    private static HttpClient InDossier(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    [Fact]
    public async Task Le_validateur_lit_un_fec_d_un_autre_logiciel()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        string fec = Header + "\r\n"
            + "AC\tAchats\t1\t20260110\t607\tAchats\t\t\tF12\t20260110\tFournisseur fictif\t100,00\t0,00\t\t\t20260110\t\t\r\n"
            + "AC\tAchats\t1\t20260110\t401\tFournisseurs\tF001\tFournisseur fictif\tF12\t20260110\tFournisseur fictif\t0,00\t90,00\t\t\t20260110\t\t\r\n";

        HttpResponseMessage response = await manager.PostAsync(
            Api("/api/v1/exports/fec/validation?fileName=552100554FEC20261231.txt&from=2026-01-01&to=2026-12-31"),
            new ByteArrayContent(Encoding.UTF8.GetBytes(fec)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        FecValidationResponse report = (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.FecValidationResponse))!;
        report.IsValid.Should().BeFalse();
        report.EntryCount.Should().Be(1);
        report.Findings.Select(f => f.Code).Should().Equal("Fec.EntryUnbalanced", "Fec.FileUnbalanced");
    }

    [Fact]
    public async Task Un_exercice_incoherent_est_refuse()
    {
        using HttpClient manager = InDossier(factory.CreateClient());

        HttpResponseMessage response = await manager.GetAsync(Api("/api/v1/exports/fec?from=2026-12-31&to=2026-01-01"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Fec.PeriodInvalid");
    }

    [Fact]
    public async Task L_export_exige_sa_permission()
    {
        using HttpClient collaborator = InDossier(factory.ClientFor(LettraApiFactory.CollaborateurId));

        (await collaborator.GetAsync(Api("/api/v1/exports/fec?from=2026-01-01&to=2026-12-31"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task La_facture_electronique_d_une_piece_inconnue_est_introuvable()
    {
        using HttpClient manager = InDossier(factory.CreateClient());

        HttpResponseMessage response = await manager.GetAsync(Api($"/api/v1/invoices/{Guid.CreateVersion7()}/e-invoice?format=Cii"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Un_format_de_facture_electronique_inconnu_est_refuse()
    {
        using HttpClient manager = InDossier(factory.CreateClient());

        HttpResponseMessage response = await manager.GetAsync(Api($"/api/v1/invoices/{Guid.CreateVersion7()}/e-invoice?format=9"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.EInvoice.FormatUnknown);
    }

    [Fact]
    public async Task Une_piece_validee_est_transmise_a_la_plateforme_simulee_puis_emise()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        Guid invoice = await ValidatedInvoiceAsync(manager);

        using HttpResponseMessage sent = await manager.PostAsJsonAsync(
            Api($"/api/v1/invoices/{invoice}/platform-transmission"), new TransmitInvoiceRequest { Format = EInvoiceFormatDto.Ubl }, ContractsJsonContext.Default.TransmitInvoiceRequest);
        sent.StatusCode.Should().Be(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        PlatformTransmissionResponse transmission = (await sent.Content.ReadFromJsonAsync(ContractsJsonContext.Default.PlatformTransmissionResponse))!;
        transmission.Status.Should().Be(PlatformStatusDto.Deposited);
        transmission.Format.Should().Be(EInvoiceFormatDto.Ubl);
        (await sent.Content.ReadAsStringAsync()).Should().Contain("\"Deposited\"");

        using HttpResponseMessage refreshed = await manager.PostAsync(Api($"/api/v1/invoices/{invoice}/platform-transmission/refresh"), null);
        (await refreshed.Content.ReadFromJsonAsync(ContractsJsonContext.Default.PlatformTransmissionResponse))!.Status.Should().Be(PlatformStatusDto.MadeAvailable);

        using HttpResponseMessage paid = await manager.PostAsJsonAsync(
            Api($"/api/v1/invoices/{invoice}/platform-transmission/payments"),
            new ReportPaymentRequest { Date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), Amount = 120m },
            ContractsJsonContext.Default.ReportPaymentRequest);
        paid.StatusCode.Should().Be(HttpStatusCode.OK, await paid.Content.ReadAsStringAsync());
        PlatformTransmissionResponse collected = (await manager.GetFromJsonAsync(
            Api($"/api/v1/invoices/{invoice}/platform-transmission"), ContractsJsonContext.Default.PlatformTransmissionResponse))!;
        collected.Status.Should().Be(PlatformStatusDto.Collected);
        collected.IsFinal.Should().BeTrue();
        (await manager.GetFromJsonAsync(Api($"/api/v1/invoices/{invoice}"), ContractsJsonContext.Default.InvoiceResponse))!
            .Status.Should().Be(InvoiceStatusDto.Issued);
    }

    [Fact]
    public async Task Une_piece_jamais_transmise_n_a_pas_de_transmission()
    {
        using HttpClient manager = InDossier(factory.CreateClient());

        HttpResponseMessage response = await manager.GetAsync(Api($"/api/v1/invoices/{Guid.CreateVersion7()}/platform-transmission"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Platform.NotTransmitted);
    }

    [Fact]
    public async Task Transmettre_exige_la_permission_d_emettre()
    {
        using HttpClient collaborator = InDossier(factory.ClientFor(LettraApiFactory.CollaborateurId));

        (await collaborator.PostAsync(Api($"/api/v1/invoices/{Guid.CreateVersion7()}/platform-transmission"), null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<Guid> ValidatedInvoiceAsync(HttpClient client)
    {
        using HttpRequestMessage profile = new(HttpMethod.Put, Api("/api/v1/company-profile"))
        {
            Content = JsonContent.Create(
                new SaveCompanyProfileRequest
                {
                    Name = "Conseil Lettra", LegalForm = "SAS", ShareCapital = 10_000m, Siren = "552100554", Rcs = "RCS Paris 552 100 554",
                    VatNumber = "FR96552100554", AddressLine1 = "10 avenue de l'Opéra", PostalCode = "75001", City = "Paris",
                    LatePenaltyRate = 10m, EarlyPaymentDiscount = "Pas d'escompte pour paiement anticipé",
                },
                ContractsJsonContext.Default.SaveCompanyProfileRequest),
        };
        profile.Headers.IfMatch.Add(EntityTagHeaderValue.Any);
        (await client.SendAsync(profile)).IsSuccessStatusCode.Should().BeTrue();

        using HttpResponseMessage customer = await client.PostAsJsonAsync(Api("/api/v1/customers"), CustomerApiTests.Martin(), ContractsJsonContext.Default.CreateCustomerRequest);
        Guid customerId = (await customer.Content.ReadFromJsonAsync(ContractsJsonContext.Default.CustomerResponse))!.Id;
        using HttpResponseMessage draft = await client.PostAsJsonAsync(
            Api("/api/v1/invoices"),
            new SaveInvoiceRequest
            {
                CustomerId = customerId,
                Lines = [new DocumentLineRequest { Designation = "Conseil", Quantity = 1, UnitPriceExclTax = 100m, UnitCode = "HUR", Kind = CatalogItemKindDto.Service }],
            },
            ContractsJsonContext.Default.SaveInvoiceRequest);
        draft.StatusCode.Should().Be(HttpStatusCode.Created, await draft.Content.ReadAsStringAsync());
        Guid invoice = (await draft.Content.ReadFromJsonAsync(ContractsJsonContext.Default.InvoiceResponse))!.Id;

        using HttpRequestMessage validate = new(HttpMethod.Post, Api($"/api/v1/invoices/{invoice}/validate"));
        validate.Headers.IfMatch.Add(EntityTagHeaderValue.Any);
        using HttpResponseMessage validated = await client.SendAsync(validate);
        validated.StatusCode.Should().Be(HttpStatusCode.OK, await validated.Content.ReadAsStringAsync());
        return invoice;
    }
}
