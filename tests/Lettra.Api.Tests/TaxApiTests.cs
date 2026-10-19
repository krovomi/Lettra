using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Tax;

namespace Lettra.Api.Tests;

/// <summary>
/// Déclarations par l'API réelle (mode 2, SQLite) : brouillon de CA3 relié au journal, DES, calendrier fiscal, réglages
/// et dépôts marqués, refus sans régime et sans permission.
/// </summary>
public sealed class TaxApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static Uri Api(string path) => new(path, UriKind.Relative);

    private static HttpClient InDossier(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task SetRegimeAsync(HttpClient client, VatDeclarationRegimeDto? regime)
    {
        CompanyProfileResponse current = (await client.GetFromJsonAsync(Api("/api/v1/company-profile"), ContractsJsonContext.Default.CompanyProfileResponse))!;
        using HttpRequestMessage profile = new(HttpMethod.Put, Api("/api/v1/company-profile"))
        {
            Content = JsonContent.Create(
                new SaveCompanyProfileRequest
                {
                    Name = current.Name,
                    LegalForm = current.LegalForm,
                    ShareCapital = current.ShareCapital,
                    Siren = current.Siren,
                    Rcs = current.Rcs,
                    VatNumber = current.VatNumber,
                    AddressLine1 = current.AddressLine1,
                    PostalCode = current.PostalCode,
                    City = current.City,
                    LatePenaltyRate = current.LatePenaltyRate,
                    EarlyPaymentDiscount = current.EarlyPaymentDiscount,
                    VatDeclarationRegime = regime,
                },
                ContractsJsonContext.Default.SaveCompanyProfileRequest),
        };
        profile.Headers.IfMatch.Add(EntityTagHeaderValue.Any);
        using HttpResponseMessage saved = await client.SendAsync(profile);
        saved.IsSuccessStatusCode.Should().BeTrue(await saved.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Le_brouillon_de_ca3_reprend_la_facture_validee_et_s_accorde_au_journal()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        Guid invoiceId = await ExportApiTests.ValidatedInvoiceAsync(manager);
        await SetRegimeAsync(manager, VatDeclarationRegimeDto.RealNormal);

        Uri draft = Api($"/api/v1/tax/vat-return?date={Today:yyyy-MM-dd}");
        VatReturnResponse unpaid = (await manager.GetFromJsonAsync(draft, ContractsJsonContext.Default.VatReturnResponse))!;
        unpaid.Boxes.SelectMany(b => b.Sources).Should().NotContain(s => s.InvoiceId == invoiceId, "la TVA d'une prestation est due à l'encaissement");
        unpaid.Checks.Single(c => c.Code == "JournalMatch").Severity.Should().Be(TaxCheckSeverityDto.Ok, "la TVA en attente est en 44574");

        InvoiceResponse invoice = (await manager.GetFromJsonAsync(Api($"/api/v1/invoices/{invoiceId}"), ContractsJsonContext.Default.InvoiceResponse))!;
        using HttpResponseMessage paid = await manager.PostAsJsonAsync(
            Api("/api/v1/payments"),
            new RecordPaymentRequest { CustomerId = invoice.CustomerId, Date = Today, Amount = 120m, Allocations = [new PaymentAllocationRequest(invoiceId, 120m)] },
            ContractsJsonContext.Default.RecordPaymentRequest);
        paid.StatusCode.Should().Be(HttpStatusCode.Created, await paid.Content.ReadAsStringAsync());

        VatReturnResponse vat = (await manager.GetFromJsonAsync(draft, ContractsJsonContext.Default.VatReturnResponse))!;

        vat.Form.Should().Be(VatReturnFormDto.Ca3);
        vat.From.Should().Be(new DateOnly(Today.Year, Today.Month, 1));
        VatReturnBoxResponse rate20 = vat.Boxes.Single(b => b.Key == "Rate20");
        rate20.Line.Should().Be("08");
        rate20.Sources.Should().Contain(s => s.InvoiceId == invoiceId && s.BaseExclTax == 100m && s.VatAmount == 20m);
        vat.GrossVat.Should().Be(rate20.VatAmount!.Value);
        vat.Checks.Single(c => c.Code == "JournalMatch").Severity.Should().Be(TaxCheckSeverityDto.Ok, "la TVA du brouillon est celle du 44571");
        vat.Checks.Should().Contain(c => c.Code == "DeductibleMissing");
    }

    [Fact]
    public async Task Les_reglages_pilotent_le_calendrier_et_un_depot_marque_se_retrouve()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        await ExportApiTests.ValidatedInvoiceAsync(manager);
        await SetRegimeAsync(manager, VatDeclarationRegimeDto.RealNormal);

        FiscalSettingsResponse settings = (await manager.GetFromJsonAsync(Api("/api/v1/tax/settings"), ContractsJsonContext.Default.FiscalSettingsResponse))!;
        using HttpResponseMessage saved = await manager.PutAsJsonAsync(Api("/api/v1/tax/settings"),
            new UpdateFiscalSettingsRequest(12, 31, VatPeriodicityDto.Monthly, 19, true, false, settings.Version), ContractsJsonContext.Default.UpdateFiscalSettingsRequest);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        FiscalSettingsResponse after = (await saved.Content.ReadFromJsonAsync(ContractsJsonContext.Default.FiscalSettingsResponse))!;
        after.Version.Should().Be(settings.Version + 1);
        after.TrackedFrom.Should().NotBeNull();

        using HttpResponseMessage stale = await manager.PutAsJsonAsync(Api("/api/v1/tax/settings"),
            new UpdateFiscalSettingsRequest(12, 31, VatPeriodicityDto.Monthly, 19, true, false, settings.Version), ContractsJsonContext.Default.UpdateFiscalSettingsRequest);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        int year = Today.Year;
        FiscalCalendarResponse calendar = (await manager.GetFromJsonAsync(
            Api($"/api/v1/tax/calendar?from={year}-01-01&to={year}-12-31"), ContractsJsonContext.Default.FiscalCalendarResponse))!;
        List<FiscalDeadlineResponse> ca3 = [.. calendar.Deadlines.Where(d => d.Obligation == FiscalObligationDto.VatReturnCa3)];
        ca3.Should().HaveCount(12).And.OnlyContain(d => d.DueDate.Day == 19);
        calendar.Deadlines.Should().NotContain(d => d.Obligation == FiscalObligationDto.Cfe, "la CFE est décochée");

        FiscalDeadlineResponse target = ca3[^1];
        using HttpResponseMessage marked = await manager.PutAsJsonAsync(Api("/api/v1/tax/calendar/filings"),
            new MarkFiscalFilingRequest(FiscalObligationDto.VatReturnCa3, target.PeriodStart, true), ContractsJsonContext.Default.MarkFiscalFilingRequest);
        marked.StatusCode.Should().Be(HttpStatusCode.OK, await marked.Content.ReadAsStringAsync());
        (await marked.Content.ReadFromJsonAsync(ContractsJsonContext.Default.FiscalDeadlineResponse))!.State.Should().Be(FiscalDeadlineStateDto.Filed);

        using HttpResponseMessage unknown = await manager.PutAsJsonAsync(Api("/api/v1/tax/calendar/filings"),
            new MarkFiscalFilingRequest(FiscalObligationDto.VatReturnCa3, target.PeriodStart.AddDays(3), true), ContractsJsonContext.Default.MarkFiscalFilingRequest);
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Tax.DeadlineUnknown);

        (await manager.GetAsync(Api("/api/v1/tax/calendar/upcoming?days=7"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task La_des_d_un_mois_sans_prestation_intracommunautaire_est_vide_et_son_fichier_aussi()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        await ExportApiTests.ValidatedInvoiceAsync(manager);

        DesResponse des = (await manager.GetFromJsonAsync(Api($"/api/v1/tax/des?year={Today.Year}&month={Today.Month}"), ContractsJsonContext.Default.DesResponse))!;
        des.Lines.Should().BeEmpty("le client de test est en France");

        using HttpResponseMessage file = await manager.GetAsync(Api($"/api/v1/tax/des/file?year={Today.Year}&month={Today.Month}"));
        file.StatusCode.Should().Be(HttpStatusCode.OK);
        file.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        (await file.Content.ReadAsStringAsync()).Should().StartWith("ligne;numero_tva_client;montant_eur;periode");
    }

    [Fact]
    public async Task Sans_regime_de_tva_le_brouillon_dit_quoi_completer()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        await ExportApiTests.ValidatedInvoiceAsync(manager);
        await SetRegimeAsync(manager, null);

        HttpResponseMessage response = await manager.GetAsync(Api($"/api/v1/tax/vat-return?date={Today:yyyy-MM-dd}"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Tax.RegimeMissing);
    }

    [Fact]
    public async Task Un_collaborateur_consulte_mais_ne_change_ni_les_reglages_ni_les_depots()
    {
        using HttpClient collaborator = InDossier(factory.ClientFor(LettraApiFactory.CollaborateurId));

        (await collaborator.GetAsync(Api("/api/v1/tax/settings"))).StatusCode.Should().Be(HttpStatusCode.OK);
        using HttpResponseMessage settings = await collaborator.PutAsJsonAsync(Api("/api/v1/tax/settings"),
            new UpdateFiscalSettingsRequest(12, 31, VatPeriodicityDto.Monthly, 15, true, true, 0), ContractsJsonContext.Default.UpdateFiscalSettingsRequest);
        settings.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using HttpResponseMessage filing = await collaborator.PutAsJsonAsync(Api("/api/v1/tax/calendar/filings"),
            new MarkFiscalFilingRequest(FiscalObligationDto.Cfe, new DateOnly(Today.Year, 1, 1), true), ContractsJsonContext.Default.MarkFiscalFilingRequest);
        filing.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
