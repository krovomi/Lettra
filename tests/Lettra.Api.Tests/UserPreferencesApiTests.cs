using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Preferences;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>
/// <c>/api/v1/me/preferences</c> : chacun lit et écrit son propre compte (base Catalog), à partir de la version lue.
/// Nécessite <c>app.MapUserPreferences()</c> dans Program.cs.
/// </summary>
public sealed class UserPreferencesApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private const string Path = "/api/v1/me/preferences";

    private static ContractsJsonContext Json => ContractsJsonContext.Default;

    private static SaveUserPreferencesRequest Request(long? version, string theme = "Dark") => new()
    {
        Workspace = new WorkspacePreferencesDto { Theme = theme, Language = "en-US", HomePage = "invoices", NavFavorites = ["quotes"] },
        EntryDefaults = [new EntryDefaultsDto { DossierId = LettraApiFactory.DossierId, Unit = "HUR", QuoteValidityDays = 45 }],
        Version = version,
    };

    [Fact]
    public async Task Le_compte_est_lu_enregistre_compte_les_choix_puis_s_efface()
    {
        using HttpClient client = factory.ClientFor(LettraApiFactory.CollaborateurId);

        UserPreferencesResponse empty = (await client.GetFromJsonAsync(Path, Json.UserPreferencesResponse))!;
        empty.Version.Should().BeNull();
        empty.Workspace.RankByUsage.Should().BeTrue();

        using HttpResponseMessage saved = await client.PutAsJsonAsync(Path, Request(null), Json.SaveUserPreferencesRequest);
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        UserPreferencesResponse stored = (await saved.Content.ReadFromJsonAsync(Json.UserPreferencesResponse))!;
        stored.Version.Should().Be(1);
        stored.Workspace.Theme.Should().Be("Dark");
        stored.EntryDefaults.Should().ContainSingle().Which.QuoteValidityDays.Should().Be(45);

        // Un autre appareil enregistre sur une version dépassée : refusé, rien n'est écrasé.
        using HttpResponseMessage stale = await client.PutAsJsonAsync(Path, Request(null, "Light") with { Version = 0 }, Json.SaveUserPreferencesRequest);
        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await stale.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Concurrency.VersionMismatch);

        Guid customer = Guid.CreateVersion7();
        RecordUsageRequest use = new(LettraApiFactory.DossierId, UsageKindDto.Customer, customer);
        (await client.PostAsJsonAsync($"{Path}/usage", use, Json.RecordUsageRequest)).StatusCode.Should().Be(HttpStatusCode.OK);
        using HttpResponseMessage counted = await client.PostAsJsonAsync($"{Path}/usage", use, Json.RecordUsageRequest);
        UserPreferencesResponse usage = (await counted.Content.ReadFromJsonAsync(Json.UserPreferencesResponse))!;
        usage.Usage.Should().ContainSingle().Which.Entries.Should().ContainSingle().Which.Uses.Should().Be(2);
        usage.Workspace.Theme.Should().Be("Dark", "compter un choix ne touche pas aux réglages");

        // Chacun ne voit que son compte.
        using HttpClient other = factory.ClientFor(LettraApiFactory.ManagerId);
        (await other.GetFromJsonAsync(Path, Json.UserPreferencesResponse))!.Workspace.Theme.Should().NotBe("Dark");

        using HttpResponseMessage forgotten = await client.DeleteAsync($"{Path}/usage?dossierId={LettraApiFactory.DossierId}");
        (await forgotten.Content.ReadFromJsonAsync(Json.UserPreferencesResponse))!.Usage.Should().BeEmpty();

        using HttpResponseMessage reset = await client.DeleteAsync(Path);
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        UserPreferencesResponse cleared = (await reset.Content.ReadFromJsonAsync(Json.UserPreferencesResponse))!;
        cleared.Workspace.Theme.Should().BeNull();
        cleared.EntryDefaults.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_reglage_invalide_est_refuse()
    {
        using HttpClient client = factory.ClientFor(LettraApiFactory.CollaborateurId);
        SaveUserPreferencesRequest request = Request(null) with { Workspace = new WorkspacePreferencesDto { HomePage = "https://ailleurs.example" } };

        using HttpResponseMessage refused = await client.PutAsJsonAsync(Path, request, Json.SaveUserPreferencesRequest);

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Preferences.Invalid);
    }

    [Fact]
    public async Task Sans_session_rien_n_est_lu()
    {
        using HttpClient anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Authorization = null;

        (await anonymous.GetAsync(Path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
