namespace Lettra.Domain.Common;

/// <summary>
/// Agrégat protégé par la concurrence optimiste (ADR-0013). La version vaut 1 à la création et avance de 1 à
/// chaque écriture ; elle est exposée en <c>ETag</c>. Le domaine ne la modifie jamais : c'est la persistance
/// qui l'avance, et qui refuse d'écrire si la ligne a changé depuis sa lecture.
/// </summary>
public interface IVersioned
{
    long Version { get; }
}
