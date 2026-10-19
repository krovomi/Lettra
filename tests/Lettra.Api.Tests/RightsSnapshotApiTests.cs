using System.Net;
using System.Net.Http.Json;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Contracts.Identity;
using Lettra.Contracts.Serialization;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Identity.Oidc.Offline;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lettra.Api.Tests;

/// <summary>Instantané de droits pour le hors ligne : émis par l'API, vérifié avec les clés qu'elle publie.</summary>
public sealed class RightsSnapshotApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static readonly Uri Snapshot = new("/api/v1/me/rights-snapshot", UriKind.Relative);

    private WebApplicationFactory<Program> WithKey() =>
        factory.WithWebHostBuilder(b => b.UseSetting("Lettra:Identity:RightsSnapshot:AllowEphemeralKey", "true"));

    private static async Task<RightsSnapshotKeysResponse> KeysAsync(HttpClient client) =>
        (await client.GetFromJsonAsync(new Uri("/.well-known/lettra-rights-keys", UriKind.Relative), ContractsJsonContext.Default.RightsSnapshotKeysResponse))!;

    [Fact]
    public async Task Le_collaborateur_recoit_ses_droits_sur_son_dossier_et_rien_sur_les_autres()
    {
        using WebApplicationFactory<Program> api = WithKey();
        using HttpClient client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", LettraApiFactory.TokenFor(LettraApiFactory.CollaborateurId));
        Guid device = Guid.CreateVersion7();
        Guid inconnu = Guid.CreateVersion7();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            Snapshot, new IssueRightsSnapshotRequest(device, [LettraApiFactory.DossierId, inconnu]), ContractsJsonContext.Default.IssueRightsSnapshotRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        RightsSnapshotResponse issued = (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.RightsSnapshotResponse))!;
        issued.DossierCount.Should().Be(1);
        (issued.ExpiresAt - issued.IssuedAt).Should().Be(TimeSpan.FromDays(7));

        using HttpClient anonymous = api.CreateClient();
        anonymous.DefaultRequestHeaders.Authorization = null;
        RightsSnapshotVerifier verifier = new(RightsSnapshotKeys.FromJwks(await KeysAsync(anonymous)));
        Result<RightsSnapshot> verified = verifier.Verify(issued.Token, device, LettraApiFactory.CollaborateurId, issued.IssuedAt);
        verified.IsSuccess.Should().BeTrue();
        verified.Value.Dossiers.Keys.Should().Equal(new DossierId(LettraApiFactory.DossierId));
        verified.Value.Dossiers[new DossierId(LettraApiFactory.DossierId)].Should().Contain(Permission.TiersConsulter);
    }

    [Fact]
    public async Task Un_administrateur_d_un_autre_cabinet_n_obtient_aucun_dossier()
    {
        using WebApplicationFactory<Program> api = WithKey();
        using HttpClient client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", LettraApiFactory.TokenFor(LettraApiFactory.AutreCabinetAdminId, cabinet: LettraApiFactory.OtherCabinetId));

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            Snapshot, new IssueRightsSnapshotRequest(Guid.CreateVersion7(), [LettraApiFactory.DossierId]), ContractsJsonContext.Default.IssueRightsSnapshotRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.RightsSnapshotResponse))!.DossierCount.Should().Be(0);
    }

    [Fact]
    public async Task Sans_jeton_pas_d_instantane()
    {
        using WebApplicationFactory<Program> api = WithKey();
        using HttpClient client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add(LettraAuthSchemes.CsrfHeader, "1");

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            Snapshot, new IssueRightsSnapshotRequest(Guid.CreateVersion7(), []), ContractsJsonContext.Default.IssueRightsSnapshotRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sans_cle_configuree_le_serveur_refuse_avec_un_code()
    {
        using HttpResponseMessage response = await factory.CreateClient().PostAsJsonAsync(
            Snapshot, new IssueRightsSnapshotRequest(Guid.CreateVersion7(), [LettraApiFactory.DossierId]), ContractsJsonContext.Default.IssueRightsSnapshotRequest);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ProblemResponse))!.Code.Should().Be(RightsSnapshotErrors.UnavailableCode);
    }
}
