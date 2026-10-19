using System.Net;
using System.Net.Sockets;
using System.Text;
using Lettra.Application.Abstractions.Mail;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.External;
using Lettra.Infrastructure.External.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lettra.External.Tests;

public sealed class SmtpEmailSenderTests
{
    private static Email Address(string value) => Email.Create(value).Value;

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(new LettraPublicAddress(null));
        services.AddEmailSender(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Sans_reglage_aucun_adaptateur_smtp_n_est_branche_mais_l_adresse_publique_est_lue()
    {
        using ServiceProvider services = Build(new() { ["Lettra:Email:PublicUrl"] = "http://localhost:8080" });

        services.GetService<IEmailSender>().Should().BeNull("le port reste celui de l'application (aucun envoi)");
        services.GetRequiredService<LettraPublicAddress>().Url.Should().Be(new Uri("http://localhost:8080"));
    }

    [Theory]
    [InlineData("", "lettra@cabinet.test", "587")]
    [InlineData("mailpit", "", "587")]
    [InlineData("mailpit", "lettra@cabinet.test", "0")]
    [InlineData("mailpit", "lettra@cabinet.test", "70000")]
    public void Un_envoi_active_mais_incomplet_arrete_le_demarrage(string host, string from, string port)
    {
        Action act = () => Build(new()
        {
            ["Lettra:Email:Enabled"] = "true",
            ["Lettra:Email:Host"] = host,
            ["Lettra:Email:FromAddress"] = from,
            ["Lettra:Email:Port"] = port,
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Lettra:Email*");
    }

    [Fact]
    public void Un_envoi_active_et_complet_branche_l_adaptateur_smtp()
    {
        using ServiceProvider services = Build(new()
        {
            ["Lettra:Email:Enabled"] = "true",
            ["Lettra:Email:Host"] = "mailpit",
            ["Lettra:Email:Port"] = "1025",
            ["Lettra:Email:FromAddress"] = "lettra@cabinet.test",
        });

        services.GetRequiredService<IEmailSender>().Should().BeOfType<SmtpEmailSender>().Which.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void Le_message_porte_l_expediteur_le_destinataire_la_reponse_et_les_pieces_jointes()
    {
        EmailMessage message = new(
            Address("compta@durand.fr"), "Relance : facture F-1", "Bonjour,\nCorps", SenderName: "Atelier Martin", ReplyTo: Address("contact@martin.fr"),
            Attachments: [new EmailAttachment("F-1.pdf", "application/pdf", new byte[] { 1, 2, 3 })]);

        using System.Net.Mail.MailMessage mail = SmtpEmailSender.Build(message, new EmailOptions { FromAddress = "lettra@cabinet.test" });

        (mail.From!.Address, mail.From.DisplayName).Should().Be(("lettra@cabinet.test", "Atelier Martin"));
        mail.To.Single().Address.Should().Be("compta@durand.fr");
        mail.ReplyToList.Single().Address.Should().Be("contact@martin.fr");
        (mail.Subject, mail.Body, mail.IsBodyHtml).Should().Be(("Relance : facture F-1", "Bonjour,\nCorps", false));
        mail.Attachments.Single().Should().Match<System.Net.Mail.Attachment>(a => a.Name == "F-1.pdf" && a.ContentType.MediaType == "application/pdf" && a.ContentStream.Length == 3);
    }

    [Fact]
    public void Sans_nom_d_expediteur_le_message_prend_celui_des_reglages()
    {
        using System.Net.Mail.MailMessage mail = SmtpEmailSender.Build(
            new EmailMessage(Address("a@b.fr"), "Objet", "Corps"), new EmailOptions { FromAddress = "lettra@cabinet.test" });

        mail.From!.DisplayName.Should().Be("Lettra");
        mail.ReplyToList.Should().BeEmpty();
        mail.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_message_part_vers_le_serveur_smtp()
    {
        await using FakeSmtpServer server = FakeSmtpServer.Start();
        SmtpEmailSender sender = Sender(server.Port, userName: "lettra", password: "secret");

        Result sent = await sender.SendAsync(new EmailMessage(Address("compta@durand.fr"), "Relance", "Bonjour"), default);

        sent.IsSuccess.Should().BeTrue();
        string conversation = await server.ConversationAsync();
        conversation.Should().Contain("MAIL FROM:<lettra@cabinet.test>").And.Contain("RCPT TO:<compta@durand.fr>").And.Contain("Subject: Relance")
            .And.Contain(Convert.ToBase64String(Encoding.UTF8.GetBytes("Bonjour")));
    }

    [Fact]
    public async Task Un_serveur_qui_refuse_le_message_donne_un_echec_d_envoi()
    {
        await using FakeSmtpServer server = FakeSmtpServer.Start(rejectRecipient: true);

        Result sent = await Sender(server.Port).SendAsync(new EmailMessage(Address("compta@durand.fr"), "Relance", "Bonjour"), default);

        sent.Errors.Single().Should().Be(EmailErrors.SendFailed);
    }

    [Fact]
    public async Task Un_serveur_injoignable_donne_un_echec_d_envoi()
    {
        TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int closedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Result sent = await Sender(closedPort).SendAsync(new EmailMessage(Address("compta@durand.fr"), "Relance", "Bonjour"), default);

        sent.Errors.Single().Code.Should().Be(EmailErrors.SendFailed.Code);
    }

    private static SmtpEmailSender Sender(int port, string? userName = null, string? password = null) => new(
        Options.Create(new EmailOptions
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = port,
            EnableSsl = false,
            FromAddress = "lettra@cabinet.test",
            UserName = userName,
            Password = password,
            Timeout = TimeSpan.FromSeconds(5),
        }),
        NullLogger<SmtpEmailSender>.Instance);

    /// <summary>Serveur SMTP minimal en mémoire : accepte une session et garde ce que le client a écrit.</summary>
    private sealed class FakeSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task<string> _session;

        private FakeSmtpServer(bool rejectRecipient)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _session = RunAsync(rejectRecipient);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static FakeSmtpServer Start(bool rejectRecipient = false) => new(rejectRecipient);

        public Task<string> ConversationAsync() => _session.WaitAsync(TimeSpan.FromSeconds(10));

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try
            {
                await _session.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception) when (exception is SocketException or IOException or ObjectDisposedException or TimeoutException)
            {
            }
        }

        private async Task<string> RunAsync(bool rejectRecipient)
        {
            using TcpClient client = await _listener.AcceptTcpClientAsync();
            using NetworkStream stream = client.GetStream();
            using StreamReader reader = new(stream, Encoding.ASCII);
            await using StreamWriter writer = new(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            StringBuilder conversation = new();
            await writer.WriteLineAsync("220 fake ESMTP");
            bool inData = false;
            while (await reader.ReadLineAsync() is string line)
            {
                conversation.AppendLine(line);
                if (inData)
                {
                    if (line == ".")
                    {
                        inData = false;
                        await writer.WriteLineAsync("250 OK");
                    }

                    continue;
                }

                string verb = line.Split(' ')[0].ToUpperInvariant();
                switch (verb)
                {
                    case "EHLO":
                        await writer.WriteLineAsync("250-fake");
                        await writer.WriteLineAsync("250 AUTH LOGIN PLAIN");
                        break;
                    case "AUTH":
                        await writer.WriteLineAsync(line.Split(' ').Length > 2 ? "235 OK" : "334 VXNlcm5hbWU6");
                        if (line.Split(' ').Length == 2)
                        {
                            conversation.AppendLine(await reader.ReadLineAsync());
                            await writer.WriteLineAsync("334 UGFzc3dvcmQ6");
                            conversation.AppendLine(await reader.ReadLineAsync());
                            await writer.WriteLineAsync("235 OK");
                        }

                        break;
                    case "RCPT" when rejectRecipient:
                        await writer.WriteLineAsync("550 No such user");
                        break;
                    case "DATA":
                        inData = true;
                        await writer.WriteLineAsync("354 Go");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 Bye");
                        return conversation.ToString();
                    default:
                        await writer.WriteLineAsync("250 OK");
                        break;
                }
            }

            return conversation.ToString();
        }
    }
}
