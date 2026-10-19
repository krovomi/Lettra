using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Mail;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Reminders;
using Lettra.Domain.Common;
using Lettra.Domain.Reminders;
using Lettra.Exports.Application.Reminders;
using Lettra.Infrastructure.Persistence.Integrity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Exports.Infrastructure;

/// <summary>Réglages de la passe quotidienne des relances (Worker), active par défaut.</summary>
public sealed class PaymentReminderJobOptions
{
    public const string SectionName = "Lettra:Worker:PaymentReminders";

    public bool Enabled { get; set; } = true;

    /// <summary>Les délais sont des jours : une passe toutes les 12 heures suffit, l'écart minimal évite tout doublon.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Attente avant la première passe, le temps que les migrations de démarrage se terminent.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Passe des relances : pour chaque dossier actif dont l'envoi automatique est activé, envoie par e-mail les relances
/// dues aujourd'hui (même règle que le bouton « Envoyer les relances du jour »). Sans serveur d'e-mail configuré, la
/// passe ne fait rien. Aucun destinataire ni montant n'est journalisé.
/// </summary>
internal sealed partial class PaymentReminderJob(
    IServiceScopeFactory scopes,
    IServiceProvider services,
    ReminderDossierSource dossierSource,
    IEmailSender email,
    IOptions<PaymentReminderJobOptions> options,
    ILogger<PaymentReminderJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PaymentReminderJobOptions settings = options.Value;
        if (!settings.Enabled || !email.IsConfigured)
        {
            return;
        }

        await Task.Delay(settings.InitialDelay, stoppingToken).ConfigureAwait(false);
        using PeriodicTimer timer = new(settings.Interval);
        do
        {
            await RunPassAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal async Task<(int Dossiers, int Sent, int Failed)> RunPassAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TenantDescriptor> dossiers;
        try
        {
            dossiers = await dossierSource.ListAsync(services, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPassFailed(exception);
            return (0, 0, 0);
        }

        int active = 0, sent = 0, failed = 0;
        foreach (TenantDescriptor dossier in dossiers)
        {
            try
            {
                RunRemindersResponse? outcome = await DossierJobScope.RunAsync(scopes, dossier, async (provider, token) =>
                {
                    PaymentReminderService reminders = provider.GetRequiredService<PaymentReminderService>();
                    ReminderSettings scenario = await reminders.SettingsAsync(token).ConfigureAwait(false);
                    if (!scenario.AutoSend)
                    {
                        return null;
                    }

                    ICurrentUser user = provider.GetRequiredService<ICurrentUser>();
                    Result<RunRemindersResponse> run = await reminders
                        .RunDueAsync(ReminderTrigger.Schedule, user.IsAuthenticated ? user.UserId : Guid.Empty, token).ConfigureAwait(false);
                    if (run.IsFailure)
                    {
                        // Fiche entreprise absente : rien à signer, et pas une panne.
                        LogSkipped(dossier.DossierId.Value, run.Errors[0].Code);
                        return null;
                    }

                    return run.Value;
                }, cancellationToken).ConfigureAwait(false);

                if (outcome is not null)
                {
                    active++;
                    sent += outcome.Sent;
                    failed += outcome.Failed;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                LogDossierFailed(dossier.DossierId.Value, exception);
            }
        }

        LogPass(active, sent, failed);
        return (active, sent, failed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Relances : {Dossiers} dossiers en envoi automatique, {Sent} relances envoyées, {Failed} en échec")]
    private partial void LogPass(int dossiers, int sent, int failed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Passe des relances impossible : catalogue illisible")]
    private partial void LogPassFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Relances du dossier {DossierId} sans objet : {Code}")]
    private partial void LogSkipped(Guid dossierId, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Relances du dossier {DossierId} en échec")]
    private partial void LogDossierFailed(Guid dossierId, Exception exception);
}

/// <summary>Les dossiers actifs de la cellule pour la passe des relances, fournis par l'hôte.</summary>
internal sealed class ReminderDossierSource(Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> list)
{
    public Task<IReadOnlyList<TenantDescriptor>> ListAsync(IServiceProvider services, CancellationToken cancellationToken) => list(services, cancellationToken);
}
