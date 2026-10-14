using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Assistant;

/// <summary>Codes d'erreur de l'assistant IA.</summary>
public static class AiErrors
{
    public const string DisabledCode = "Ai.Disabled";
    public const string FeatureDisabledCode = "Ai.FeatureDisabled";
    public const string DossierExcludedCode = "Ai.DossierExcluded";
    public const string EngineUnavailableCode = "Ai.EngineUnavailable";

    public static Error Disabled => new(DisabledCode, ErrorKind.Forbidden);

    public static Error FeatureDisabled => new(FeatureDisabledCode, ErrorKind.Forbidden);

    public static Error DossierExcluded => new(DossierExcludedCode, ErrorKind.Forbidden);

    public static Error EngineUnavailable => new(EngineUnavailableCode, ErrorKind.Forbidden);

    /// <summary>Politique du cabinet : la première raison qui empêche l'assistant de répondre, ou <c>null</c>.</summary>
    public static Error? CheckPolicy(AiSettings settings, AssistantFeature feature, DossierId dossier)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Enabled)
        {
            return Disabled;
        }

        if (!settings.Features.Contains(feature))
        {
            return FeatureDisabled;
        }

        if (settings.ExcludedDossiers.Contains(dossier))
        {
            return DossierExcluded;
        }

        return settings.Engine == AiEngineKind.None ? EngineUnavailable : null;
    }
}

/// <summary>
/// Poser une question à l'assistant IA dans le dossier courant. La réponse est un flux d'événements : le pipeline
/// vérifie le dossier, la permission <see cref="Permission.IaUtiliser"/> et la saisie avant le premier appel au moteur.
/// </summary>
public sealed record AskAssistantQuery(DossierId Dossier, AssistantFeature Feature, string Question, Guid? OpenDocumentId = null)
    : IQuery<Result<IAsyncEnumerable<AssistantEvent>>>, IRequirePermission
{
    public Permission Permission => Permission.IaUtiliser;
}

internal sealed class AskAssistantValidator : IValidator<AskAssistantQuery>
{
    public IReadOnlyList<Error> Validate(AskAssistantQuery instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (string.IsNullOrWhiteSpace(instance.Question))
        {
            return [Error.Validation(ErrorCodes.Validation.Required, nameof(instance.Question))];
        }

        if (instance.Question.Length > AskAssistantRequest.MaxQuestionLength)
        {
            return [Error.Validation(ErrorCodes.Validation.TooLong, nameof(instance.Question))];
        }

        return Enum.IsDefined(instance.Feature) ? [] : [Error.Validation(ErrorCodes.Validation.Invalid, nameof(instance.Feature))];
    }
}

internal sealed class AskAssistantHandler(
    ITenantContext tenant,
    ICurrentUser user,
    IPermissionService permissions,
    IAiUsagePolicy policy,
    IAccountingAssistant assistant) : IQueryHandler<AskAssistantQuery, Result<IAsyncEnumerable<AssistantEvent>>>
{
    public async Task<Result<IAsyncEnumerable<AssistantEvent>>> HandleAsync(AskAssistantQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        OrganisationId organisation = tenant.Current.OrganisationId;
        AiSettings settings = await policy.GetSettingsAsync(organisation, cancellationToken).ConfigureAwait(false);
        if (AiErrors.CheckPolicy(settings, query.Feature, query.Dossier) is { } refused)
        {
            return refused;
        }

        IReadOnlySet<Permission> granted = await permissions
            .GetEffectiveAsync(user.UserId, query.Dossier, cancellationToken)
            .ConfigureAwait(false);
        AssistantRequest request = new(organisation, query.Dossier, user.UserId, granted, query.Feature, query.Question, OpenDocumentId: query.OpenDocumentId);
        return Result<IAsyncEnumerable<AssistantEvent>>.Success(assistant.AskAsync(request, cancellationToken));
    }
}

/// <summary>Lire le journal IA du dossier : chaque demande, appel au moteur, outil et réponse (annexe F §10).</summary>
public sealed record GetAssistantJournalQuery(DossierId Dossier, int Limit = 200)
    : IQuery<Result<IReadOnlyList<AssistantJournalEntry>>>, IRequirePermission
{
    public const int MaxLimit = 1000;

    public Permission Permission => Permission.IaUtiliser;
}

internal sealed class GetAssistantJournalValidator : IValidator<GetAssistantJournalQuery>
{
    public IReadOnlyList<Error> Validate(GetAssistantJournalQuery instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return instance.Limit is < 1 or > GetAssistantJournalQuery.MaxLimit
            ? [Error.Validation(ErrorCodes.Validation.Invalid, nameof(instance.Limit))]
            : [];
    }
}

internal sealed class GetAssistantJournalHandler(IAiAuditLog journal)
    : IQueryHandler<GetAssistantJournalQuery, Result<IReadOnlyList<AssistantJournalEntry>>>
{
    public async Task<Result<IReadOnlyList<AssistantJournalEntry>>> HandleAsync(GetAssistantJournalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<AiAuditEntry> entries = await journal.ReadAsync(query.Dossier, query.Limit, cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<AssistantJournalEntry>>.Success(
            [.. entries.Select(e => new AssistantJournalEntry(e.At, e.UserId, e.Kind.ToString(), e.Detail, e.Engine))]);
    }
}
