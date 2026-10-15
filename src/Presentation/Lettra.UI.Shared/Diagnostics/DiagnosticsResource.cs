namespace Lettra.UI.Shared.Diagnostics;

/// <summary>
/// Textes des écrans de diagnostic (français neutre, anglais en <c>.en</c>), séparés des ressources partagées :
/// ils ne servent qu'aux mesures sur poste et ne gonflent pas le catalogue des écrans métier.
/// </summary>
#pragma warning disable CA1052 // Type marqueur pour IStringLocalizer<T>, jamais instancié.
public sealed class DiagnosticsResource
#pragma warning restore CA1052
{
    private DiagnosticsResource()
    {
    }
}
