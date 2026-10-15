using System.Net;
using System.Text;
using Lettra.Contracts.Sync;
using Lettra.Sync.Client;

namespace Lettra.Sync.Tests;

/// <summary>Le transport HTTP du poste : il se nomme sur chaque appel, et reconnaît sa révocation (étape 4.4).</summary>
public sealed class HttpSyncTransportTests
{
    private sealed class Server(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/problem+json") });
        }
    }

    private static readonly SyncClientOptions Options = new() { DeviceId = Guid.CreateVersion7() };

    private static async Task<SyncFailure> RefusalAsync(HttpStatusCode status, string body)
    {
        using Server server = new(status, body);
        using HttpClient http = new(server) { BaseAddress = new Uri("https://cabinet.example/") };
        HttpSyncTransport transport = new(http, Options);
        SyncRefusedException refused = (await FluentActions.Awaiting(() => transport.PullAsync(Guid.CreateVersion7(), null, 10, default))
            .Should().ThrowAsync<SyncRefusedException>()).Which;
        server.Last!.Headers.GetValues(SyncHttp.DeviceHeader).Should().Equal(Options.DeviceId.ToString());
        return refused.Reason;
    }

    [Fact]
    public async Task Un_refus_qui_porte_la_revocation_du_poste_est_reconnu() =>
        (await RefusalAsync(HttpStatusCode.Forbidden, """{"title":"Sync.DeviceRevoked","status":403,"code":"Sync.DeviceRevoked"}"""))
            .Should().Be(SyncFailure.DeviceRevoked);

    [Theory]
    [InlineData("""{"code":"Security.Forbidden"}""")]
    [InlineData("pas du JSON")]
    [InlineData("")]
    public async Task Un_autre_refus_reste_un_refus_ordinaire(string body) =>
        (await RefusalAsync(HttpStatusCode.Forbidden, body)).Should().Be(SyncFailure.Forbidden);
}
