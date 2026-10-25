using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Exports;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Bridges;

namespace Lettra.Exports.Application.Fec;

/// <summary>Un FEC prêt à remettre : son nom, son contenu (ISO 8859-15) et le verdict du validateur intégré.</summary>
public sealed record FecFile(string FileName, byte[] Content, FecValidationResponse Validation);

/// <summary>
/// Exporter le FEC du dossier sur un exercice (bornes comprises, 24 mois au plus) : les écritures du journal des ventes
/// enregistrées à la validation des pièces. Le fichier passe le validateur intégré avant d'être remis.
/// </summary>
public sealed record ExportFecQuery(DossierId Dossier, DateOnly From, DateOnly To) : IQuery<Result<FecFile>>, IRequirePermission
{
    public Permission Permission => Permission.FecExporter;
}

/// <summary>Valider un FEC fourni (celui de Lettra ou d'un autre logiciel), éventuellement sur un exercice donné.</summary>
public sealed record ValidateFecQuery(DossierId Dossier, string FileName, ReadOnlyMemory<byte> Content, DateOnly? From = null, DateOnly? To = null)
    : IQuery<Result<FecValidationResponse>>, IRequirePermission
{
    /// <summary>Taille maximale d'un FEC soumis au validateur (un exercice de toute la comptabilité d'une PME).</summary>
    public const int MaxContentLength = 64 * 1024 * 1024;

    public Permission Permission => Permission.FecExporter;
}

/// <summary>
/// Exporter les écritures du dossier sur une période (mêmes bornes que le FEC) au format d'import d'un logiciel de
/// production du cabinet : Quadra, Sage 100, Cegid Expert ou ACD.
/// </summary>
public sealed record ExportEntriesForSoftwareQuery(DossierId Dossier, AccountingSoftwareDto Software, DateOnly From, DateOnly To)
    : IQuery<Result<BridgeFile>>, IRequirePermission
{
    public Permission Permission => Permission.FecExporter;
}

/// <summary>
/// Construction du FEC d'un dossier, partagée par la requête de l'utilisateur et l'export en masse du Worker (qui agit
/// dans la portée du dossier, sans passer par les droits d'un utilisateur).
/// </summary>
public sealed class FecExportService(ICompanyProfileRepository profiles, ISalesJournalReader journal)
{
    /// <summary>Un exercice dure au plus 24 mois (premier exercice allongé compris).</summary>
    public const int MaxPeriodMonths = 24;

    public static Result ValidatePeriod(DateOnly from, DateOnly to) =>
        to < from ? FecErrors.PeriodInvalid
        : to >= from.AddMonths(MaxPeriodMonths) ? FecErrors.PeriodTooLong
        : Result.Success();

    public async Task<Result<FecFile>> ExportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        Result<(string Siren, IReadOnlyList<SalesJournalExportEntry> Entries)> read = await ReadAsync(from, to, cancellationToken).ConfigureAwait(false);
        if (read.IsFailure)
        {
            return Result<FecFile>.Failure(read.Errors);
        }

        string fileName = FecFormat.FileName(read.Value.Siren, to);
        byte[] content = FecWriter.Write(read.Value.Entries);
        FecValidationResponse validation = FecValidator.Validate(fileName, content, from, to);
        return validation.IsValid ? new FecFile(fileName, content, validation) : FecErrors.ExportInvalid;
    }

    /// <summary>Les mêmes écritures que le FEC, au format d'import du logiciel du cabinet.</summary>
    public async Task<Result<BridgeFile>> ExportForAsync(AccountingSoftwareDto software, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(software))
        {
            return FecErrors.SoftwareUnknown;
        }

        Result<(string Siren, IReadOnlyList<SalesJournalExportEntry> Entries)> read = await ReadAsync(from, to, cancellationToken).ConfigureAwait(false);
        return read.IsFailure
            ? Result<BridgeFile>.Failure(read.Errors)
            : BridgeWriters.Write(software, read.Value.Siren, from, to, read.Value.Entries);
    }

    private async Task<Result<(string Siren, IReadOnlyList<SalesJournalExportEntry> Entries)>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        Result period = ValidatePeriod(from, to);
        if (period.IsFailure)
        {
            return Result<(string, IReadOnlyList<SalesJournalExportEntry>)>.Failure(period.Errors);
        }

        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return CompanyProfileErrors.NotFound;
        }

        if (profile.Siren is not { } siren)
        {
            return FecErrors.SirenMissing;
        }

        IReadOnlyList<SalesJournalExportEntry> entries = await journal.ReadAsync(from, to, cancellationToken).ConfigureAwait(false);
        return (siren.Value, entries);
    }
}

internal sealed class ExportFecHandler(FecExportService service) : IQueryHandler<ExportFecQuery, Result<FecFile>>
{
    public Task<Result<FecFile>> HandleAsync(ExportFecQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return service.ExportAsync(query.From, query.To, cancellationToken);
    }
}

internal sealed class ExportEntriesForSoftwareHandler(FecExportService service) : IQueryHandler<ExportEntriesForSoftwareQuery, Result<BridgeFile>>
{
    public Task<Result<BridgeFile>> HandleAsync(ExportEntriesForSoftwareQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return service.ExportForAsync(query.Software, query.From, query.To, cancellationToken);
    }
}

internal sealed class ValidateFecHandler : IQueryHandler<ValidateFecQuery, Result<FecValidationResponse>>
{
    public Task<Result<FecValidationResponse>> HandleAsync(ValidateFecQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Content.Length > ValidateFecQuery.MaxContentLength)
        {
            return Task.FromResult<Result<FecValidationResponse>>(FecErrors.FileTooLarge);
        }

        if (query is { From: { } from, To: { } to })
        {
            Result period = FecExportService.ValidatePeriod(from, to);
            if (period.IsFailure)
            {
                return Task.FromResult(Result<FecValidationResponse>.Failure(period.Errors));
            }
        }

        return Task.FromResult<Result<FecValidationResponse>>(FecValidator.Validate(query.FileName, query.Content.Span, query.From, query.To));
    }
}
