using Lettra.Domain.Common;

namespace Lettra.Application.Abstractions.Persistence;

/// <summary>
/// Commande qui modifie un document existant à partir d'une version lue (ADR-0013). L'API la remplit depuis
/// l'en-tête <c>If-Match</c>, la gateway in-process depuis la version affichée à l'écran, la synchronisation
/// (phase 4) depuis l'intention rejouée. <c>null</c> : pas de contrôle (outils internes, migrations de données).
/// </summary>
public interface IVersionedCommand
{
    long? ExpectedVersion { get; }
}

/// <summary>
/// Levée par <see cref="IUnitOfWork.SaveChangesAsync"/> quand une ligne a changé entre sa lecture et son
/// écriture. Le pipeline la traduit en <see cref="ConcurrencyErrors.VersionMismatch"/> : elle ne sort jamais.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
        : base("Le document a été modifié par quelqu'un d'autre depuis sa lecture.")
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Codes d'erreur de la concurrence optimiste.</summary>
public static class ConcurrencyErrors
{
    public const string VersionMismatchCode = "Concurrency.VersionMismatch";
    public const string PreconditionRequiredCode = "Concurrency.PreconditionRequired";

    /// <summary>La version envoyée n'est plus celle du serveur : relire, fusionner, renvoyer (HTTP 412).</summary>
    public static Error VersionMismatch => new(VersionMismatchCode, ErrorKind.PreconditionFailed);

    /// <summary>Écriture sans <c>If-Match</c> sur une route qui l'exige (HTTP 428).</summary>
    public static Error PreconditionRequired => new(PreconditionRequiredCode, ErrorKind.PreconditionFailed);
}

/// <summary>Le contrôle à faire dans un handler, juste après avoir chargé l'agrégat.</summary>
public static class Concurrency
{
    /// <summary>
    /// Échec si la version attendue n'est pas celle lue. L'écart entre la lecture et l'écriture est couvert par
    /// la persistance (la mise à jour est conditionnée par la version lue).
    /// </summary>
    public static Result Check(IVersioned document, long? expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(document);
        return expectedVersion is null || expectedVersion.Value == document.Version
            ? Result.Success()
            : ConcurrencyErrors.VersionMismatch;
    }

    /// <summary>
    /// Le préambule des commandes versionnées : le document chargé, ou <paramref name="notFound"/> s'il n'existe pas,
    /// ou <see cref="ConcurrencyErrors.VersionMismatch"/> s'il a changé depuis la version lue.
    /// </summary>
    public static Result<T> Load<T>(T? document, Error notFound, long? expectedVersion)
        where T : class, IVersioned
    {
        if (document is null)
        {
            return notFound;
        }

        Result version = Check(document, expectedVersion);
        return version.IsFailure ? Result<T>.Failure(version.Errors) : document;
    }
}
