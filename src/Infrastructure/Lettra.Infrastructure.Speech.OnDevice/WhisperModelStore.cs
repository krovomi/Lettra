using System.Security.Cryptography;
using Lettra.Application.Features.Speech;
using Lettra.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Lettra.Infrastructure.Speech.OnDevice;

/// <summary>
/// Modèle ggml du poste : présent et intact, ou téléchargé une fois puis vérifié. Un fichier dont l'empreinte ne
/// correspond pas n'est jamais chargé (un modèle altéré pourrait transcrire autre chose que ce qui est dit).
/// </summary>
internal sealed partial class WhisperModelStore(OnDeviceSpeechOptions options, IHttpClientFactory http, ILogger<WhisperModelStore> logger) : IDisposable
{
    /// <summary>Fichiers du dépôt whisper.cpp, avec la taille et l'empreinte SHA-1 publiées par whisper.cpp (models/README.md).</summary>
    internal static readonly IReadOnlyDictionary<WhisperModelSize, WhisperModelFile> Files = new Dictionary<WhisperModelSize, WhisperModelFile>
    {
        [WhisperModelSize.Tiny] = new("ggml-tiny.bin", "bd577a113a864445d4c299885e0cb97d4ba92b5f"),
        [WhisperModelSize.Base] = new("ggml-base.bin", "465707469ff3a37a2b9b8d8f89f2f99de7299dac"),
        [WhisperModelSize.Small] = new("ggml-small.bin", "55356645c2b361a969dfd0ef2c5a50d530afd8d5"),
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _verified;

    /// <summary>Autre fichier attendu que ceux de whisper.cpp : pour les tests seulement.</summary>
    internal WhisperModelFile? FileOverride { get; init; }

    public WhisperModelFile File => FileOverride ?? Files[options.Model];

    public string PathOnDisk => Path.Combine(options.ModelDirectory, File.Name);

    /// <summary>Le chemin d'un modèle vérifié, en le téléchargeant si besoin et si c'est permis.</summary>
    public async Task<Result<string>> EnsureAsync(CancellationToken cancellationToken)
    {
        if (_verified is not null)
        {
            return _verified;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_verified is not null)
            {
                return _verified;
            }

            if (System.IO.File.Exists(PathOnDisk))
            {
                if (await MatchesAsync(PathOnDisk, cancellationToken).ConfigureAwait(false))
                {
                    _verified = PathOnDisk;
                    return _verified;
                }

                LogCorrupted(logger, File.Name);
                System.IO.File.Delete(PathOnDisk);
            }

            if (!options.AllowDownload)
            {
                return SpeechErrors.ModelUnavailable;
            }

            return await DownloadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Result<string>> DownloadAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.ModelDirectory);
        string partial = PathOnDisk + ".part";
        try
        {
            using HttpClient client = http.CreateClient(nameof(WhisperModelStore));
            Uri source = new(options.DownloadBaseAddress, File.Name);
            using HttpResponseMessage response = await client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogDownloadFailed(logger, File.Name, ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
                return SpeechErrors.ModelUnavailable;
            }

            Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (body.ConfigureAwait(false))
            {
                FileStream target = new(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                await using (target.ConfigureAwait(false))
                {
                    await body.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                }
            }

            if (!await MatchesAsync(partial, cancellationToken).ConfigureAwait(false))
            {
                LogCorrupted(logger, File.Name);
                System.IO.File.Delete(partial);
                return SpeechErrors.ModelUnavailable;
            }

            System.IO.File.Move(partial, PathOnDisk, overwrite: true);
            _verified = PathOnDisk;
            return _verified;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException
                                          || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogDownloadFailed(logger, File.Name, exception.GetType().Name);
            if (System.IO.File.Exists(partial))
            {
                System.IO.File.Delete(partial);
            }

            return SpeechErrors.ModelUnavailable;
        }
    }

    private async Task<bool> MatchesAsync(string path, CancellationToken cancellationToken)
    {
        FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
#pragma warning disable CA5350 // whisper.cpp ne publie que des SHA-1 : ils servent à vérifier l'intégrité du fichier téléchargé, pas à signer.
            byte[] hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
#pragma warning restore CA5350
            return Convert.ToHexStringLower(hash) == File.Sha1;
        }
    }

    public void Dispose() => _gate.Dispose();

    [LoggerMessage(EventId = 7200, Level = LogLevel.Warning, Message = "Modèle vocal {Model} : empreinte incorrecte, fichier écarté")]
    private static partial void LogCorrupted(ILogger logger, string model);

    [LoggerMessage(EventId = 7201, Level = LogLevel.Warning, Message = "Modèle vocal {Model} : téléchargement impossible ({Reason})")]
    private static partial void LogDownloadFailed(ILogger logger, string model, string reason);
}

internal sealed record WhisperModelFile(string Name, string Sha1);
