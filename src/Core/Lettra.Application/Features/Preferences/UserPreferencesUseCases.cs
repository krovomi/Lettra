using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Preferences;
using Lettra.Contracts.Common;
using Lettra.Contracts.Preferences;
using Lettra.Domain.Common;
using Lettra.Domain.Preferences;

namespace Lettra.Application.Features.Preferences;

/// <summary>
/// Lire ses propres préférences. Aucune permission : chacun ne lit que son compte (l'identifiant vient de la session,
/// jamais de la requête). Rien n'est encore réglé : des préférences vides, version <c>null</c>.
/// </summary>
[PublicUseCase]
public sealed record GetUserPreferencesQuery : IQuery<Result<UserPreferencesResponse>>;

/// <summary>Enregistrer son espace de travail et ses valeurs de saisie (page « Mes préférences »), à partir de la version lue.</summary>
[PublicUseCase]
public sealed record SaveUserPreferencesCommand(SaveUserPreferencesRequest Request) : ICommand<Result<UserPreferencesResponse>>;

/// <summary>Compter un choix (client, article, pièce ouverte) pour le reproposer en premier. Sans version : fusionné côté serveur.</summary>
[PublicUseCase]
public sealed record RecordUsageCommand(RecordUsageRequest Request) : ICommand<Result<UserPreferencesResponse>>;

/// <summary>Oublier ses choix comptés, d'un dossier ou de tous (<see cref="DossierId"/> <c>null</c>).</summary>
[PublicUseCase]
public sealed record ForgetUsageCommand(Guid? DossierId) : ICommand<Result<UserPreferencesResponse>>;

/// <summary>Revenir aux réglages de départ : tout ce que le compte retenait est effacé.</summary>
[PublicUseCase]
public sealed record ResetUserPreferencesCommand : ICommand<Result<UserPreferencesResponse>>;

internal sealed class SaveUserPreferencesValidator : IValidator<SaveUserPreferencesCommand>
{
    public IReadOnlyList<Error> Validate(SaveUserPreferencesCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        SaveUserPreferencesRequest? request = instance.Request;
        List<Error> errors = [];
        if (request is null)
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.Required, nameof(instance.Request)));
        }
        else
        {
            if (request.Workspace is null)
            {
                errors.Add(Error.Validation(ErrorCodes.Validation.Required, nameof(request.Workspace)));
            }

            if (request.EntryDefaults is null || request.EntryDefaults.Any(d => d is null))
            {
                errors.Add(Error.Validation(ErrorCodes.Validation.Required, nameof(request.EntryDefaults)));
            }
        }

        return errors;
    }
}

internal sealed class RecordUsageValidator : IValidator<RecordUsageCommand>
{
    public IReadOnlyList<Error> Validate(RecordUsageCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return instance.Request is null
            ? [Error.Validation(ErrorCodes.Validation.Required, nameof(instance.Request))]
            : [];
    }
}

internal sealed class GetUserPreferencesHandler(IUserPreferencesStore store, ICurrentUser user)
    : IQueryHandler<GetUserPreferencesQuery, Result<UserPreferencesResponse>>
{
    public async Task<Result<UserPreferencesResponse>> HandleAsync(GetUserPreferencesQuery query, CancellationToken cancellationToken)
    {
        Result<UserPreferences> loaded = await UserPreferencesAccess.LoadAsync(store, user, cancellationToken).ConfigureAwait(false);
        return loaded.IsSuccess ? UserPreferencesMapping.ToResponse(loaded.Value) : Result<UserPreferencesResponse>.Failure(loaded.Errors);
    }
}

