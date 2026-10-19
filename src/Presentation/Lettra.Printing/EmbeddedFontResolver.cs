using System.Reflection;
using PdfSharp.Fonts;

namespace Lettra.Printing;

/// <summary>
/// Sert à PDFsharp la seule police des documents, embarquée dans l'assembly : aucun accès aux polices du système,
/// qui n'existent ni dans le navigateur ni de façon identique d'un poste à l'autre.
/// </summary>
internal sealed class EmbeddedFontResolver : IFontResolver
{
    public const string FamilyName = "Liberation Sans";

    private const string Regular = "LiberationSans-Regular";
    private const string Bold = "LiberationSans-Bold";

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, byte[]> Cache = [];

    /// <summary>Installe le résolveur une fois pour tout le processus (réglage global de PDFsharp).</summary>
    public static void EnsureInstalled()
    {
        lock (Gate)
        {
            GlobalFontSettings.FontResolver ??= new EmbeddedFontResolver();
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        // Toute famille demandée retombe sur la police embarquée ; l'italique est simulé.
        new(bold ? Bold : Regular, mustSimulateBold: false, mustSimulateItalic: italic);

    public byte[]? GetFont(string faceName)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(faceName, out byte[]? cached))
            {
                return cached;
            }

            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Lettra.Printing.Fonts.{faceName}.ttf");
            if (stream is null)
            {
                return null;
            }

            using MemoryStream buffer = new();
            stream.CopyTo(buffer);
            byte[] font = buffer.ToArray();
            Cache[faceName] = font;
            return font;
        }
    }
}
