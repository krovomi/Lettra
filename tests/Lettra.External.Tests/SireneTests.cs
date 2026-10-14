using System.Net;
using Lettra.Application.Abstractions.External;
using Lettra.Contracts.Directory;
using Lettra.Domain.Common;

namespace Lettra.External.Tests;

public sealed class SireneTests : IDisposable
{
    // Extrait d'une réponse réelle de GET /search de l'API Recherche d'entreprises (champs inutiles retirés).
    private const string Martin = """
        {
          "results": [
            {
              "siren": "732829320",
              "nom_complet": "BOULANGERIE MARTIN (MARTIN ET FILS)",
              "nom_raison_sociale": "BOULANGERIE MARTIN",
              "activite_principale": "10.71C",
              "etat_administratif": "A",
              "siege": {
                "siret": "73282932000074",
                "numero_voie": "12",
                "indice_repetition": "B",
                "type_voie": "RUE",
                "libelle_voie": "DE LA PAIX",
                "complement_adresse": "BATIMENT A",
                "code_postal": "75002",
                "libelle_commune": "PARIS",
                "cedex": null,
                "libelle_cedex": null,
                "code_pays_etranger": null,
                "etat_administratif": "A"
              }
            },
            {
              "siren": "123456789",
              "nom_complet": "SIREN INVALIDE",
              "siege": null
            },
            {
              "siren": "552100554",
              "nom_complet": "ANCIENNE SOCIETE",
              "nom_raison_sociale": null,
              "etat_administratif": "C",
              "siege": { "siret": "55210055400013", "code_postal": "69001", "libelle_commune": "LYON" }
            }
          ],
          "total_results": 3,
          "page": 1,
          "per_page": 10,
          "total_pages": 1
        }
        """;

    private readonly ExternalHarness _harness = new();

    [Fact]
    public async Task Les_resultats_sont_traduits_en_contrats_et_les_siren_invalides_ecartes()
    {
        _harness.Handler.Respond = _ => FakeHttpHandler.Json(Martin);

        Result<CompanySearchResponse> result = await _harness.Directory.SearchAsync("boulangerie martin", 10, default);

        result.Value.TotalCount.Should().Be(3);
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items[0].Should().Be(new CompanySearchResult(
            "732829320", "73282932000074", "BOULANGERIE MARTIN", "12 B RUE DE LA PAIX", "BATIMENT A", "75002", "PARIS",
            "FR", "FR44732829320", "10.71C", IsActive: true));
        result.Value.Items[1].Name.Should().Be("ANCIENNE SOCIETE");
        result.Value.Items[1].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task La_requete_envoie_la_saisie_encodee_et_le_nombre_de_resultats()
    {
        _harness.Handler.Respond = _ => FakeHttpHandler.Json(Martin);

        await _harness.Directory.SearchAsync("732 829 320", 5, default);

        _harness.Handler.Requests.Should().ContainSingle().Which.Uri.PathAndQuery
            .Should().Be("/search?q=732829320&page=1&per_page=5");
    }

    [Fact]
    public async Task Une_meme_recherche_est_servie_par_le_cache()
    {
        _harness.Handler.Respond = _ => FakeHttpHandler.Json(Martin);

        await _harness.Directory.SearchAsync("Martin", 10, default);
        await _harness.Directory.SearchAsync("Martin", 10, default);

        _harness.Handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Une_erreur_du_service_donne_un_annuaire_indisponible_et_n_est_pas_mise_en_cache(HttpStatusCode status)
    {
        _harness.Handler.Respond = _ => new HttpResponseMessage(status);

        Result<CompanySearchResponse> first = await _harness.Directory.SearchAsync("Martin", 10, default);
        _harness.Handler.Respond = _ => FakeHttpHandler.Json(Martin);
        Result<CompanySearchResponse> second = await _harness.Directory.SearchAsync("Martin", 10, default);

        first.Errors.Should().ContainSingle().Which.Code.Should().Be(DirectoryErrors.UnavailableCode);
        second.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Une_coupure_reseau_donne_un_annuaire_indisponible()
    {
        _harness.Handler.Respond = _ => throw new HttpRequestException("pas de réseau");

        Result<CompanySearchResponse> result = await _harness.Directory.SearchAsync("Martin", 10, default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(DirectoryErrors.UnavailableCode);
    }

    [Fact]
    public async Task Une_reponse_illisible_donne_un_annuaire_indisponible()
    {
        _harness.Handler.Respond = _ => FakeHttpHandler.Json("<html>maintenance</html>");

        (await _harness.Directory.SearchAsync("Martin", 10, default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(DirectoryErrors.UnavailableCode);
    }

    [Fact]
    public async Task La_saisie_n_apparait_jamais_dans_les_journaux()
    {
        _harness.Handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        await _harness.Directory.SearchAsync("Cabinet Secret Durand", 10, default);

        _harness.Logs.Should().NotBeEmpty().And.NotContain(l => l.Contains("Durand", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Annuaires_desactives_aucun_appel_sortant()
    {
        using ExternalHarness disabled = new(enabled: false);

        Result<CompanySearchResponse> result = await disabled.Directory.SearchAsync("Martin", 10, default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(DirectoryErrors.UnavailableCode);
        disabled.Handler.Requests.Should().BeEmpty();
    }

    public void Dispose() => _harness.Dispose();
}
