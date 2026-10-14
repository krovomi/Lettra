using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Identity.Local;

/// <summary>Le compte du poste tel qu'il est stocké. Jamais la phrase secrète : seulement son hachage.</summary>
internal sealed record LocalAccountFile
{
    public int Version { get; init; } = 1;

    public required Guid UserId { get; init; }

    public required string DisplayName { get; init; }

    public required byte[] Salt { get; init; }

    public required byte[] Hash { get; init; }

    public required int Iterations { get; init; }

    public int FailedAttempts { get; init; }

    public int Lockouts { get; init; }

    public DateTimeOffset? LockedUntil { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(LocalAccountFile))]
internal sealed partial class LocalAccountJsonContext : JsonSerializerContext;

/// <summary>Lecture et écriture atomique du fichier du compte local.</summary>
internal sealed class LocalAccountStore(IOptions<LocalIdentityOptions> options)
{
    private string Path => System.IO.Path.GetFullPath(options.Value.StorePath);

    public async ValueTask<LocalAccountFile?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        FileStream stream = File.OpenRead(Path);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync(stream, LocalAccountJsonContext.Default.LocalAccountFile, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask WriteAsync(LocalAccountFile account, CancellationToken cancellationToken)
    {
        string? folder = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        // Écriture dans un fichier temporaire puis remplacement : une coupure ne laisse jamais un compte à moitié écrit.
        string temporary = Path + ".tmp";
        FileStream stream = File.Create(temporary);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, account, LocalAccountJsonContext.Default.LocalAccountFile, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, Path, overwrite: true);
    }
}
