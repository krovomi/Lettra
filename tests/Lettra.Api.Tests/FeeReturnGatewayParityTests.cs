using System.Globalization;
using System.Numerics;
using System.Text;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Contracts.Tax;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Déclaration des honoraires (DAS 2) par la même interface, en HTTP et en direct : les virements des relevés vers l'IBAN
/// du bénéficiaire et les sommes notées à la main s'additionnent, le seuil de 1 200 € décide, l'échéance entre au
/// calendrier fiscal et se marque déposée.
/// </summary>
public sealed class FeeReturnGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    private static DateOnly Today => LegalCalendar.DateOf(DateTimeOffset.UtcNow);

    private static SaveFeeBeneficiaryRequest Fees(DateOnly yearStart, decimal paidOutside = 0m) =>
        new() { Nature = FeeNatureDto.Fees, Profession = "Avocat", FiscalYearStart = yearStart, PaidOutside = paidOutside };

    /// <summary>Un IBAN français valable propre au test, pour que les virements des autres tests ne s'y ajoutent pas.</summary>
    private static string UniqueIban()
    {
        string bban = "30006" + "00001" + Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(CultureInfo.InvariantCulture) + "42";
        int check = 98 - (int)(BigInteger.Parse(bban + "152700", CultureInfo.InvariantCulture) % 97);
        return $"FR{check:00}{bban}";
    }

    private static async Task<SupplierResponse> CreateSupplierAsync(ServiceProvider services, string? iban, string? siret = "73282932000074")
    {
        CreateSupplierRequest request = PartiesAndCatalogApiTests.Dupuis();
        request.Name = "Cabinet d'avocats " + Guid.CreateVersion7().ToString("N")[^8..];
        request.Iban = iban;
        request.Siret = siret;
        request.Siren = siret is null ? "732829320" : null;
        OperationResult<SupplierResponse> created = await services.GetRequiredService<ISuppliersGateway>().CreateAsync(request, default);
        created.IsSuccess.Should().BeTrue(GatewayHarness.Codes(created));
        return created.Value!;
    }

    private static string Debit(string iban, decimal amount) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.08">
          <BkToCstmrStmt>
            <GrpHdr><MsgId>TEST-{Guid.NewGuid():N}</MsgId><CreDtTm>{Today:yyyy-MM-dd}T08:00:00</CreDtTm></GrpHdr>
            <Stmt>
              <Id>TEST</Id>
              <Acct><Id><IBAN>FR7610107001011234567890129</IBAN></Id></Acct>
              <Ntry>
                <Amt Ccy="EUR">{amount.ToString("0.00", CultureInfo.InvariantCulture)}</Amt>
                <CdtDbtInd>DBIT</CdtDbtInd>
                <Sts><Cd>BOOK</Cd></Sts>
                <BookgDt><Dt>{Today:yyyy-MM-dd}</Dt></BookgDt>
                <NtryDtls><TxDtls>
                  <RltdPties><Cdtr><Pty><Nm>Cabinet d'avocats</Nm></Pty></Cdtr><CdtrAcct><Id><IBAN>{iban}</IBAN></Id></CdtrAcct></RltdPties>
                  <RmtInf><Ustrd>VIR HONORAIRES {Guid.NewGuid():N}</Ustrd></RmtInf>
                </TxDtls></NtryDtls>
              </Ntry>
            </Stmt>
          </BkToCstmrStmt>
        </Document>
        """;

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Virements_des_releves_et_sommes_notees_s_additionnent_et_l_echeance_se_marque_deposee(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        await GatewayHarness.SaveCompanyProfileAsync(services);
        IFeeReturnGateway fees = services.GetRequiredService<IFeeReturnGateway>();
        string iban = UniqueIban();
        SupplierResponse lawyer = await CreateSupplierAsync(services, iban);
        OperationResult<BankImportResponse> imported = await services.GetRequiredService<IBankingGateway>().ImportStatementAsync(
            new ImportBankStatementRequest { FileName = "releve.xml", ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(Debit(iban, 900m))) }, default);
        imported.IsSuccess.Should().BeTrue(GatewayHarness.Codes(imported));

        FeeReturnResponse before = (await fees.GetAsync(Today, default)).Value!;
        DateOnly start = before.FiscalYearStart;
        OperationResult<FeeReturnResponse> tracked = await fees.SaveAsync(lawyer.Id, Fees(start), null, default);
        FeeBeneficiaryRow below = tracked.Value!.Rows.Single(r => r.SupplierId == lawyer.Id);
        OperationResult<FeeReturnResponse> stale = await fees.SaveAsync(lawyer.Id, Fees(start, 400m), below.Version + 1, default);
        OperationResult<FeeReturnResponse> outside = await fees.SaveAsync(lawyer.Id, Fees(start, 400m), below.Version, default);
        FeeBeneficiaryRow above = outside.Value!.Rows.Single(r => r.SupplierId == lawyer.Id);

        before.Candidates.Should().Contain(c => c.SupplierId == lawyer.Id);
        Today.Should().BeOnOrAfter(start).And.BeOnOrBefore(before.FiscalYearEnd);
        tracked.IsSuccess.Should().BeTrue(GatewayHarness.Codes(tracked));
        tracked.Value.Candidates.Should().NotContain(c => c.SupplierId == lawyer.Id);
        (below.PaidByBank, below.PaidOutside, below.Total, below.ToDeclare, below.SiretMissing, below.HasIban).Should().Be((900m, 0m, 900m, false, false, true));
        below.Profession.Should().Be("Avocat");
        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        (above.PaidOutside, above.Total, above.ToDeclare).Should().Be((400m, 1_300m, true), "au-delà de 1 200 € TTC");
        FiscalDeadlineResponse deadline = outside.Value.Deadline!;
        deadline.Obligation.Should().Be(FiscalObligationDto.FeeReturnDas2);
        deadline.PeriodStart.Should().Be(start);
        deadline.DueDate.Should().BeAfter(before.FiscalYearEnd, "avec la déclaration de résultats, après la clôture");

        OperationResult<FiscalDeadlineResponse> filed = await services.GetRequiredService<ITaxGateway>().MarkFilingAsync(
            new MarkFiscalFilingRequest(FiscalObligationDto.FeeReturnDas2, start, true), default);
        filed.Value!.State.Should().Be(FiscalDeadlineStateDto.Filed);
        (await fees.GetAsync(Today, default)).Value!.Deadline!.State.Should().Be(FiscalDeadlineStateDto.Filed);
        await services.GetRequiredService<ITaxGateway>().MarkFilingAsync(new MarkFiscalFilingRequest(FiscalObligationDto.FeeReturnDas2, start, false), default);

        FeeBeneficiaryRow cleared = (await fees.SaveAsync(lawyer.Id, Fees(start), above.Version, default)).Value!.Rows.Single(r => r.SupplierId == lawyer.Id);
        (cleared.PaidOutside, cleared.Total, cleared.ToDeclare).Should().Be((0m, 900m, false), "zéro retire la somme notée");

        OperationResult<FeeReturnResponse> removed = await fees.RemoveAsync(lawyer.Id, start, cleared.Version, default);
        removed.IsSuccess.Should().BeTrue(GatewayHarness.Codes(removed));
        removed.Value!.Rows.Should().NotContain(r => r.SupplierId == lawyer.Id);
        removed.Value.Candidates.Should().Contain(c => c.SupplierId == lawyer.Id);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Les_refus_sont_les_memes(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IFeeReturnGateway fees = services.GetRequiredService<IFeeReturnGateway>();
        SupplierResponse noSiret = await CreateSupplierAsync(services, null, siret: null);
        DateOnly start = (await fees.GetAsync(Today, default)).Value!.FiscalYearStart;

        OperationResult<FeeReturnResponse> unknown = await fees.SaveAsync(Guid.CreateVersion7(), Fees(start), null, default);
        OperationResult<FeeReturnResponse> profession = await fees.SaveAsync(noSiret.Id, Fees(start) with { Profession = " " }, null, default);
        OperationResult<FeeReturnResponse> amount = await fees.SaveAsync(noSiret.Id, Fees(start, -1m), null, default);
        OperationResult<FeeReturnResponse> year = await fees.SaveAsync(noSiret.Id, Fees(start) with { FiscalYearStart = start.AddDays(1) }, null, default);
        OperationResult<FeeReturnResponse> notTracked = await fees.RemoveAsync(noSiret.Id, start, 1, default);
        OperationResult<FeeReturnResponse> saved = await fees.SaveAsync(noSiret.Id, Fees(start, 1_500m), null, default);

        unknown.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Fee.SupplierNotFound);
        profession.Errors.Should().ContainSingle().Which.Should().Match<ErrorDetail>(e => e.Code == ErrorCodes.Fee.ProfessionRequired && e.Target == "Profession");
        amount.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Fee.AmountInvalid);
        year.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Fee.YearInvalid);
        notTracked.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Fee.BeneficiaryNotFound);
        FeeBeneficiaryRow row = saved.Value!.Rows.Single(r => r.SupplierId == noSiret.Id);
        (row.SiretMissing, row.HasIban, row.PaidByBank, row.ToDeclare).Should().Be((true, false, 0m, true));
        (await fees.RemoveAsync(noSiret.Id, start, row.Version, default)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Le_client_ne_voit_pas_la_declaration_des_honoraires()
    {
        await using ServiceProvider portal = _harness.HttpGateway(LettraApiFactory.DossierId, LettraApiFactory.PortailId);
        IFeeReturnGateway fees = portal.GetRequiredService<IFeeReturnGateway>();

        (await fees.GetAsync(null, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden);
        (await fees.SaveAsync(Guid.CreateVersion7(), Fees(Today), null, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden);
    }

    [Fact]
    public async Task Serveur_injoignable_chaque_appel_rend_Api_Unreachable()
    {
        await using ServiceProvider services = GatewayHarness.Unreachable();
        IFeeReturnGateway fees = services.GetRequiredService<IFeeReturnGateway>();

        (await fees.GetAsync(null, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable);
        (await fees.SaveAsync(Guid.CreateVersion7(), Fees(Today), 1, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable);
        (await fees.RemoveAsync(Guid.CreateVersion7(), Today, 1, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable);
    }

    public void Dispose() => _harness.Dispose();
}
