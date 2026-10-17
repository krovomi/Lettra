using System.Net;
using System.Security.Cryptography;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Infrastructure.Speech.OnDevice;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lettra.Speech.Tests;

/// <summary>Modèle du poste : vérifié par empreinte avant d'être chargé, téléchargé une fois au plus.</summary>
public sealed class WhisperModelStoreTests : IDisposable
{
    private static readonly byte[] Model = "un faux modèle ggml"u8.ToArray();

#pragma warning disable CA5350 // Même empreinte que whisper.cpp : SHA-1, pour l'intégrité.
    private static readonly WhisperModelFile Expected = new("ggml-test.bin", Convert.ToHexStringLower(SHA1.HashData(Model)), Convert.ToHexStringLower(SHA256.HashData(Model)));
#pragma warning restore CA5350

    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-voix-").FullName;

    [Fact]
    public async Task Un_modele_present_et_intact_est_pris_sans_telechargement()
    {
        await File.WriteAllBytesAsync(Path.Combine(_folder, Expected.Name), Model);
        Downloads downloads = new(HttpStatusCode.OK, []);
        using WhisperModelStore store = Store(downloads);

        Result<string> path = await store.EnsureAsync(CancellationToken.None);

        path.Value.Should().Be(Path.Combine(_folder, Expected.Name));
        downloads.Count.Should().Be(0);
    }

    [Fact]
    public async Task Un_modele_absent_est_telecharge_puis_verifie_une_seule_fois()
    {
        Downloads downloads = new(HttpStatusCode.OK, Model);
        using WhisperModelStore store = Store(downloads);

        Result<string> first = await store.EnsureAsync(CancellationToken.None);
        Result<string> second = await store.EnsureAsync(CancellationToken.None);

        first.Value.Should().Be(second.Value);
        (await File.ReadAllBytesAsync(first.Value)).Should().Equal(Model);
        downloads.Count.Should().Be(1);
        downloads.LastUri!.AbsoluteUri.Should().Be("https://modeles.test/ggml/ggml-test.bin");
    }

    [Fact]
    public async Task Un_modele_altere_n_est_jamais_garde()
    {
        await File.WriteAllBytesAsync(Path.Combine(_folder, Expected.Name), "altéré"u8.ToArray());
        using WhisperModelStore store = Store(new Downloads(HttpStatusCode.OK, "aussi altéré"u8.ToArray()));

        Result<string> path = await store.EnsureAsync(CancellationToken.None);

        path.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Speech.ModelUnavailable);
        Directory.GetFiles(_folder).Should().BeEmpty("ni le fichier altéré ni le téléchargement partiel ne restent");
    }

    [Fact]
    public async Task Sans_telechargement_permis_un_modele_absent_est_indisponible()
    {
        Downloads downloads = new(HttpStatusCode.OK, Model);
        using WhisperModelStore store = Store(downloads, allowDownload: false);

        (await store.EnsureAsync(CancellationToken.None)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Speech.ModelUnavailable);
        downloads.Count.Should().Be(0);
    }

    [Fact]
    public async Task Une_source_en_erreur_rend_le_modele_indisponible()
    {
        using WhisperModelStore store = Store(new Downloads(HttpStatusCode.NotFound, []));

        (await store.EnsureAsync(CancellationToken.None)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Speech.ModelUnavailable);
    }

    [Fact]
    public async Task Un_sha_256_different_ecarte_le_modele_meme_si_le_sha_1_concorde()
    {
        await File.WriteAllBytesAsync(Path.Combine(_folder, Expected.Name), Model);
        Downloads downloads = new(HttpStatusCode.OK, Model);
        using WhisperModelStore store = Store(downloads, file: Expected with { Sha256 = new string('0', 64) });

        Result<string> path = await store.EnsureAsync(CancellationToken.None);

        path.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Speech.ModelUnavailable);
        downloads.Count.Should().Be(1, "le fichier présent est écarté, puis le téléchargement aussi");
        Directory.GetFiles(_folder).Should().BeEmpty();
    }

    [Fact]
    public async Task Sans_sha_256_epingle_le_sha_1_suffit()
    {
        await File.WriteAllBytesAsync(Path.Combine(_folder, Expected.Name), Model);
        using WhisperModelStore store = Store(new Downloads(HttpStatusCode.OK, []), file: Expected with { Sha256 = null });

        (await store.EnsureAsync(CancellationToken.None)).Value.Should().Be(Path.Combine(_folder, Expected.Name));
    }

    [Fact]
    public void Les_modeles_proposes_ont_tous_une_empreinte_publiee() =>
        WhisperModelStore.Files.Values.Should().OnlyContain(f => f.Name.StartsWith("ggml-", StringComparison.Ordinal) && f.Sha1.Length == 40
                                                                 && (f.Sha256 == null || (f.Sha256.Length == 64 && f.Sha256.All(c => char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c)))));

    [Fact]
    public void Le_modele_de_la_demonstration_a_un_sha_256_epingle() =>
        WhisperModelStore.Files[WhisperModelSize.Base].Sha256.Should().HaveLength(64);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private WhisperModelStore Store(Downloads downloads, bool allowDownload = true, WhisperModelFile? file = null) => new(
        new OnDeviceSpeechOptions { ModelDirectory = _folder, AllowDownload = allowDownload, DownloadBaseAddress = new Uri("https://modeles.test/ggml/") },
        downloads,
        NullLogger<WhisperModelStore>.Instance)
    {
        FileOverride = file ?? Expected,
    };

    private sealed class Downloads(HttpStatusCode status, byte[] content) : HttpMessageHandler, IHttpClientFactory
    {
        public int Count { get; private set; }

        public Uri? LastUri { get; private set; }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(content) });
        }
    }
}
