using System.Net;
using Lettra.Application.Abstractions.External;
using Lettra.Contracts.Directory;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.External.Tests;

public sealed class ViesTests : IDisposable
{
    private readonly ExternalHarness _harness = new();

    private static VatNumber Vat(string value) => VatNumber.Create(value).Value;

    [Fact]
    public async Task Un_numero_valide_renvoie_le_nom_et_la_date_de_consultation()
    {
        _harness.Handler.Respond = _ => FakeHttpHandler.Json("""
            {"countryCode":"FR","vatNumber":"44732829320","requestDate":"2026-10-14T09:00:00.000Z","valid":true,
             "requestIdentifier":"","name":"SAS BOULANGERIE MARTIN","address":"12 RUE DE LA PAIX\n75002 PARIS",
             "traderName":"---","userError":"VALID"}
            """);

        Result<VatNumberCheckResponse> result = await _harness.Vies.CheckAsync(Vat("FR44732829320"), default);

        result.Value.Should().Be(new VatNumberCheckResponse(
            "FR44732829320", true, "SAS BOULANGERIE MARTIN", "12 RUE DE LA PAIX\n75002 PARIS", new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero)));
        _harness.Handler.Requests.Should().ContainSingle().Which.Body.Should().Be("""{"countryCode":"FR","vatNumber":"44732829320"}""");
    }

    [Fact]
    public async Task Un_numero_inconnu_est_une_reponse_et_les_champs_masques_sont_vides()
    {
        _harness.Handler.Respond = _ => FakeHttpHandler.Json("""
            {"countryCode":"DE","vatNumber":"123456789","requestDate":"2026-10-14T09:00:00Z","valid":false,"name":"---","address":"---","userError":"INVALID"}
            """);

        Result<VatNumberCheckResponse> result = await _harness.Vies.CheckAsync(Vat("DE123456789"), default);

        result.Value.IsValid.Should().BeFalse();
        result.Value.Name.Should().BeNull();
        result.Value.Address.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"countryCode":"FR","vatNumber":"44732829320","valid":false,"userError":"MS_UNAVAILABLE"}""")]
    [InlineData("""{"actionSucceed":false,"errorWrappers":[{"error":"MS_MAX_CONCURRENT_REQ","message":"..."}]}""")]
    public async Task Un_etat_membre_indisponible_n_est_pas_un_numero_invalide(string body)
    {
        _harness.Handler.Respond = _ => FakeHttpHandler.Json(body);

        (await _harness.Vies.CheckAsync(Vat("FR44732829320"), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(DirectoryErrors.UnavailableCode);
    }

    [Fact]
    public async Task Une_page_d_erreur_du_service_donne_un_annuaire_indisponible()
    {
        _harness.Handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("<html>Service Unavailable</html>"),
        };

        (await _harness.Vies.CheckAsync(Vat("FR44732829320"), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(DirectoryErrors.UnavailableCode);
    }

    public void Dispose() => _harness.Dispose();
}
