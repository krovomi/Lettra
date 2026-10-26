using System.Net;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Nom et IBAN vérifiés par la même interface, en HTTP et en direct : la banque simulée répond, la fiche garde sa
/// réponse, la liste des bénéficiaires à contrôler la reprend avec le contrôle local du titulaire.
/// </summary>
public sealed class PayeeCheckGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task La_banque_simulee_repond_et_la_fiche_comme_la_liste_gardent_sa_reponse(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        ISuppliersGateway suppliers = services.GetRequiredService<ISuppliersGateway>();
        IIbanChangesGateway changes = services.GetRequiredService<IIbanChangesGateway>();
        CreateSupplierRequest request = PartiesAndCatalogApiTests.Dupuis();
        request.Name = "Meunerie " + Guid.CreateVersion7().ToString("N")[^8..];
        request.AccountHolderName = "Minoterie Dupuis";
        SupplierResponse created = (await suppliers.CreateAsync(request, default)).Value!;
        created.AccountHolderName.Should().Be("Minoterie Dupuis");
        created.HolderNameMatch.Should().Be(PayeeMatchDto.NoMatch);
        created.PayeeCheck.Should().BeNull();

        OperationResult<PayeeCheckResponse> verified = await suppliers.VerifyPayeeAsync(created.Id, default);

        verified.Value!.Result.Should().Be(PayeeMatchDto.CloseMatch);
        verified.Value.SuggestedName.Should().Be("Minoterie Dupuy");
        (await suppliers.GetAsync(created.Id, default)).Value!.PayeeCheck.Should().Be(verified.Value);
        PayeeCheckRowResponse row = (await changes.ListPayeesAsync(new PayeeCheckListQuery(Search: request.Name), default)).Value!.Items.Should().ContainSingle().Subject;
        row.Should().BeEquivalentTo(new PayeeCheckRowResponse(
            created.Id, request.Name, "Minoterie Dupuis", "FR76 •••• 0189", PayeeMatchDto.NoMatch, verified.Value, false, true));
        (await changes.ListPayeesAsync(new PayeeCheckListQuery(false, request.Name, Sort: PayeeCheckSortDto.Name, Direction: SortDirectionDto.Descending), default))
            .Value!.TotalCount.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Sans_iban_ou_inconnu_la_verification_est_refusee(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        ISuppliersGateway suppliers = services.GetRequiredService<ISuppliersGateway>();
        CreateSupplierRequest request = PartiesAndCatalogApiTests.Dupuis();
        request.Iban = null;
        SupplierResponse created = (await suppliers.CreateAsync(request, default)).Value!;

        (await suppliers.VerifyPayeeAsync(created.Id, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Supplier.IbanMissing);
        (await suppliers.VerifyPayeeAsync(Guid.CreateVersion7(), default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Supplier.NotFound);
    }

    [Fact]
    public async Task Verifier_exige_le_droit_de_modifier_les_tiers()
    {
        using HttpClient reader = factory.ClientFor(LettraApiFactory.PortailId);
        reader.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());

        using HttpResponseMessage response = await reader.PostAsync(new Uri($"/api/v1/suppliers/{Guid.CreateVersion7()}/payee-verification", UriKind.Relative), null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    public void Dispose() => _harness.Dispose();
}
