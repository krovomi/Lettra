using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Contracts.Vigilance;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Attestations de vigilance par la même interface, en HTTP et en direct : mêmes résultats, mêmes refus, et le fournisseur
/// sans attestation valable passe « à contrôler » avant paiement.
/// </summary>
public sealed class AttestationGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    private static DateOnly Today => LegalCalendar.DateOf(DateTimeOffset.UtcNow);

    private static SaveAttestationRequest Received(DateOnly? verifiedOn) => new()
    {
        ContractAmountExclTax = 18_000m,
        IssuedOn = Today.AddDays(-10),
        SecurityCode = "ab12 cd34 ef56",
        VerifiedOn = verifiedOn,
    };

    private static async Task<SupplierResponse> CreateSupplierAsync(ServiceProvider services)
    {
        CreateSupplierRequest request = PartiesAndCatalogApiTests.Dupuis();
        request.Name = "Maçonnerie " + Guid.CreateVersion7().ToString("N")[^8..];
        return (await services.GetRequiredService<ISuppliersGateway>().CreateAsync(request, default)).Value!;
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Le_fournisseur_suivi_reste_a_controler_tant_que_l_attestation_n_est_pas_verifiee(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IAttestationsGateway attestations = services.GetRequiredService<IAttestationsGateway>();
        IIbanChangesGateway payees = services.GetRequiredService<IIbanChangesGateway>();
        SupplierResponse supplier = await CreateSupplierAsync(services);

        SupplierAttestationRow untracked = (await attestations.ListAsync(default)).Value!.Rows.Single(r => r.SupplierId == supplier.Id);
        OperationResult<SupplierAttestationsResponse> missing = await attestations.SaveAsync(supplier.Id, new SaveAttestationRequest { ContractAmountExclTax = 18_000m }, null, default);
        SupplierAttestationRow missingRow = missing.Value!.Rows.Single(r => r.SupplierId == supplier.Id);
        PayeeCheckRowResponse blocked = (await payees.ListPayeesAsync(new PayeeCheckListQuery(Search: supplier.Name), default)).Value!.Items.Should().ContainSingle().Subject;
        OperationResult<SupplierAttestationsResponse> stale = await attestations.SaveAsync(supplier.Id, Received(Today), missingRow.Version - 1, default);
        OperationResult<SupplierAttestationsResponse> verified = await attestations.SaveAsync(supplier.Id, Received(Today), missingRow.Version, default);
        PayeeCheckRowResponse cleared = (await payees.ListPayeesAsync(new PayeeCheckListQuery(false, supplier.Name), default)).Value!.Items.Should().ContainSingle().Subject;

        untracked.Should().Be(new SupplierAttestationRow(supplier.Id, supplier.Name, null, null, null, null, null, AttestationStatusDto.NotRequired, null));
        missingRow.Status.Should().Be(AttestationStatusDto.Missing);
        blocked.AttestationDue.Should().BeTrue();
        blocked.ToReview.Should().BeTrue();
        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        verified.IsSuccess.Should().BeTrue(GatewayHarness.Codes(verified));
        SupplierAttestationRow row = verified.Value!.Rows.Single(r => r.SupplierId == supplier.Id);
        row.Status.Should().Be(AttestationStatusDto.Valid);
        row.SecurityCode.Should().Be("AB12CD34EF56");
        row.ValidUntil.Should().Be(Today.AddDays(-10).AddMonths(6).AddDays(-1));
        cleared.AttestationDue.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Les_refus_sont_les_memes(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IAttestationsGateway attestations = services.GetRequiredService<IAttestationsGateway>();
        SupplierResponse supplier = await CreateSupplierAsync(services);

        OperationResult<SupplierAttestationsResponse> unknown = await attestations.SaveAsync(Guid.CreateVersion7(), Received(Today), null, default);
        OperationResult<SupplierAttestationsResponse> future = await attestations.SaveAsync(supplier.Id, Received(Today.AddDays(1)), null, default);
        OperationResult<SupplierAttestationsResponse> code = await attestations.SaveAsync(supplier.Id, Received(null) with { SecurityCode = "ABC" }, null, default);
        OperationResult<SupplierAttestationsResponse> noCode = await attestations.SaveAsync(supplier.Id, Received(Today) with { SecurityCode = null }, null, default);
        OperationResult<SupplierAttestationsResponse> amount = await attestations.SaveAsync(supplier.Id, Received(null) with { ContractAmountExclTax = -1m }, null, default);

        unknown.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Attestation.SupplierNotFound);
        future.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Attestation.DateInFuture);
        code.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Attestation.CodeInvalid);
        noCode.Errors.Should().ContainSingle().Which.Should().Match<ErrorDetail>(e => e.Code == ErrorCodes.Attestation.VerificationNeedsCode && e.Target == "VerifiedOn");
        amount.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Attestation.AmountInvalid);
    }

    [Fact]
    public async Task Le_client_ne_voit_pas_les_attestations()
    {
        await using ServiceProvider portal = _harness.HttpGateway(LettraApiFactory.DossierId, LettraApiFactory.PortailId);
        IAttestationsGateway attestations = portal.GetRequiredService<IAttestationsGateway>();

        (await attestations.ListAsync(default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden);
        (await attestations.SaveAsync(Guid.CreateVersion7(), Received(null), null, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden);
    }

    [Fact]
    public async Task Serveur_injoignable_chaque_appel_rend_Api_Unreachable()
    {
        await using ServiceProvider services = GatewayHarness.Unreachable();
        IAttestationsGateway attestations = services.GetRequiredService<IAttestationsGateway>();

        (await attestations.ListAsync(default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable);
        (await attestations.SaveAsync(Guid.CreateVersion7(), Received(null), 1, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable);
    }

    public void Dispose() => _harness.Dispose();
}
