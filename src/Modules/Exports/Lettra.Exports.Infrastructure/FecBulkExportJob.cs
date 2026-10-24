using System.Text.Json;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Exports.Application;
using Lettra.Exports.Application.Fec;
using Lettra.Infrastructure.Persistence.Integrity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Exports.Infrastructure;

/// <summary>Réglages de l'export FEC en masse (Worker), désactivé par défaut.</summary>
public sealed class FecBulkExportOptions
{
    public const string SectionName = "Lettra:Worker:FecExport";

    public bool Enabled { get; set; }

    /// <summary>Racine des dépôts ; un lot par date de clôture (<c>FEC-AAAAMMJJ</c>), un dossier par dossier client.</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>Premier jour de l'exercice exporté.</summary>
    public DateOnly? PeriodStart { get; set; }

    /// <summary>Date de clôture de l'exercice exporté.</summary>
    public DateOnly? PeriodEnd { get; set; }

    /// <summary>Attente avant la passe, le temps que les migrations de démarrage se terminent.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Ce qu'est devenu un dossier dans un lot d'export.</summary>
public enum FecBulkExportStatus
{
    Exported = 1,

    /// <summary>Déjà déposé par une passe précédente : la reprise ne le refait pas.</summary>
    AlreadyExported = 2,

    /// <summary>Refusé avec un code (fiche entreprise absente, SIREN manquant…).</summary>
    Refused = 3,

    Failed = 4,
}

/// <summary>Une ligne du rapport du lot : aucune donnée de dossier, des identifiants et des codes.</summary>
public sealed record FecBulkExportLine(Guid DossierId, FecBulkExportStatus Status, string? FileName, int? EntryCount, string? Code);

/// <summary>Le rapport d'un lot, déposé à côté des fichiers (<c>rapport.json</c>).</summary>
public sealed record FecBulkExportReport(DateOnly PeriodStart, DateOnly PeriodEnd, DateTimeOffset GeneratedAt, IReadOnlyList<FecBulkExportLine> Dossiers);

/// <summary>
/// Export FEC en masse (annexe C, étape 5.2) : une passe sur tous les dossiers actifs de la cellule, au démarrage du
/// Worker quand elle est activée. Chaque FEC passe le validateur intégré avant d'être déposé ; un dossier en échec
/// n'arrête pas la passe, un fichier déjà déposé n'est pas refait. Le portail du cabinet et la file asynchrone
/// (étape 5.4) déclencheront ces lots à la demande.
/// </summary>
internal sealed partial class FecBulkExportJob(
    IServiceScopeFactory scopes,
    IServiceProvider services,
    ActiveDossierSource dossierSource,
    IExportArchive archive,
    TimeProvider clock,
    IOptions<FecBulkExportOptions> options,
    ILogger<FecBulkExportJob> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        FecBulkExportOptions settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        if (settings is not { OutputDirectory: not null, PeriodStart: { }, PeriodEnd: { } })
        {
            LogMisconfigured();
            return;
        }

        await Task.Delay(settings.InitialDelay, clock, stoppingToken).ConfigureAwait(false);
        await RunAsync(settings.PeriodStart.Value, settings.PeriodEnd.Value, stoppingToken).ConfigureAwait(false);
    }

    internal async Task<FecBulkExportReport?> RunAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (FecExportService.ValidatePeriod(from, to).IsFailure)
        {
            LogMisconfigured();
            return null;
        }

        IReadOnlyList<TenantDescriptor> dossiers;
        try
        {
            dossiers = await dossierSource.ListAsync(services, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPassFailed(exception);
            return null;
        }

        string batch = "FEC-" + FecFormat.FormatDate(to);
        List<FecBulkExportLine> lines = [];
        foreach (TenantDescriptor dossier in dossiers)
        {
            lines.Add(await ExportDossierAsync(batch, dossier, from, to, cancellationToken).ConfigureAwait(false));
        }

        FecBulkExportReport report = new(from, to, clock.GetUtcNow(), lines);
        await archive.WriteReportAsync(batch, JsonSerializer.SerializeToUtf8Bytes(report, ReportJson), cancellationToken).ConfigureAwait(false);
        LogPass(
            lines.Count(l => l.Status == FecBulkExportStatus.Exported),
            lines.Count(l => l.Status == FecBulkExportStatus.AlreadyExported),
            lines.Count(l => l.Status == FecBulkExportStatus.Refused),
            lines.Count(l => l.Status == FecBulkExportStatus.Failed));
        return report;
    }

    private async Task<FecBulkExportLine> ExportDossierAsync(string batch, TenantDescriptor dossier, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        Guid id = dossier.DossierId.Value;
        try
        {
            Result<FecFile> file = await DossierJobScope.RunAsync(
                scopes,
                dossier,
                (provider, token) => provider.GetRequiredService<FecExportService>().ExportAsync(from, to, token),
                cancellationToken).ConfigureAwait(false);
            if (file.IsFailure)
            {
                LogRefused(id, file.Errors[0].Code);
                return new FecBulkExportLine(id, FecBulkExportStatus.Refused, null, null, file.Errors[0].Code);
            }

            FecFile fec = file.Value;
            if (await archive.ExistsAsync(batch, dossier.DossierId, fec.FileName, cancellationToken).ConfigureAwait(false))
            {
                return new FecBulkExportLine(id, FecBulkExportStatus.AlreadyExported, fec.FileName, fec.Validation.EntryCount, null);
            }

            await archive.WriteAsync(batch, dossier.DossierId, fec.FileName, fec.Content, cancellationToken).ConfigureAwait(false);
            return new FecBulkExportLine(id, FecBulkExportStatus.Exported, fec.FileName, fec.Validation.EntryCount, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDossierFailed(id, exception);
            return new FecBulkExportLine(id, FecBulkExportStatus.Failed, null, null, null);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Export FEC en masse : {Exported} déposés, {Skipped} déjà présents, {Refused} refusés, {Failed} en échec")]
    private partial void LogPass(int exported, int skipped, int refused, int failed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Export FEC en masse mal configuré : répertoire de dépôt et exercice (24 mois au plus) obligatoires")]
    private partial void LogMisconfigured();

    [LoggerMessage(Level = LogLevel.Error, Message = "Export FEC en masse impossible : catalogue illisible")]
    private partial void LogPassFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "FEC du dossier {DossierId} refusé : {Code}")]
    private partial void LogRefused(Guid dossierId, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "FEC du dossier {DossierId} en échec")]
    private partial void LogDossierFailed(Guid dossierId, Exception exception);
}

/// <summary>Les dossiers actifs de la cellule, fournis par l'hôte (le catalogue, côté Worker).</summary>
internal sealed class ActiveDossierSource(Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> list)
{
    public Task<IReadOnlyList<TenantDescriptor>> ListAsync(IServiceProvider services, CancellationToken cancellationToken) => list(services, cancellationToken);
}
