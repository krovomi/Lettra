using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests;

internal sealed class FakeServerConnection(Uri? current = null, ServerRestartKind restart = ServerRestartKind.CloseThenReopen) : IServerConnection
{
    public Uri? CurrentServer { get; } = current;

    public bool RestartPending { get; private set; }

    public ServerRestartKind RestartKind { get; } = restart;

    public string? Saved { get; private set; }

    public bool DeviceOnly { get; private set; }

    public int Restarts { get; private set; }

    public Task<OperationResult<Uri>> UseServerAsync(string address, CancellationToken cancellationToken)
    {
        if (address.Contains("inconnu", StringComparison.Ordinal))
        {
            return Task.FromResult(OperationResult<Uri>.Failure(ErrorCodes.SharedServer.Unreachable));
        }

        Saved = address;
        RestartPending = true;
        return Task.FromResult(OperationResult<Uri>.Success(new Uri("https://" + address + "/")));
    }

    public Task UseThisDeviceOnlyAsync(CancellationToken cancellationToken)
    {
        DeviceOnly = true;
        RestartPending = true;
        return Task.CompletedTask;
    }

    public void Restart() => Restarts++;
}

[Collection("culture")]
public sealed class SharedServerPageTests : BunitContext
{
    public SharedServerPageTests()
    {
        Services.AddLettraUi();
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    [Fact]
    public void Dans_le_navigateur_l_ecran_donne_l_adresse_a_saisir_dans_les_applications()
    {
        IRenderedComponent<SharedServer> page = Render<SharedServer>();

        page.Find("#shared-server-browser").TextContent.Should().Contain("http://localhost");
        page.FindAll("#shared-server-address").Should().BeEmpty();
    }

    [Fact]
    public void Un_appareil_seul_choisit_le_serveur_puis_redemarre()
    {
        FakeServerConnection connection = new();
        Services.AddSingleton<IServerConnection>(connection);

        IRenderedComponent<SharedServer> page = Render<SharedServer>();
        page.Find("#shared-server-state").GetAttribute("data-mode").Should().Be("device");
        page.FindAll("#shared-server-device-only").Should().BeEmpty("l'appareil travaille déjà seul");

        page.Find("#shared-server-address").Change("192.168.1.20:8443");
        page.Find("form").Submit();

        connection.Saved.Should().Be("192.168.1.20:8443");
        page.Find("#shared-server-saved").TextContent.Should().Contain("https://192.168.1.20:8443");
        page.Find("#shared-server-restart-now").Click();
        connection.Restarts.Should().Be(1);
    }

    [Fact]
    public void Une_adresse_injoignable_est_expliquee_sans_rien_enregistrer()
    {
        FakeServerConnection connection = new();
        Services.AddSingleton<IServerConnection>(connection);

        IRenderedComponent<SharedServer> page = Render<SharedServer>();
        page.Find("#shared-server-address").Change("serveur-inconnu.example");
        page.Find("form").Submit();

        page.Find(".validation-message").TextContent.Should().Contain("Aucun serveur ne répond");
        connection.Saved.Should().BeNull();
        page.FindAll("#shared-server-restart-now").Should().BeEmpty();
    }

    [Fact]
    public void Relie_au_serveur_on_peut_revenir_a_cet_appareil_seul()
    {
        FakeServerConnection connection = new(new Uri("https://192.168.1.20:8443/"), ServerRestartKind.Manual);
        Services.AddSingleton<IServerConnection>(connection);

        IRenderedComponent<SharedServer> page = Render<SharedServer>();
        page.Find("#shared-server-state").GetAttribute("data-mode").Should().Be("server");
        page.Find("#shared-server-address").GetAttribute("value").Should().Be("https://192.168.1.20:8443");

        page.Find("#shared-server-device-only").Click();

        connection.DeviceOnly.Should().BeTrue();
        page.Find("#shared-server-saved").TextContent.Should().Contain("cet appareil seul");
        page.FindAll("#shared-server-restart-now").Should().BeEmpty("iOS : l'utilisateur ferme et rouvre l'application");
    }

    [Fact]
    public void L_ecran_de_session_de_l_application_installee_mene_au_choix_du_serveur()
    {
        Services.AddSingleton<IServerConnection>(new FakeServerConnection());
        Services.AddSingleton<ISessionGateway>(new FakeSessionGateway(new SessionResponse(false, SessionModeDto.Local, null, LocalAccountExists: false)));

        IRenderedComponent<Session> page = Render<Session>();

        page.Find("#shared-server-link").GetAttribute("href").Should().Be("session/serveur");
    }

    [Fact]
    public void Le_navigateur_n_affiche_pas_le_choix_du_serveur_sur_l_ecran_de_session()
    {
        Services.AddSingleton<ISessionGateway>(new FakeSessionGateway(new SessionResponse(false, SessionModeDto.Oidc, null, false), "bff/login"));

        IRenderedComponent<Session> page = Render<Session>();

        page.FindAll("#shared-server-link").Should().BeEmpty();
    }
}
