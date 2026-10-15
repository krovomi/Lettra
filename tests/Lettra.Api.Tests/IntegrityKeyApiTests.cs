using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Integrity;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>
/// Registre des clés de la chaîne et rotation à la demande, par l'API réelle (mode 2, SQLite). Base à part : la
/// rotation ajoute des entrées au journal, ce que le test de la sortie J2 ne doit pas voir.
/// </summary>
public sealed class IntegrityKeyApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static Uri Api(string path) => new(path, UriKind.Relative);

    private static HttpClient InDossier(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    [Fact]
    public async Task Le_manager_fait_tourner_les_cles_et_l_historique_reste_verifiable()
    {
        using HttpClient manager = InDossier(factory.CreateClient());

        IReadOnlyList<IntegrityKeySummary> first = await RotateAsync(manager);
        IReadOnlyList<IntegrityKeySummary> second = await RotateAsync(manager);

        // La première rotation crée la version 1 (le dossier n'avait pas encore de clé) et l'annonce de la version 2.
        first.Select(k => k.Version).Should().Equal(1, 2);
        second.Select(k => k.Version).Should().Equal(1, 2, 3);
        second.Should().ContainSingle(k => k.Current).Which.Version.Should().Be(3);
        second.Take(2).Should().OnlyContain(k => k.RetiredAt != null);

        HttpResponseMessage listed = await manager.GetAsync(Api("/api/v1/integrity/keys"));
        listed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await listed.Content.ReadFromJsonAsync(ContractsJsonContext.Default.IReadOnlyListIntegrityKeySummary))!
            .Select(k => k.KeyId).Should().Equal(second.Select(k => k.KeyId));

        HttpResponseMessage verified = await manager.PostAsync(Api("/api/v1/integrity/verification"), null);
        IntegrityReportResponse report = (await verified.Content.ReadFromJsonAsync(ContractsJsonContext.Default.IntegrityReportResponse))!;
        report.Intact.Should().BeTrue();
        report.EntriesChecked.Should().Be(2, "chaque rotation est annoncée dans la chaîne");
    }

    [Fact]
    public async Task Le_registre_et_la_rotation_exigent_leurs_permissions()
    {
        using HttpClient collaborator = InDossier(factory.ClientFor(LettraApiFactory.CollaborateurId));

        (await collaborator.GetAsync(Api("/api/v1/integrity/keys"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await collaborator.PostAsync(Api("/api/v1/integrity/keys/rotation"), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<IReadOnlyList<IntegrityKeySummary>> RotateAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsync(Api("/api/v1/integrity/keys/rotation"), null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.IReadOnlyListIntegrityKeySummary))!;
    }
}
