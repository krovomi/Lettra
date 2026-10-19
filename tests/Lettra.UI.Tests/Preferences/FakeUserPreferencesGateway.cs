using Lettra.Contracts.Common;
using Lettra.Contracts.Preferences;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Preferences;

/// <summary>Le compte de l'utilisateur côté serveur : une version par écriture, des conflits et des pannes simulables.</summary>
internal sealed class FakeUserPreferencesGateway : IUserPreferencesGateway
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    public UserPreferencesResponse Stored { get; set; } = new(new WorkspacePreferencesDto(), [], [], null, null);

    /// <summary>Faux : serveur sans la route ou poste isolé (<c>Preferences.Unavailable</c>).</summary>
    public bool Available { get; set; } = true;

    /// <summary>Vrai : chaque écriture rend <c>Api.Unreachable</c> (la lecture reste possible).</summary>
    public bool WritesUnreachable { get; set; }

    public List<string> Calls { get; } = [];

    public List<SaveUserPreferencesRequest> Saves { get; } = [];

    /// <summary>Un autre appareil écrit : la version avance sans que l'écran le sache.</summary>
    public void WriteFromAnotherDevice(Func<UserPreferencesResponse, UserPreferencesResponse> change) =>
        Stored = change(Stored) with { Version = (Stored.Version ?? 0) + 1, UpdatedAt = Now };

    public Task<OperationResult<UserPreferencesResponse>> GetAsync(CancellationToken cancellationToken)
    {
        Calls.Add("get");
        return Task.FromResult(Available ? OperationResult<UserPreferencesResponse>.Success(Stored) : Unavailable());
    }

    public Task<OperationResult<UserPreferencesResponse>> SaveAsync(SaveUserPreferencesRequest request, CancellationToken cancellationToken)
    {
        Calls.Add("save");
        Saves.Add(request);
        if (Refused() is { } refused)
        {
            return refused;
        }

        if (request.Version is not null && request.Version != Stored.Version)
        {
            return Task.FromResult(OperationResult<UserPreferencesResponse>.Stale(Stored));
        }

        return Write(Stored with { Workspace = request.Workspace, EntryDefaults = [.. request.EntryDefaults] });
    }

    public Task<OperationResult<UserPreferencesResponse>> RecordUsageAsync(RecordUsageRequest request, CancellationToken cancellationToken)
    {
        Calls.Add($"usage:{request.Kind}");
        if (Refused() is { } refused)
        {
            return refused;
        }

        DossierUsageDto current = Stored.Usage.FirstOrDefault(u => u.DossierId == request.DossierId) ?? new DossierUsageDto(request.DossierId, []);
        UsageEntryDto? previous = current.Entries.FirstOrDefault(e => e.Kind == request.Kind && e.Id == request.Id);
        DossierUsageDto updated = current with
        {
            Entries = [.. current.Entries.Where(e => e != previous), new UsageEntryDto(request.Kind, request.Id, (previous?.Uses ?? 0) + 1, Now.AddSeconds(Calls.Count))],
        };
        return Write(Stored with { Usage = [updated, .. Stored.Usage.Where(u => u.DossierId != request.DossierId)] });
    }

    public Task<OperationResult<UserPreferencesResponse>> ForgetUsageAsync(Guid? dossierId, CancellationToken cancellationToken)
    {
        Calls.Add("forget");
        return Refused() ?? Write(Stored with { Usage = dossierId is Guid id ? [.. Stored.Usage.Where(u => u.DossierId != id)] : [] });
    }

    public Task<OperationResult<UserPreferencesResponse>> ResetAsync(CancellationToken cancellationToken)
    {
        Calls.Add("reset");
        return Refused() ?? Write(new UserPreferencesResponse(new WorkspacePreferencesDto(), [], [], Stored.Version, null));
    }

    private Task<OperationResult<UserPreferencesResponse>>? Refused() =>
        !Available ? Task.FromResult(Unavailable())
        : WritesUnreachable ? Task.FromResult(OperationResult<UserPreferencesResponse>.Failure(ErrorCodes.Api.Unreachable))
        : null;

    private Task<OperationResult<UserPreferencesResponse>> Write(UserPreferencesResponse next)
    {
        Stored = next with { Version = (Stored.Version ?? 0) + 1, UpdatedAt = Now };
        return Task.FromResult(OperationResult<UserPreferencesResponse>.Success(Stored));
    }

    private static OperationResult<UserPreferencesResponse> Unavailable() =>
        OperationResult<UserPreferencesResponse>.Failure(ErrorCodes.Preferences.Unavailable);
}
