namespace Lettra.Domain.Common;

/// <summary>Nature d'une erreur, indépendante du transport (traduite en statut HTTP par l'API).</summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    Unexpected,

    /// <summary>L'écriture part d'une version périmée du document (HTTP 412, ADR-0013).</summary>
    PreconditionFailed,
}

/// <summary>
/// Erreur métier. Le domaine ne renvoie <b>jamais de texte</b> : seulement un code stable
/// (<c>Customer.SirenRequired</c>) que l'interface traduit dans la langue de l'utilisateur.
/// </summary>
/// <param name="Code">Code stable, repris à l'identique dans <c>Lettra.Contracts.ErrorCodes</c>.</param>
/// <param name="Kind">Nature de l'erreur.</param>
/// <param name="Target">Champ concerné, quand l'erreur porte sur une saisie.</param>
public sealed record Error(string Code, ErrorKind Kind, string? Target = null)
{
    public static Error Validation(string code, string? target = null) => new(code, ErrorKind.Validation, target);

    public static Error NotFound(string code) => new(code, ErrorKind.NotFound);

    public static Error Conflict(string code) => new(code, ErrorKind.Conflict);

    public Error For(string target) => this with { Target = target };
}
