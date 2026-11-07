using System.Buffers.Binary;
using System.Security.Cryptography;
using Lettra.Infrastructure.Persistence.Transfer;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Le chiffrement du paquet de réversibilité : ce qui sort s'ouvre avec la phrase secrète, et rien d'autre ne s'ouvre
/// (phrase fausse, paquet tronqué, morceaux déplacés, données ajoutées, fichier étranger).
/// </summary>
public sealed class DossierPackageFormatTests
{
    private const string Passphrase = "cheval agrafe batterie correcte";

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(1 << 20)]
    [InlineData((3 << 20) + 17)]
    public async Task Ce_qui_est_chiffre_se_dechiffre_avec_la_phrase_secrete(int length)
    {
        byte[] content = RandomNumberGenerator.GetBytes(length);
        byte[] package = await EncryptAsync(content);

        package.AsSpan().IndexOf(content.AsSpan(0, Math.Min(length, 64))).Should().Be(length == 0 ? 0 : -1, "rien du contenu n'est en clair");
        (await DecryptAsync(package)).Should().Equal(content);
    }

    [Fact]
    public async Task Deux_paquets_du_meme_contenu_different_sel_et_morceaux()
    {
        byte[] content = RandomNumberGenerator.GetBytes(2000);
        (await EncryptAsync(content)).Should().NotEqual(await EncryptAsync(content));
    }

    [Fact]
    public async Task Une_autre_phrase_secrete_n_ouvre_pas_le_paquet()
    {
        byte[] package = await EncryptAsync(RandomNumberGenerator.GetBytes(100));
        await FluentActions.Awaiting(() => DecryptAsync(package, "une autre phrase secrète"))
            .Should().ThrowAsync<DossierPackageException>().WithMessage("*incorrecte*");
    }

    [Fact]
    public async Task Un_paquet_tronque_ou_prolonge_ne_s_ouvre_pas()
    {
        byte[] package = await EncryptAsync(RandomNumberGenerator.GetBytes((2 << 20) + 5));
        int firstChunkEnd = HeaderLength(package) + 5 + (1 << 20) + 16;

        await FluentActions.Awaiting(() => DecryptAsync(package[..firstChunkEnd])).Should().ThrowAsync<DossierPackageException>("le dernier morceau manque");
        await FluentActions.Awaiting(() => DecryptAsync(package[..^3])).Should().ThrowAsync<DossierPackageException>();
        await FluentActions.Awaiting(() => DecryptAsync([.. package, 0])).Should().ThrowAsync<DossierPackageException>("rien ne suit le dernier morceau");
    }

    [Fact]
    public async Task Des_morceaux_echanges_ou_un_drapeau_change_ne_s_ouvrent_pas()
    {
        byte[] package = await EncryptAsync(RandomNumberGenerator.GetBytes((3 << 20) + 5));
        int header = HeaderLength(package);
        int chunk = 5 + (1 << 20) + 16;
        byte[] swapped = [.. package];
        package.AsSpan(header, chunk).CopyTo(swapped.AsSpan(header + chunk));
        package.AsSpan(header + chunk, chunk).CopyTo(swapped.AsSpan(header));
        await FluentActions.Awaiting(() => DecryptAsync(swapped)).Should().ThrowAsync<DossierPackageException>();

        // Le premier morceau prétendu dernier, suivi du reste : refusé, le drapeau est authentifié.
        byte[] flagged = [.. package];
        flagged[header] = 1;
        await FluentActions.Awaiting(() => DecryptAsync(flagged)).Should().ThrowAsync<DossierPackageException>();

        byte[] altered = [.. package];
        altered[header + 100] ^= 0x01;
        await FluentActions.Awaiting(() => DecryptAsync(altered)).Should().ThrowAsync<DossierPackageException>();
    }

    [Fact]
    public async Task Un_fichier_etranger_ou_un_en_tete_affaibli_est_refuse()
    {
        await FluentActions.Awaiting(() => DecryptAsync([.. "PK\u0003\u0004"u8, .. new byte[200]]))
            .Should().ThrowAsync<DossierPackageException>().WithMessage("*pas un paquet*");

        // Un en-tête qui demande 1 000 itérations (dérivation affaiblie) n'est pas lu.
        byte[] package = await EncryptAsync(RandomNumberGenerator.GetBytes(10));
        int magic = "LETTRA-PAQUET-DOSSIER\n"u8.Length;
        int headerEnd = HeaderLength(package);
        string header = System.Text.Encoding.UTF8.GetString(package, magic + 4, headerEnd - magic - 4);
        byte[] weakHeader = System.Text.Encoding.UTF8.GetBytes(header.Replace("\"iterations\": 600000", "\"iterations\": 1000", StringComparison.Ordinal));
        weakHeader.Length.Should().BeLessThan(headerEnd - magic - 4);
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, weakHeader.Length);
        byte[] weakened = [.. package[..magic], .. length, .. weakHeader, .. package[headerEnd..]];
        await FluentActions.Awaiting(() => DecryptAsync(weakened)).Should().ThrowAsync<DossierPackageException>().WithMessage("*format inconnu*");
    }

    private static int HeaderLength(byte[] package)
    {
        int magic = "LETTRA-PAQUET-DOSSIER\n"u8.Length;
        return magic + 4 + BinaryPrimitives.ReadInt32LittleEndian(package.AsSpan(magic));
    }

    private static async Task<byte[]> EncryptAsync(byte[] content)
    {
        using MemoryStream source = new(content);
        using MemoryStream package = new();
        await DossierPackageFormat.EncryptAsync(source, package, Passphrase, default);
        return package.ToArray();
    }

    private static async Task<byte[]> DecryptAsync(byte[] package, string passphrase = Passphrase)
    {
        using MemoryStream source = new(package);
        using MemoryStream content = new();
        await DossierPackageFormat.DecryptAsync(source, content, passphrase, default);
        return content.ToArray();
    }
}
