using System.IO.Compression;
using System.Text;
using Lettra.Infrastructure.Persistence.Integrity;

namespace Lettra.Persistence.Tests;

/// <summary>Une archive abîmée ou modifiée est refusée comme mal formée, sans exception.</summary>
public sealed class SealedArchiveFormatTests
{
    private const string Closing = """{"number":1,"keyId":"p256-0000000000000000"}""";

    [Theory]
    [InlineData("null")]
    [InlineData("""{"format":"lettra-archive-scellee-v1","dossier":"00000000-0000-0000-0000-000000000001","closingNumber":1,"closingHash":"x","keyId":"p256-0000000000000000","files":null}""")]
    [InlineData("""{"format":"lettra-archive-scellee-v1"}""")]
    [InlineData("pas du JSON")]
    public void Un_manifeste_vide_incomplet_ou_illisible_est_un_format_invalide(string manifest)
    {
        byte[] archive = Zip((SealedArchiveFormat.ManifestFile, manifest), (SealedArchiveFormat.ClosingFile, Closing));

        SealedArchiveFormat.Verify(archive, "p256-0000000000000000", out string? failure).Should().BeFalse();
        failure.Should().Be("format");
    }

    [Fact]
    public void Une_archive_sans_manifeste_ou_qui_n_est_pas_un_zip_est_un_format_invalide()
    {
        SealedArchiveFormat.Verify(Zip((SealedArchiveFormat.ClosingFile, Closing)), "p256-0000000000000000", out string? missing).Should().BeFalse();
        SealedArchiveFormat.Verify(Encoding.UTF8.GetBytes("pas un zip"), "p256-0000000000000000", out string? notZip).Should().BeFalse();

        missing.Should().Be("format");
        notZip.Should().Be("format");
    }

    private static byte[] Zip(params (string Name, string Content)[] files)
    {
        using MemoryStream output = new();
        using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in files)
            {
                using Stream stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return output.ToArray();
    }
}
