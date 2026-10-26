using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Serialization;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Comptes intra-groupe : deux dossiers du cabinet dont l'un facture l'autre forment un couple ; le virement de l'acheteur
/// est reconnu par l'IBAN du vendeur et le numéro de la facture, et seuls les dossiers lisibles sont rapprochés.
/// </summary>
public sealed class IntercompanyApiTests
{
    private const string SellerIban = "FR7630006000011234567890189";
    private static readonly Uri Intercompany = new("/api/v1/dossiers/intercompany", UriKind.Relative);

    [Fact]
    public async Task Le_vendeur_facture_la_filiale_dont_la_banque_paie_sans_fiche_fournisseur()
    {
        await using LettraApiFactory api = new();
        using GatewayHarness harness = new(api);
        Guid buyer = await CreateDossierAsync(api, "Boulangerie Martin");

        InvoiceResponse invoice;
        await using (ServiceProvider seller = harness.HttpGateway(LettraApiFactory.DossierId))
        {
            SaveCompanyProfileRequest profile = GatewayHarness.CompanyProfile();
            profile.Iban = SellerIban;
            (await seller.GetRequiredService<IBillingSettingsGateway>().SaveCompanyProfileAsync(profile, null, default)).IsSuccess.Should().BeTrue();
            Guid customer = (await seller.GetRequiredService<ICustomersGateway>().CreateAsync(CustomerApiTests.Martin(), default)).Value!.Id;
            IInvoicesGateway invoices = seller.GetRequiredService<IInvoicesGateway>();
            InvoiceResponse draft = (await invoices.CreateAsync(new SaveInvoiceRequest { CustomerId = customer, Lines = [GatewayHarness.Conseil()] }, default)).Value!;
            invoice = (await invoices.ValidateAsync(draft.Id, draft.Version, default)).Value!;
        }

        await using (ServiceProvider bought = harness.HttpGateway(buyer))
        {
            SaveCompanyProfileRequest profile = GatewayHarness.CompanyProfile();
            (profile.Name, profile.Siren, profile.VatNumber, profile.Rcs) = ("Boulangerie Martin", "732829320", "FR44732829320", null);
            OperationResult<CompanyProfileResponse> saved = await bought.GetRequiredService<IBillingSettingsGateway>().SaveCompanyProfileAsync(profile, null, default);
            saved.IsSuccess.Should().BeTrue(GatewayHarness.Codes(saved));
            ImportBankStatementRequest statement = new()
            {
                FileName = "releve-filiale.xml",
                ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(Debit(Today, invoice.Totals.TotalInclTax, $"VIR CONSEIL LETTRA {invoice.Number}"))),
            };
            OperationResult<BankImportResponse> imported = await bought.GetRequiredService<IBankingGateway>().ImportStatementAsync(statement, default);
            imported.IsSuccess.Should().BeTrue(GatewayHarness.Codes(imported));
        }

        IntercompanyResponse response = (await api.ClientFor(LettraApiFactory.ManagerId).GetFromJsonAsync(Intercompany, ContractsJsonContext.Default.IntercompanyResponse))!;

        response.Dossiers.Should().BeGreaterThanOrEqualTo(2);
        IntercompanyPairResponse pair = response.Pairs.Should().ContainSingle().Subject;
        (pair.SellerDossierId, pair.BuyerDossierId, pair.BuyerName).Should().Be((LettraApiFactory.DossierId, buyer, "Boulangerie Martin"));
        pair.BuyerBankLinked.Should().BeTrue();
        (pair.InvoicedInclTax, pair.PaidBySeller, pair.PaidByBuyer).Should().Be((invoice.Totals.TotalInclTax, 0m, invoice.Totals.TotalInclTax));
        pair.Gaps.Should().Equal(IntercompanyGapDto.SupplierMissing, IntercompanyGapDto.SeenNotPaid, IntercompanyGapDto.TotalsDiffer);
        pair.Invoices.Should().ContainSingle().Which.Should().Match<IntercompanyInvoiceResponse>(i => i.Number == invoice.Number && i.SeenAtBuyer);

        // Le collaborateur ne lit que le premier dossier : aucun couple.
        IntercompanyResponse collaborateur = (await api.ClientFor(LettraApiFactory.CollaborateurId).GetFromJsonAsync(Intercompany, ContractsJsonContext.Default.IntercompanyResponse))!;
        collaborateur.Pairs.Should().BeEmpty();
    }

    [Fact]
    public async Task Sans_jeton_c_est_401()
    {
        await using LettraApiFactory api = new();
        using HttpClient anonymous = api.CreateClient();
        anonymous.DefaultRequestHeaders.Authorization = null;

        (await anonymous.GetAsync(Intercompany)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static DateOnly Today => LegalCalendar.DateOf(DateTimeOffset.UtcNow);

    private static async Task<Guid> CreateDossierAsync(LettraApiFactory api, string name)
    {
        using HttpClient admin = api.ClientFor(LettraApiFactory.AdministrateurCabinetId);
        using HttpResponseMessage created = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/cabinets/{LettraApiFactory.CabinetId}/dossiers", UriKind.Relative),
            new CreateDossierRequest { Name = name },
            ContractsJsonContext.Default.CreateDossierRequest);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await created.Content.ReadFromJsonAsync(ContractsJsonContext.Default.DossierSummary))!.Id;
    }

    /// <summary>Un relevé de l'acheteur : un virement vers l'IBAN du vendeur.</summary>
    private static string Debit(DateOnly day, decimal amount, string label) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.08">
          <BkToCstmrStmt>
            <GrpHdr><MsgId>TEST-{Guid.NewGuid():N}</MsgId><CreDtTm>{day:yyyy-MM-dd}T08:00:00</CreDtTm></GrpHdr>
            <Stmt>
              <Id>TEST</Id>
              <Acct><Id><IBAN>FR7610107001011234567890129</IBAN></Id></Acct>
              <Ntry>
                <Amt Ccy="EUR">{amount.ToString("0.00", CultureInfo.InvariantCulture)}</Amt>
                <CdtDbtInd>DBIT</CdtDbtInd>
                <Sts><Cd>BOOK</Cd></Sts>
                <BookgDt><Dt>{day:yyyy-MM-dd}</Dt></BookgDt>
                <NtryDtls><TxDtls>
                  <RltdPties><Cdtr><Pty><Nm>Conseil Lettra</Nm></Pty></Cdtr><CdtrAcct><Id><IBAN>{SellerIban}</IBAN></Id></CdtrAcct></RltdPties>
                  <RmtInf><Ustrd>{label}</Ustrd></RmtInf>
                </TxDtls></NtryDtls>
              </Ntry>
            </Stmt>
          </BkToCstmrStmt>
        </Document>
        """;
}
