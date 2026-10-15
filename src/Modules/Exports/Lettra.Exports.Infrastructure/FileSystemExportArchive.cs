using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application;
using Microsoft.Extensions.Options;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Dépôt des exports en masse sur disque : <c>&lt;racine&gt;/&lt;lot&gt;/&lt;dossier&gt;/&lt;fichier&gt;</c>, écrit dans
/// un fichier temporaire puis renommé, pour qu'un fichier présent soit toujours complet (la reprise s'y fie).
/// </summary>
internal sealed class FileSystemExportArchive(IOptions<FecBulkExportOptions> options) : IExportArchive
{
    public const string ReportFileName = "rapport.json";

    public Task<bool> ExistsAsync(string batch, DossierId dossier, string fileName, CancellationToken cancellationToken) =>
        Task.FromResult(File.Exists(PathOf(batch, dossier.Value.ToString("D"), fileName)));

    public Task WriteAsync(string batch, DossierId dossier, string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
        WriteAtomicallyAsync(PathOf(batch, dossier.Value.ToString("D"), fileName), content, cancellationToken);

    public Task WriteReportAsync(string batch, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
        WriteAtomicallyAsync(PathOf(batch, null, ReportFileName), content, cancellationToken);

    private string PathOf(string batch, string? dossier, string fileName)
    {
        string root = options.Value.OutputDirectory
            ?? throw new InvalidOperationException($"{FecBulkExportOptions.SectionName}:OutputDirectory est obligatoire.");
        foreach (string part in new[] { batch, dossier ?? string.Empty, fileName })
        {
            if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part is "." or "..")
            {
                throw new ArgumentException("Nom de fichier d'export invalide.", nameof(fileName));
            }
        }

        return dossier is null ? Path.Combine(root, batch, fileName) : Path.Combine(root, batch, dossier, fileName);
    }

    private static async Task WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".partiel";
        await File.WriteAllBytesAsync(temporary, content, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }
}
