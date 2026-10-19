using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Lettra.Api.Tests;

/// <summary>Limitation de débit par cabinet puis par utilisateur : 429, code stable et Retry-After.</summary>
public sealed class RateLimitingApiTests
{
    private static WebApplicationFactory<Program> Limited(LettraApiFactory factory, int perCabinet, int perUser) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lettra:RateLimiting:PermitsPerCabinet"] = perCabinet.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Lettra:RateLimiting:PermitsPerUser"] = perUser.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Lettra:RateLimiting:Window"] = "01:00:00",
        })));

    private static HttpClient ClientFor(WebApplicationFactory<Program> factory, Guid userId, Guid cabinet)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LettraApiFactory.TokenFor(userId, cabinet: cabinet));
        return client;
    }

    private static Task<HttpResponseMessage> SettingsAsync(HttpClient client, Guid cabinet) =>
        client.GetAsync(new Uri($"/api/v1/cabinets/{cabinet}/settings/segregation-of-duties", UriKind.Relative));

    [Fact]
    public async Task Le_quota_du_cabinet_epuise_bloque_ses_utilisateurs_mais_pas_les_autres_cabinets()
    {
        using LettraApiFactory root = new();
        using WebApplicationFactory<Program> factory = Limited(root, perCabinet: 2, perUser: 100);
        using HttpClient manager = ClientFor(factory, LettraApiFactory.ManagerId, LettraApiFactory.CabinetId);
        using HttpClient admin = ClientFor(factory, LettraApiFactory.AdministrateurCabinetId, LettraApiFactory.CabinetId);
        using HttpClient other = ClientFor(factory, LettraApiFactory.AutreCabinetAdminId, LettraApiFactory.OtherCabinetId);

        (await SettingsAsync(manager, LettraApiFactory.CabinetId)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        (await SettingsAsync(admin, LettraApiFactory.CabinetId)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);

        using HttpResponseMessage rejected = await SettingsAsync(admin, LettraApiFactory.CabinetId);
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull();
        ProblemResponse? problem = await rejected.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ProblemResponse);
        problem!.Code.Should().Be(ErrorCodes.Api.TooManyRequests);

        (await SettingsAsync(other, LettraApiFactory.OtherCabinetId)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Un_utilisateur_au_dela_de_son_quota_ne_prive_pas_ses_collegues()
    {
        using LettraApiFactory root = new();
        using WebApplicationFactory<Program> factory = Limited(root, perCabinet: 100, perUser: 1);
        using HttpClient manager = ClientFor(factory, LettraApiFactory.ManagerId, LettraApiFactory.CabinetId);
        using HttpClient admin = ClientFor(factory, LettraApiFactory.AdministrateurCabinetId, LettraApiFactory.CabinetId);

        (await SettingsAsync(manager, LettraApiFactory.CabinetId)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        (await SettingsAsync(manager, LettraApiFactory.CabinetId)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await SettingsAsync(admin, LettraApiFactory.CabinetId)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Les_requetes_anonymes_ne_sont_pas_comptees()
    {
        using LettraApiFactory root = new();
        using WebApplicationFactory<Program> factory = Limited(root, perCabinet: 1, perUser: 1);
        using HttpClient anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Authorization = null;

        for (int i = 0; i < 3; i++)
        {
            (await anonymous.GetAsync(new Uri("/health", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
