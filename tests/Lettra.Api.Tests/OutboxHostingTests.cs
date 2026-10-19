using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lettra.Api.Tests;

/// <summary>La passe de l'outbox ne tourne dans l'API que si une installation sans Worker la lui confie.</summary>
public sealed class OutboxHostingTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void L_api_ne_reprend_les_envois_en_file_que_sans_worker(string? hostedByApi, bool expected)
    {
        using LettraApiFactory factory = new();
        // UseSetting, comme une variable d'environnement : lu avant la construction de l'hôte.
        using var configured = factory.WithWebHostBuilder(b => b.UseSetting("Lettra:Outbox:Job:HostedByApi", hostedByApi));

        configured.Services.GetServices<IHostedService>()
            .Any(s => s.GetType().Name == "OutboxDispatchJob")
            .Should().Be(expected);
    }
}
