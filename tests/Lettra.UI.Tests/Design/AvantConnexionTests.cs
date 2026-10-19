using System.Globalization;
using Bunit;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Layout;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Administration;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Cabinet;
using Lettra.UI.Tests.Security;
using Lettra.UI.Tests.Voice;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Design;

/// <summary>
/// Avant la connexion, la mise en page ne lit ni les droits du menu ni l'état de la voix : l'API répondrait 401 et la
/// console du navigateur doit rester vide. Les deux lectures partent dès que la session est ouverte.
/// </summary>
[Collection("culture")]
public sealed class AvantConnexionTests : BunitContext
{
    private readonly FakeAdministrationGateway _administration = new();
    private readonly FakeSpeechGateway _speech = new();
    private readonly FakeSessionGateway _session = new(new SessionResponse(false, SessionModeDto.Local, "Claire Martin", true)) { Passphrase = "phrase" };

    public AvantConnexionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddCabinetFakes();
        Services.AddSingleton<IInvoicesGateway>(new FakeInvoicesGateway());
        Services.AddSingleton<IQuotesGateway>(new FakeQuotesGateway());
        Services.AddSingleton<IAdministrationGateway>(_administration);
        Services.AddSingleton<ISpeechGateway>(_speech);
        Services.AddSingleton<ISessionGateway>(_session);
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    [Fact]
    public async Task Droits_et_voix_attendent_la_connexion()
    {
        Render<MainLayout>(p => p.Add(l => l.Body, b => b.AddContent(0, "écran")));

        _administration.AccessReads.Should().Be(0, "sans session, /api/v1/dossiers/acces répondrait 401");
        _speech.StatusReads.Should().Be(0, "sans session, /api/v1/voix/statut répondrait 401");

        await _session.UnlockAsync(new UnlockLocalSessionRequest { Passphrase = "phrase" }, CancellationToken.None);
        await Services.GetRequiredService<SessionState>().RefreshAsync();

        _administration.AccessReads.Should().Be(1);
        _speech.StatusReads.Should().Be(1);
    }
}
