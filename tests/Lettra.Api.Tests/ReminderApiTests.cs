using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Lettra.Application.Abstractions.Mail;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Common;
using Lettra.Contracts.Reminders;
using Lettra.Contracts.Serialization;
using Lettra.Domain.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Relances des factures échues et invitations par e-mail par l'API réelle (mode 2, SQLite), avec un port e-mail qui
/// garde les messages au lieu de les envoyer. L'échéance de la facture est reculée directement en base : une pièce
/// validée ne peut pas être antidatée par l'API.
/// </summary>
public sealed class ReminderApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static Uri Api(string path) => new(path, UriKind.Relative);

    private static Uri Cabinet(string path) => new($"/api/v1/cabinets/{LettraApiFactory.CabinetId}/{path}", UriKind.Relative);

    private static HttpClient InDossier(HttpClient client, Guid? user = null)
    {
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        if (user is Guid id)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", LettraApiFactory.TokenFor(id));
        }

        return client;
    }

    private WebApplicationFactory<Program> WithMail(CapturedMail mail) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(mail)));

    private async Task MakeOverdueAsync(Guid invoice, int days)
    {
        await using SqliteConnection connection = new($"Data Source={factory.DossierDatabase};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """UPDATE "Invoices" SET "DueDate" = $due WHERE lower("Id") = lower($id)""";
        command.Parameters.AddWithValue("$due", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-days).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", invoice.ToString());
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Une_facture_echue_se_relance_par_e_mail_et_reste_dans_l_historique()
    {
        CapturedMail mail = new();
        using WebApplicationFactory<Program> host = WithMail(mail);
        using HttpClient manager = InDossier(host.CreateClient());
        Guid invoice = await ExportApiTests.ValidatedInvoiceAsync(manager);
        await MakeOverdueAsync(invoice, 20);

        ReminderOverviewResponse overview = (await manager.GetFromJsonAsync(Api("/api/v1/reminders"), ContractsJsonContext.Default.ReminderOverviewResponse))!;
        ReminderInvoiceResponse row = overview.Invoices.Should().ContainSingle(i => i.InvoiceId == invoice).Subject;
        (row.DaysOverdue, row.NextLevel, row.DueToday, row.CustomerEmail, row.RecoveryIndemnity).Should().Be((20, ReminderLevelDto.Friendly, true, "compta@martin.fr", 40m));
        row.PenaltyInterest.Should().BeGreaterThan(0m, "le taux de 10 % de la fiche entreprise est figé sur la facture");
        overview.Settings.EmailConfigured.Should().BeTrue();

        ReminderPreviewResponse preview = (await manager.GetFromJsonAsync(Api($"/api/v1/reminders/invoices/{invoice}/preview"), ContractsJsonContext.Default.ReminderPreviewResponse))!;
        (preview.Level, preview.Language, preview.Recipient).Should().Be((ReminderLevelDto.Friendly, "fr-FR", "compta@martin.fr"));
        preview.Subject.Should().StartWith("Rappel : facture ");

        using HttpResponseMessage sent = await manager.PostAsJsonAsync(
            Api($"/api/v1/reminders/invoices/{invoice}"), new SendReminderRequest(ReminderChannelDto.Email), ContractsJsonContext.Default.SendReminderRequest);
        sent.StatusCode.Should().Be(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        PaymentReminderResponse reminder = (await sent.Content.ReadFromJsonAsync(ContractsJsonContext.Default.PaymentReminderResponse))!;
        (reminder.Outcome, reminder.Recipient, reminder.CustomerName, reminder.Subject).Should().Be(
            (ReminderOutcomeDto.Sent, "compta@martin.fr", "Boulangerie Martin", preview.Subject));
        mail.Sent.Should().ContainSingle().Which.Should().Match<EmailMessage>(m =>
            m.To.Value == "compta@martin.fr" && m.Subject == preview.Subject && m.SenderName == "Conseil Lettra");

        PagedResponse<PaymentReminderResponse> history = (await manager.GetFromJsonAsync(
            Api($"/api/v1/reminders/history?invoiceId={invoice}&level=Friendly&outcome=Sent&channel=Email&search=martin&sort=Customer&descending=false&page=1&pageSize=10"),
            ContractsJsonContext.Default.PagedResponsePaymentReminderResponse))!;
        history.Items.Should().ContainSingle().Which.Id.Should().Be(reminder.Id);

        // Le jour même, rien d'autre n'est dû : la passe ne renvoie rien.
        using HttpResponseMessage run = await manager.PostAsync(Api("/api/v1/reminders/run"), null);
        run.StatusCode.Should().Be(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());
        (await run.Content.ReadFromJsonAsync(ContractsJsonContext.Default.RunRemindersResponse))!.Sent.Should().Be(0);
    }

    [Fact]
    public async Task Le_scenario_se_regle_et_un_reglage_invalide_est_refuse()
    {
        using HttpClient manager = InDossier(factory.CreateClient());

        using HttpResponseMessage invalid = await manager.PutAsJsonAsync(
            Api("/api/v1/reminders/settings"), new UpdateReminderSettingsRequest { Delays = [10, 5, 30, 45] }, ContractsJsonContext.Default.UpdateReminderSettingsRequest);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Reminders.DelaysInvalid);

        using HttpResponseMessage saved = await manager.PutAsJsonAsync(
            Api("/api/v1/reminders/settings"), new UpdateReminderSettingsRequest { AutoSend = true, Delays = [5, 20, 40, 60], MinimumGapDays = 10 },
            ContractsJsonContext.Default.UpdateReminderSettingsRequest);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        ReminderSettingsResponse settings = (await manager.GetFromJsonAsync(Api("/api/v1/reminders/settings"), ContractsJsonContext.Default.ReminderSettingsResponse))!;
        (settings.AutoSend, settings.MinimumGapDays, settings.EmailConfigured).Should().Be((true, 10, false));
        settings.Delays.Should().Equal(5, 20, 40, 60);
    }

    [Fact]
    public async Task Sans_serveur_d_envoi_la_passe_est_refusee_et_un_collaborateur_ne_relance_pas()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        using HttpClient collaborateur = InDossier(factory.CreateClient(), LettraApiFactory.CollaborateurId);

        using HttpResponseMessage run = await manager.PostAsync(Api("/api/v1/reminders/run"), null);
        run.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await run.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Email.NotConfigured);

        using HttpResponseMessage notOverdue = await manager.PostAsJsonAsync(
            Api($"/api/v1/reminders/invoices/{Guid.CreateVersion7()}"), new SendReminderRequest(ReminderChannelDto.Manual), ContractsJsonContext.Default.SendReminderRequest);
        notOverdue.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await notOverdue.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Reminders.InvoiceNotOverdue);

        (await collaborateur.PostAsync(Api("/api/v1/reminders/run"), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Une_personne_invitee_recoit_un_e_mail_qui_peut_etre_renvoye()
    {
        CapturedMail mail = new();
        using WebApplicationFactory<Program> host = WithMail(mail);
        using HttpClient admin = host.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new("Bearer", LettraApiFactory.TokenFor(LettraApiFactory.AdministrateurCabinetId));

        HttpResponseMessage invited = await admin.PostAsJsonAsync(
            Cabinet("personnes"), new InvitePersonRequest { DisplayName = "Inès Petit", Email = "ines.petit@cabinet.test" }, ContractsJsonContext.Default.InvitePersonRequest);
        invited.StatusCode.Should().Be(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync());
        PersonSummary ines = (await invited.Content.ReadFromJsonAsync(ContractsJsonContext.Default.PersonSummary))!;
        ines.InvitationEmail.Should().Be(InvitationEmailStatusDto.Sent);
        EmailMessage message = mail.Sent.Should().ContainSingle().Subject;
        message.To.Value.Should().Be("ines.petit@cabinet.test");
        message.Subject.Should().StartWith("Invitation à rejoindre ");
        message.TextBody.Should().Contain("Bonjour Inès Petit").And.Contain("Hello Inès Petit").And.Contain("ines.petit@cabinet.test");

        HttpResponseMessage resent = await admin.PostAsync(Cabinet($"personnes/{ines.Id}/invitation"), null);
        resent.StatusCode.Should().Be(HttpStatusCode.OK, await resent.Content.ReadAsStringAsync());
        (await resent.Content.ReadFromJsonAsync(ContractsJsonContext.Default.InvitationEmailResponse))!.Should().Be(new InvitationEmailResponse(ines.Id, InvitationEmailStatusDto.Sent));
        mail.Sent.Should().HaveCount(2);

        // Une personne déjà connectée n'est plus invitée ; une personne inconnue n'existe pas.
        HttpResponseMessage active = await admin.PostAsync(Cabinet($"personnes/{LettraApiFactory.ManagerId}/invitation"), null);
        active.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await active.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Administration.NotInvited);
        (await admin.PostAsync(Cabinet($"personnes/{Guid.CreateVersion7()}/invitation"), null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Un serveur qui refuse le message n'annule pas l'invitation : l'écran le dit.
        mail.Fail = true;
        (await (await admin.PostAsync(Cabinet($"personnes/{ines.Id}/invitation"), null)).Content.ReadFromJsonAsync(ContractsJsonContext.Default.InvitationEmailResponse))!
            .Status.Should().Be(InvitationEmailStatusDto.Failed);
    }

    [Fact]
    public async Task Sans_serveur_d_envoi_l_invitation_est_creee_et_l_ecran_le_signale()
    {
        using HttpClient admin = factory.ClientFor(LettraApiFactory.AdministrateurCabinetId);

        HttpResponseMessage invited = await admin.PostAsJsonAsync(
            Cabinet("personnes"), new InvitePersonRequest { DisplayName = "Hugo Roy", Email = "hugo.roy@cabinet.test" }, ContractsJsonContext.Default.InvitePersonRequest);

        invited.StatusCode.Should().Be(HttpStatusCode.Created);
        (await invited.Content.ReadFromJsonAsync(ContractsJsonContext.Default.PersonSummary))!.InvitationEmail.Should().Be(InvitationEmailStatusDto.NotConfigured);
    }

    private sealed class CapturedMail : IEmailSender
    {
        public bool IsConfigured => true;

        public bool Fail { get; set; }

        public ConcurrentQueue<EmailMessage> Sent { get; } = new();

        public Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                return Task.FromResult(Result.Failure(EmailErrors.SendFailed));
            }

            Sent.Enqueue(message);
            return Task.FromResult(Result.Success());
        }
    }
}
