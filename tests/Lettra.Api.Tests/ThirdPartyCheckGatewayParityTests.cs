using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Contracts.Vigilance;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Vérifier les tiers et le code des attestations par la même interface, en HTTP et en direct, sur les registres simulés :
/// mêmes résultats, mêmes refus.
/// </summary>
public sealed class ThirdPartyCheckGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    private static DateOnly Today => LegalCalendar.DateOf(DateTimeOffset.UtcNow);

    private static SaveAttestationRequest Received(string code) => new()
    {
        ContractAmountExclTax = 18_000m,
        IssuedOn = Today.AddDays(-10),
        SecurityCode = code,
    };

    private static async Task<SupplierResponse> CreateSupplierAsync(ServiceProvider services)
    {
        CreateSupplierRequest request = PartiesAndCatalogApiTests.Dupuis();
        request.Name = "Minoterie " + Guid.CreateVersion7().ToString("N")[^8..];
        return (await services.GetRequiredService<ISuppliersGateway>().CreateAsync(request, default)).Value!;
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Un_fournisseur_verifie_aux_registres_puis_son_code_urssaf(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IThirdPartyChecksGateway checks = services.GetRequiredService<IThirdPartyChecksGateway>();
        IAttestationsGateway attestations = services.GetRequiredService<IAttestationsGateway>();
        SupplierResponse supplier = await CreateSupplierAsync(services);
        await attestations.SaveAsync(supplier.Id, Received("ZZ12CD34EF56"), null, default);

        ThirdPartyCheckRow never = (await checks.ListAsync(default)).Value!.Rows.Single(r => r.PartyId == supplier.Id);
        OperationResult<ThirdPartyCheckRow> flagged = await checks.CheckAsync(ThirdPartyKindDto.Supplier, supplier.Id, default);
        ThirdPartyCheckRow listed = (await checks.ListAsync(default)).Value!.Rows.Single(r => r.PartyId == supplier.Id);
        OperationResult<AttestationVerificationResponse> unknownCode = await attestations.VerifyAsync(supplier.Id, default);
        SupplierAttestationRow saved = (await attestations.ListAsync(default)).Value!.Rows.Single(r => r.SupplierId == supplier.Id);
        await attestations.SaveAsync(supplier.Id, Received("AB12CD34EF56"), saved.Version, default);
        OperationResult<AttestationVerificationResponse> recognized = await attestations.VerifyAsync(supplier.Id, default);
        OperationResult<ThirdPartyCheckRow> cleared = await checks.CheckAsync(ThirdPartyKindDto.Supplier, supplier.Id, default);

        never.Should().Be(new ThirdPartyCheckRow(ThirdPartyKindDto.Supplier, supplier.Id, supplier.Name, "732829320", ThirdPartyCheckStatusDto.NeverChecked,
            null, null, null, null, null, null, null, null));
        flagged.IsSuccess.Should().BeTrue(GatewayHarness.Codes(flagged));
        flagged.Value!.Status.Should().Be(ThirdPartyCheckStatusDto.ToReview);
        flagged.Value.Urssaf.Should().Be(UrssafAnswerDto.NotRecognized);
        flagged.Value.Freeze.Should().Be(FreezeScreeningDto.Clear);
        flagged.Value.OwnersFound.Should().Be(1);
        flagged.Value.Notice.Should().BeNull();
        listed.Should().Be(flagged.Value);
        unknownCode.Value!.Answer.Should().Be(UrssafAnswerDto.NotRecognized);
        unknownCode.Value.Attestations.Rows.Single(r => r.SupplierId == supplier.Id).VerifiedOn.Should().BeNull();
        recognized.Value!.Answer.Should().Be(UrssafAnswerDto.Recognized);
        recognized.Value.Attestations.Rows.Single(r => r.SupplierId == supplier.Id).Status.Should().Be(AttestationStatusDto.Valid);
        cleared.Value!.Status.Should().Be(ThirdPartyCheckStatusDto.Clear);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Les_refus_sont_les_memes(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IThirdPartyChecksGateway checks = services.GetRequiredService<IThirdPartyChecksGateway>();
        IAttestationsGateway attestations = services.GetRequiredService<IAttestationsGateway>();
        SupplierResponse supplier = await CreateSupplierAsync(services);

        OperationResult<ThirdPartyCheckRow> unknownCustomer = await checks.CheckAsync(ThirdPartyKindDto.Customer, Guid.CreateVersion7(), default);
        OperationResult<ThirdPartyCheckRow> wrongKind = await checks.CheckAsync(ThirdPartyKindDto.Customer, supplier.Id, default);
        OperationResult<AttestationVerificationResponse> noCode = await attestations.VerifyAsync(supplier.Id, default);
        OperationResult<AttestationVerificationResponse> unknownSupplier = await attestations.VerifyAsync(Guid.CreateVersion7(), default);
        OperationResult<RegisteredOwnersResponse> owners = await services.GetRequiredService<IVigilanceGateway>().LookupOwnersAsync(default);

        unknownCustomer.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.ThirdPartyCheck.PartyNotFound);
        wrongKind.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.ThirdPartyCheck.PartyNotFound);
        noCode.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Attestation.VerificationNeedsCode);
        unknownSupplier.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Attestation.SupplierNotFound);
        owners.IsSuccess.Should().BeTrue(GatewayHarness.Codes(owners));
        if (owners.Value!.Siren is null)
        {
            owners.Value.Owners.Should().BeEmpty();
        }
        else
        {
            owners.Value.Owners.Should().NotBeEmpty().And.OnlyContain(o => o.Kind == OwnershipKindDto.Capital);
        }
    }

    [Fact]
    public async Task Le_client_ne_verifie_pas_les_tiers()
    {
        await using ServiceProvider portal = _harness.HttpGateway(LettraApiFactory.DossierId, LettraApiFactory.PortailId);

        (await portal.GetRequiredService<IThirdPartyChecksGateway>().ListAsync(default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden);
        (await portal.GetRequiredService<IThirdPartyChecksGateway>().CheckAsync(ThirdPartyKindDto.Supplier, Guid.CreateVersion7(), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden);
        (await portal.GetRequiredService<IAttestationsGateway>().VerifyAsync(Guid.CreateVersion7(), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden);
        (await portal.GetRequiredService<IVigilanceGateway>().LookupOwnersAsync(default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden);
    }

    [Fact]
    public async Task Serveur_injoignable_chaque_appel_rend_Api_Unreachable()
    {
        await using ServiceProvider services = GatewayHarness.Unreachable();

        List<IReadOnlyList<ErrorDetail>> failures =
        [
            (await services.GetRequiredService<IThirdPartyChecksGateway>().ListAsync(default)).Errors,
            (await services.GetRequiredService<IThirdPartyChecksGateway>().CheckAsync(ThirdPartyKindDto.Customer, Guid.CreateVersion7(), default)).Errors,
            (await services.GetRequiredService<IAttestationsGateway>().VerifyAsync(Guid.CreateVersion7(), default)).Errors,
            (await services.GetRequiredService<IVigilanceGateway>().LookupOwnersAsync(default)).Errors,
        ];

        failures.Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable));
    }

    public void Dispose() => _harness.Dispose();
}
