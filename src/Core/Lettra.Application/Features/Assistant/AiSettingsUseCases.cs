using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Assistant;

/// <summary>Codes d'erreur des réglages IA du cabinet.</summary>
public static class AiSettingsErrors
{
    public const string SettingsUnavailableCode = ErrorCodes.Ai.SettingsUnavailable;
    public const string EngineRequiredCode = ErrorCodes.Ai.EngineRequired;
    public const string SettingsConflictCode = ErrorCodes.Ai.SettingsConflict;

    public static Error SettingsUnavailable => Error.Validation(SettingsUnavailableCode);

    public static Error EngineRequired => Error.Validation(EngineRequiredCode, nameof(SaveAiSettingsRequest.Engine));

    public static Error SettingsConflict => Error.Conflict(SettingsConflictCode);

    /// <summary>Le cabinet demande un point fermé sur ce déploiement (<see cref="AiFeatureSafeguards"/>).</summary>
    public static Error FeatureBlocked => Error.Validation(ErrorCodes.Ai.FeatureBlocked, nameof(SaveAiSettingsRequest.Features));
}

/// <summary>
/// Lire les réglages IA du cabinet : ceux enregistrés dans la base Catalog, sinon ceux de la configuration du déploiement
/// (désactivés par défaut). Administration du cabinet, hors de tout dossier.
/// </summary>
public sealed record GetAiSettingsQuery(OrganisationId Organisation) : IQuery<Result<AiSettingsResponse>>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.CabinetAdministrer;
}

/// <summary>
/// Enregistrer les réglages IA du cabinet dans la base Catalog : activation, moteur, fonctions, dossiers exclus.
/// L'administrateur du cabinet seulement ; l'auteur et la date sont conservés. Un point fermé par un garde-fou
/// (<see cref="AiFeatureSafeguards"/>) est refusé, même si l'IA reste éteinte.
/// </summary>
public sealed record SaveAiSettingsCommand(OrganisationId Organisation, SaveAiSettingsRequest Request)
    : ICommand<Result<AiSettingsResponse>>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.CabinetAdministrer;
}

internal sealed class SaveAiSettingsValidator(INamedEntityDetection namedEntities) : IValidator<SaveAiSettingsCommand>
{
    public IReadOnlyList<Error> Validate(SaveAiSettingsCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        SaveAiSettingsRequest request = instance.Request;
        if (request is null)
        {
            return [Error.Validation(ErrorCodes.Validation.Required, nameof(instance.Request))];
        }

        List<Error> errors = [];
        if (!Enum.IsDefined(request.Engine))
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.Invalid, nameof(request.Engine)));
        }
        else if (request.Enabled && request.Engine == AssistantEngine.None)
        {
            errors.Add(AiSettingsErrors.EngineRequired);
        }

        if (request.Features is null || request.Features.Any(f => !Enum.IsDefined(f)))
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.Invalid, nameof(request.Features)));
        }
        else if (request.Features.Any(f => AiFeatureSafeguards.IsBlocked(f, namedEntities)))
        {
            errors.Add(AiSettingsErrors.FeatureBlocked);
        }

        if (request.ExcludedDossiers is null || request.ExcludedDossiers.Count > SaveAiSettingsRequest.MaxExcludedDossiers)
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.TooLong, nameof(request.ExcludedDossiers)));
        }

        if (request.MaxToolRounds is < 1 or > SaveAiSettingsRequest.MaxToolRoundsLimit)
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.Invalid, nameof(request.MaxToolRounds)));
        }

        return errors;
    }
}

internal sealed class GetAiSettingsHandler(IAiSettingsStore store, IAiUsagePolicy policy, INamedEntityDetection namedEntities)
    : IQueryHandler<GetAiSettingsQuery, Result<AiSettingsResponse>>
{
    public async Task<Result<AiSettingsResponse>> HandleAsync(GetAiSettingsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        StoredAiSettings? stored = await store.FindAsync(query.Organisation, cancellationToken).ConfigureAwait(false);
        if (stored is not null)
        {
            return AiSettingsMapping.ToResponse(stored.Settings, namedEntities, AiSettingsSource.Catalog, stored.Version, stored.UpdatedAt);
        }

        AiSettings effective = await policy.GetSettingsAsync(query.Organisation, cancellationToken).ConfigureAwait(false);
        return AiSettingsMapping.ToResponse(effective, namedEntities, AiSettingsSource.Configuration, null, null);
    }
}

internal sealed class SaveAiSettingsHandler(IAiSettingsStore store, ICurrentUser user, INamedEntityDetection namedEntities)
    : ICommandHandler<SaveAiSettingsCommand, Result<AiSettingsResponse>>
{
    public async Task<Result<AiSettingsResponse>> HandleAsync(SaveAiSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!store.IsAvailable)
        {
            return AiSettingsErrors.SettingsUnavailable;
        }

        SaveAiSettingsRequest request = command.Request;
        AiSettings settings = new()
        {
            Enabled = request.Enabled,
            Engine = (AiEngineKind)(int)request.Engine,
            Features = request.Features.ToHashSet(),
            ExcludedDossiers = request.ExcludedDossiers.Select(DossierId.From).ToHashSet(),
            OfficialWebLookup = request.OfficialWebLookup,
            MaxToolRounds = request.MaxToolRounds,
        };
        Result<StoredAiSettings> saved = await store
            .SaveAsync(command.Organisation, settings, request.Version, user.UserId, cancellationToken)
            .ConfigureAwait(false);
        return saved.IsSuccess
            ? AiSettingsMapping.ToResponse(saved.Value.Settings, namedEntities, AiSettingsSource.Catalog, saved.Value.Version, saved.Value.UpdatedAt)
            : Result<AiSettingsResponse>.Failure(saved.Errors);
    }
}

/// <summary>
/// Un point fermé par un garde-fou n'apparaît pas dans la réponse, même s'il reste dans un réglage enregistré avant la
/// fermeture : le réglage n'est pas effacé (les données survivent aux mises à jour), il est ignoré.
/// </summary>
internal static class AiSettingsMapping
{
    public static AiSettingsResponse ToResponse(
        AiSettings settings,
        INamedEntityDetection namedEntities,
        AiSettingsSource source,
        long? version,
        DateTimeOffset? updatedAt) => new(
        settings.Enabled,
        (AssistantEngine)(int)settings.Engine,
        [.. settings.Features.Where(f => !AiFeatureSafeguards.IsBlocked(f, namedEntities)).Order()],
        [.. settings.ExcludedDossiers.Select(d => d.Value).Order()],
        settings.OfficialWebLookup,
        settings.MaxToolRounds,
        source,
        version,
        updatedAt);
}
