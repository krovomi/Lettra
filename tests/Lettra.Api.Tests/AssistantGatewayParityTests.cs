using System.Net;
using System.Text;
using System.Text.Json;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// L'assistant par la gateway : désactivé par défaut, il se dit indisponible avec le même code sur les deux transports ;
/// par HTTP, la réponse NDJSON est lue ligne par ligne, et une coupure ou une erreur illisible devient un événement
/// « indisponible » plutôt qu'une exception.
/// </summary>
public sealed class AssistantGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private static readonly AskAssistantRequest Question = new()
    {
        Feature = AssistantFeature.RegulatoryQuestions,
        Question = "Quel est le taux de TVA sur une prestation de conseil ?",
    };

    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    private static async Task<List<AssistantEvent>> AskAsync(IServiceProvider services, AskAssistantRequest request)
    {
        List<AssistantEvent> events = [];
        await foreach (AssistantEvent assistantEvent in services.GetRequiredService<IAssistantGateway>().AskAsync(request, default))
        {
            events.Add(assistantEvent);
        }

        return events;
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Sans_activation_l_assistant_se_dit_indisponible_de_meme(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);

        OperationResult<AssistantStatusResponse> status = await services.GetRequiredService<IAssistantGateway>().GetStatusAsync(default);
        List<AssistantEvent> events = await AskAsync(services, Question);

        status.IsSuccess.Should().BeTrue(GatewayHarness.Codes(status));
        status.Value!.Features.Should().BeEmpty();
        events.Should().ContainSingle().Which.Should().Be(new AssistantUnavailableEvent(ErrorCodes.Ai.Disabled));
    }

    [Fact]
    public async Task Par_http_le_flux_ndjson_est_lu_evenement_par_evenement()
    {
        string body = string.Join('\n',
            JsonSerializer.Serialize<AssistantEvent>(new AssistantTextEvent("Le taux normal de 20 % s'applique."), ContractsJsonContext.Default.AssistantEvent),
            "",
            JsonSerializer.Serialize<AssistantEvent>(new AssistantCitationEvent("CGI art. 278"), ContractsJsonContext.Default.AssistantEvent),
            "   ",
            JsonSerializer.Serialize<AssistantEvent>(new AssistantCompletedEvent("test:echo", 0, null, null, "abc123"), ContractsJsonContext.Default.AssistantEvent));
        HttpRequestMessage? sent = null;
        await using ServiceProvider services = GatewayHarness.Answering(request =>
        {
            sent = request;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/x-ndjson") };
        });

        List<AssistantEvent> events = await AskAsync(services, Question);

        sent!.RequestUri!.AbsolutePath.Should().Be("/api/v1/assistant");
        events.Should().HaveCount(3);
        events[0].Should().Be(new AssistantTextEvent("Le taux normal de 20 % s'applique."));
        events[1].Should().Be(new AssistantCitationEvent("CGI art. 278"));
        events[2].Should().BeOfType<AssistantCompletedEvent>().Which.Engine.Should().Be("test:echo");
    }

    [Fact]
    public async Task Par_http_un_refus_illisible_ou_une_coupure_devient_un_evenement_indisponible()
    {
        await using ServiceProvider proxy = GatewayHarness.Answering(_ => GatewayHarness.HtmlError(HttpStatusCode.BadGateway));
        await using ServiceProvider offline = GatewayHarness.Unreachable();

        List<AssistantEvent> unreadable = await AskAsync(proxy, Question);
        List<AssistantEvent> unreachable = await AskAsync(offline, Question);
        OperationResult<AssistantStatusResponse> status = await offline.GetRequiredService<IAssistantGateway>().GetStatusAsync(default);

        unreadable.Should().ContainSingle().Which.Should().Be(new AssistantUnavailableEvent(ErrorCodes.Api.Unexpected));
        unreachable.Should().ContainSingle().Which.Should().Be(new AssistantUnavailableEvent(ErrorCodes.Api.Unreachable));
        status.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable);
    }

    [Fact]
    public async Task Par_http_un_flux_coupe_en_route_garde_les_etapes_recues_et_se_termine_sur_indisponible()
    {
        string first = JsonSerializer.Serialize<AssistantEvent>(new AssistantToolUsedEvent("calculer", AssistantToolRisk.ReadOnly, true), ContractsJsonContext.Default.AssistantEvent);
        await using ServiceProvider cut = GatewayHarness.Answering(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new CutStream(Encoding.UTF8.GetBytes(first + "\n"))),
        });
        await using ServiceProvider garbled = GatewayHarness.Answering(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(first + "\n{pas du json\n", Encoding.UTF8, "application/x-ndjson"),
        });

        List<AssistantEvent> interrupted = await AskAsync(cut, Question);
        List<AssistantEvent> unreadable = await AskAsync(garbled, Question);

        foreach (List<AssistantEvent> events in new[] { interrupted, unreadable })
        {
            events.Should().HaveCount(2);
            events[0].Should().BeOfType<AssistantToolUsedEvent>();
            events[1].Should().Be(new AssistantUnavailableEvent(ErrorCodes.Api.Unreachable));
        }
    }

    public void Dispose() => _harness.Dispose();

    /// <summary>Un flux qui rend ses premiers octets puis se coupe, comme une connexion perdue au milieu de la réponse.</summary>
    private sealed class CutStream(byte[] head) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= head.Length)
            {
                throw new IOException("connexion perdue");
            }

            int read = Math.Min(count, head.Length - _position);
            Array.Copy(head, _position, buffer, offset, read);
            _position += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
