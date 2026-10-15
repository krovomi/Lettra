using Lettra.Domain.Common;

namespace Lettra.Supervision.Domain;

/// <summary>Niveau de la portée d'une alerte, du plus large au plus étroit.</summary>
public enum ScopeLevel
{
    Platform = 1,
    Cell = 2,
    Cabinet = 3,
    Group = 4,
    Dossier = 5,
    Device = 6,
}

/// <summary>
/// Portée d'une alerte : uniquement des identifiants techniques, jamais un nom (annexe G §2). Une portée
/// étroite porte aussi ses parents (un dossier connaît son cabinet) pour que le routage puisse remonter.
/// </summary>
public sealed record AlertScope
{
    private AlertScope(ScopeLevel level, string? cell, Guid? cabinet, Guid? group, Guid? dossier, Guid? device)
    {
        Level = level;
        Cell = cell;
        Cabinet = cabinet;
        Group = group;
        Dossier = dossier;
        Device = device;
    }

    public ScopeLevel Level { get; }

    public string? Cell { get; }

    public Guid? Cabinet { get; }

    public Guid? Group { get; }

    public Guid? Dossier { get; }

    public Guid? Device { get; }

    public static AlertScope Platform() => new(ScopeLevel.Platform, null, null, null, null, null);

    public static Result<AlertScope> ForCell(string cell) =>
        string.IsNullOrWhiteSpace(cell)
            ? Result<AlertScope>.Failure(SupervisionErrors.ScopeIncomplete)
            : new AlertScope(ScopeLevel.Cell, cell, null, null, null, null);

    public static Result<AlertScope> ForCabinet(string? cell, Guid cabinet) =>
        cabinet == Guid.Empty
            ? Result<AlertScope>.Failure(SupervisionErrors.ScopeIncomplete)
            : new AlertScope(ScopeLevel.Cabinet, cell, cabinet, null, null, null);

    public static Result<AlertScope> ForGroup(string? cell, Guid cabinet, Guid group) =>
        cabinet == Guid.Empty || group == Guid.Empty
            ? Result<AlertScope>.Failure(SupervisionErrors.ScopeIncomplete)
            : new AlertScope(ScopeLevel.Group, cell, cabinet, group, null, null);

    public static Result<AlertScope> ForDossier(string? cell, Guid cabinet, Guid dossier) =>
        cabinet == Guid.Empty || dossier == Guid.Empty
            ? Result<AlertScope>.Failure(SupervisionErrors.ScopeIncomplete)
            : new AlertScope(ScopeLevel.Dossier, cell, cabinet, null, dossier, null);

    public static Result<AlertScope> ForDevice(string? cell, Guid cabinet, Guid device) =>
        cabinet == Guid.Empty || device == Guid.Empty
            ? Result<AlertScope>.Failure(SupervisionErrors.ScopeIncomplete)
            : new AlertScope(ScopeLevel.Device, cell, cabinet, null, null, device);

    /// <summary>Portée relue de la base de supervision, telle qu'elle a été enregistrée.</summary>
    public static AlertScope Restore(ScopeLevel level, string? cell, Guid? cabinet, Guid? group, Guid? dossier, Guid? device) =>
        new(level, cell, cabinet, group, dossier, device);

    /// <summary>Clé stable de la portée, utilisée dans l'empreinte de dédoublonnage.</summary>
    public string Key => Level switch
    {
        ScopeLevel.Platform => "platform",
        ScopeLevel.Cell => $"cell:{Cell}",
        ScopeLevel.Cabinet => $"cabinet:{Cabinet}",
        ScopeLevel.Group => $"group:{Group}",
        ScopeLevel.Dossier => $"dossier:{Dossier}",
        ScopeLevel.Device => $"device:{Device}",
        _ => throw new InvalidOperationException($"Niveau de portée inconnu : {Level}."),
    };

    /// <summary>Vrai si l'alerte concerne l'exploitation de l'éditeur plutôt qu'un cabinet.</summary>
    public bool IsTechnical => Level is ScopeLevel.Platform or ScopeLevel.Cell;
}
