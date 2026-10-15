using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Mapping;
using Lettra.Contracts.Integrity;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Integrity;

/// <summary>Contrôler maintenant l'intégrité de la chaîne du dossier (le Worker le fait aussi chaque nuit).</summary>
public sealed record VerifyDossierIntegrityCommand(DossierId Dossier)
    : ICommand<Result<IntegrityReportResponse>>, IRequirePermission
{
    public Permission Permission => Permission.IntegriteVerifier;
}

/// <summary>Clôturer les mois écoulés du dossier.</summary>
public sealed record CloseDuePeriodsCommand(DossierId Dossier)
    : ICommand<Result<IReadOnlyList<ClosingSummary>>>, IRequirePermission
{
    public Permission Permission => Permission.ClotureEffectuer;
}

/// <summary>Lister les clôtures signées du dossier.</summary>
public sealed record ListClosingsQuery(DossierId Dossier)
    : IQuery<Result<IReadOnlyList<ClosingSummary>>>, IRequirePermission
{
    public Permission Permission => Permission.IntegriteVerifier;
}

/// <summary>Télécharger l'archive scellée d'une clôture.</summary>
public sealed record GetSealedArchiveQuery(DossierId Dossier, int ClosingNumber)
    : IQuery<Result<SealedArchive>>, IRequirePermission
{
    public Permission Permission => Permission.ArchivesExporter;
}

internal sealed class IntegrityHandlers(DossierIntegrityService integrity, IIntegrityStore store, ISealedArchiveBuilder archives) :
    ICommandHandler<VerifyDossierIntegrityCommand, Result<IntegrityReportResponse>>,
    ICommandHandler<CloseDuePeriodsCommand, Result<IReadOnlyList<ClosingSummary>>>,
    IQueryHandler<ListClosingsQuery, Result<IReadOnlyList<ClosingSummary>>>,
    IQueryHandler<GetSealedArchiveQuery, Result<SealedArchive>>
{
    public async Task<Result<IntegrityReportResponse>> HandleAsync(VerifyDossierIntegrityCommand command, CancellationToken cancellationToken) =>
        (await integrity.VerifyAsync(cancellationToken).ConfigureAwait(false)).Map(IntegrityMapper.ToResponse);

    public async Task<Result<IReadOnlyList<ClosingSummary>>> HandleAsync(CloseDuePeriodsCommand command, CancellationToken cancellationToken) =>
        (await integrity.CloseDuePeriodsAsync(cancellationToken).ConfigureAwait(false)).Map(IntegrityMapper.ToSummaries);

    public async Task<Result<IReadOnlyList<ClosingSummary>>> HandleAsync(ListClosingsQuery query, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<ClosingSummary>>.Success(IntegrityMapper.ToSummaries(await store.ListClosingsAsync(cancellationToken).ConfigureAwait(false)));

    public Task<Result<SealedArchive>> HandleAsync(GetSealedArchiveQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.ClosingNumber < 1
            ? Task.FromResult<Result<SealedArchive>>(IntegrityErrors.ClosingNotFound)
            : archives.BuildAsync(query.ClosingNumber, cancellationToken);
    }
}