internal sealed class SaveUserPreferencesHandler(IUserPreferencesStore store, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<SaveUserPreferencesCommand, Result<UserPreferencesResponse>>
{
    public async Task<Result<UserPreferencesResponse>> HandleAsync(SaveUserPreferencesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        SaveUserPreferencesRequest request = command.Request;
        Result<UserPreferences> loaded = await UserPreferencesAccess.LoadAsync(store, user, cancellationToken).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return Result<UserPreferencesResponse>.Failure(loaded.Errors);
        }

        UserPreferences preferences = loaded.Value;
        Result version = Concurrency.Check(preferences, request.Version);
        if (version.IsFailure)
        {
            return Result<UserPreferencesResponse>.Failure(version.Errors);
        }

        Result changed = preferences.Change(
            UserPreferencesMapping.ToDomain(request.Workspace),
            [.. request.EntryDefaults.Select(UserPreferencesMapping.ToDomain)],
            clock.GetUtcNow());
        if (changed.IsFailure)
        {
            return Result<UserPreferencesResponse>.Failure(changed.Errors);
        }

        return await UserPreferencesAccess.SaveAsync(store, preferences, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class RecordUsageHandler(IUserPreferencesStore store, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<RecordUsageCommand, Result<UserPreferencesResponse>>
{
    public Task<Result<UserPreferencesResponse>> HandleAsync(RecordUsageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RecordUsageRequest request = command.Request;
        return UserPreferencesAccess.UpdateAsync(
            store,
            user,
            preferences => preferences.RecordUse(request.DossierId, (UsageKind)(int)request.Kind, request.Id, clock.GetUtcNow()),
            cancellationToken);
    }
}

internal sealed class ForgetUsageHandler(IUserPreferencesStore store, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<ForgetUsageCommand, Result<UserPreferencesResponse>>
{
    public Task<Result<UserPreferencesResponse>> HandleAsync(ForgetUsageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return UserPreferencesAccess.UpdateAsync(
            store,
            user,
            preferences =>
            {
                preferences.ForgetUsage(command.DossierId, clock.GetUtcNow());
                return Result.Success();
            },
            cancellationToken);
    }
}

internal sealed class ResetUserPreferencesHandler(IUserPreferencesStore store, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<ResetUserPreferencesCommand, Result<UserPreferencesResponse>>
{
    public Task<Result<UserPreferencesResponse>> HandleAsync(ResetUserPreferencesCommand command, CancellationToken cancellationToken) =>
        UserPreferencesAccess.UpdateAsync(
            store,
            user,
            preferences =>
            {
                preferences.Reset(clock.GetUtcNow());
                return Result.Success();
            },
            cancellationToken);
}

/// <summary>Lecture et écriture du compte de l'utilisateur connecté, communes aux cas d'usage.</summary>
internal static class UserPreferencesAccess
{
    /// <summary>Écritures sans version (choix comptés, effacement) : relues et refaites si un autre appareil a écrit entre-temps.</summary>
    public const int MaxAttempts = 3;

    public static async Task<Result<UserPreferences>> LoadAsync(IUserPreferencesStore store, ICurrentUser user, CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated || user.UserId == Guid.Empty)
        {
            return SecurityErrors.Unauthenticated;
        }

        if (!store.IsAvailable)
        {
            return UserPreferencesErrors.Unavailable;
        }

        UserPreferences? found = await store.FindAsync(user.UserId, cancellationToken).ConfigureAwait(false);
        return found ?? UserPreferences.Empty(user.UserId);
    }

    public static async Task<Result<UserPreferencesResponse>> SaveAsync(IUserPreferencesStore store, UserPreferences preferences, CancellationToken cancellationToken)
    {
        Result<UserPreferences> saved = await store.SaveAsync(preferences, cancellationToken).ConfigureAwait(false);
        return saved.IsSuccess ? UserPreferencesMapping.ToResponse(saved.Value) : Result<UserPreferencesResponse>.Failure(saved.Errors);
    }

    public static async Task<Result<UserPreferencesResponse>> UpdateAsync(
        IUserPreferencesStore store,
        ICurrentUser user,
        Func<UserPreferences, Result> change,
        CancellationToken cancellationToken)
    {
        Result<UserPreferencesResponse> outcome = default;
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            Result<UserPreferences> loaded = await LoadAsync(store, user, cancellationToken).ConfigureAwait(false);
            if (loaded.IsFailure)
            {
                return Result<UserPreferencesResponse>.Failure(loaded.Errors);
            }

            Result changed = change(loaded.Value);
            if (changed.IsFailure)
            {
                return Result<UserPreferencesResponse>.Failure(changed.Errors);
            }

            outcome = await SaveAsync(store, loaded.Value, cancellationToken).ConfigureAwait(false);
            if (!outcome.Errors.Any(e => e.Code == ConcurrencyErrors.VersionMismatchCode))
            {
                return outcome;
            }
        }

        return outcome;
    }
}

internal static class UserPreferencesMapping
{
    public static UserPreferencesResponse ToResponse(UserPreferences preferences) => new(
        new WorkspacePreferencesDto
        {
            Theme = preferences.Workspace.Theme,
            Density = preferences.Workspace.Density,
            Language = preferences.Workspace.Language,
            HomePage = preferences.Workspace.HomePage,
            NavFavorites = preferences.Workspace.NavFavorites,
            CollapsedSections = preferences.Workspace.CollapsedSections,
            FavoriteDossiers = preferences.Workspace.FavoriteDossiers,
            RecentDossiers = preferences.Workspace.RecentDossiers,
            RankByUsage = preferences.Workspace.RankByUsage,
        },
        [.. preferences.EntryDefaults.Select(d => new EntryDefaultsDto
        {
            DossierId = d.DossierId,
            VatRateCode = d.VatRateCode,
            Unit = d.Unit,
            PaymentTerms = d.PaymentTerms,
            InvoiceNotes = d.InvoiceNotes,
            QuoteNotes = d.QuoteNotes,
            QuoteValidityDays = d.QuoteValidityDays,
        })],
        [.. preferences.Usage.Select(u => new DossierUsageDto(
            u.DossierId,
            [.. u.Entries.Select(e => new UsageEntryDto((UsageKindDto)(int)e.Kind, e.Id, e.Uses, e.LastUsed))]))],
        preferences.Version == 0 ? null : preferences.Version,
        preferences.Version == 0 ? null : preferences.UpdatedAt);

    public static WorkspacePreferences ToDomain(WorkspacePreferencesDto workspace) => new()
    {
        Theme = workspace.Theme,
        Density = workspace.Density,
        Language = workspace.Language,
        HomePage = workspace.HomePage,
        NavFavorites = workspace.NavFavorites ?? [],
        CollapsedSections = workspace.CollapsedSections ?? [],
        FavoriteDossiers = workspace.FavoriteDossiers ?? [],
        RecentDossiers = workspace.RecentDossiers ?? [],
        RankByUsage = workspace.RankByUsage,
    };

    public static EntryDefaults ToDomain(EntryDefaultsDto defaults) => new()
    {
        DossierId = defaults.DossierId,
        VatRateCode = defaults.VatRateCode,
        Unit = defaults.Unit,
        PaymentTerms = defaults.PaymentTerms,
        InvoiceNotes = defaults.InvoiceNotes,
        QuoteNotes = defaults.QuoteNotes,
        QuoteValidityDays = defaults.QuoteValidityDays,
    };
}
