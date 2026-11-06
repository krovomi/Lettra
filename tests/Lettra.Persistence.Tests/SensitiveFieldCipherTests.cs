using System.Security.Cryptography;
using Lettra.Infrastructure.Persistence.Security;

namespace Lettra.Persistence.Tests;

public sealed class SensitiveFieldCipherTests
{
    private static readonly byte[] Dek = RandomNumberGenerator.GetBytes(32);

    [Theory]
    [InlineData("FR7630006000011234567890189")]
    [InlineData("élodie.dupré@exemple.fr")]
    [InlineData("")]
    public void Aller_retour_et_valeur_cachee(string clear)
    {
        SensitiveFieldCipher cipher = new(Dek);

        string stored = cipher.Encrypt(clear);

        stored.Should().StartWith(SensitiveField.EncryptedPrefix);
        if (clear.Length > 0)
        {
            stored.Should().NotContain(clear);
        }

        cipher.Decrypt(stored).Should().Be(clear);
    }

    [Fact]
    public void Deterministe_pour_une_cle_et_different_d_une_cle_a_l_autre()
    {
        SensitiveFieldCipher cipher = new(Dek);
        SensitiveFieldCipher other = new(RandomNumberGenerator.GetBytes(32));

        cipher.Encrypt("compta@martin.fr").Should().Be(cipher.Encrypt("compta@martin.fr"))
            .And.NotBe(cipher.Encrypt("compta@martin.com"))
            .And.NotBe(other.Encrypt("compta@martin.fr"));
    }

    [Fact]
    public void Une_valeur_alteree_ou_d_un_autre_dossier_est_refusee()
    {
        SensitiveFieldCipher cipher = new(Dek);
        string stored = cipher.Encrypt("FR7630006000011234567890189");
        byte[] raw = Convert.FromBase64String(stored[SensitiveField.EncryptedPrefix.Length..]);
        raw[^1] ^= 1;

        Action tampered = () => cipher.Decrypt(SensitiveField.EncryptedPrefix + Convert.ToBase64String(raw));
        Action foreign = () => new SensitiveFieldCipher(RandomNumberGenerator.GetBytes(32)).Decrypt(stored);
        Action truncated = () => cipher.Decrypt(SensitiveField.EncryptedPrefix + Convert.ToBase64String(new byte[10]));

        tampered.Should().Throw<CryptographicException>();
        foreign.Should().Throw<CryptographicException>();
        truncated.Should().Throw<CryptographicException>().WithMessage("*tronqué*");
    }

    [Fact]
    public void Une_DEK_de_mauvaise_taille_est_refusee()
    {
        Action build = () => _ = new SensitiveFieldCipher(new byte[16]);

        build.Should().Throw<CryptographicException>();
    }

    [Theory]
    [InlineData(34)]
    [InlineData(254)]
    public void La_colonne_tient_le_pire_cas_chiffre(int maxCharacters)
    {
        SensitiveFieldCipher cipher = new(Dek);

        cipher.Encrypt(new string('€', maxCharacters)).Length.Should().BeLessThanOrEqualTo(SensitiveField.StoredLength(maxCharacters));
    }

    [Fact]
    public void Marque_retiree_et_valeur_non_marquee_rendue_telle_quelle()
    {
        SensitiveField.Unmark(SensitiveField.PendingMarker + "abc").Should().Be("abc");
        SensitiveField.Unmark("abc").Should().Be("abc");
    }
}
