using Lettra.Domain.Banking;

namespace Lettra.Domain.Tests.Banking;

public sealed class IbanFingerprintTests
{
    private static readonly Guid Dossier = new("0199e3a0-0000-7000-8000-00000000a001");
    private static readonly byte[] Key = [.. Enumerable.Range(1, 32).Select(i => (byte)i)];

    [Fact]
    public void L_empreinte_est_un_hmac_avec_la_cle_du_dossier_et_ne_depend_pas_de_la_mise_en_forme()
    {
        string digest = BankText.IbanDigest("FR76 3000 4000 0312 3456 7890 143", Dossier)!;

        string fingerprint = BankText.KeyedIbanFingerprint(digest, Key);

        fingerprint.Should().StartWith(BankText.KeyedIbanPrefix).And.HaveLength(64).And.NotContain(digest[..16]);
        BankText.KeyedIbanFingerprint(BankText.IbanDigest("fr7630004000031234567890143", Dossier)!, Key).Should().Be(fingerprint);
        BankText.IsKeyedIbanFingerprint(fingerprint).Should().BeTrue();
        BankText.IsKeyedIbanFingerprint(digest).Should().BeFalse("un condensé sans clé est en hexadécimal, sans le préfixe");
    }

    [Fact]
    public void Sans_la_cle_on_ne_retrouve_pas_l_empreinte()
    {
        string digest = BankText.IbanDigest("FR7630004000031234567890143", Dossier)!;
        byte[] otherKey = [.. Key.Reverse()];

        BankText.KeyedIbanFingerprint(digest, otherKey).Should().NotBe(BankText.KeyedIbanFingerprint(digest, Key));
    }

    [Fact]
    public void Un_iban_invalide_n_a_pas_d_empreinte()
    {
        BankText.IbanDigest(null, Dossier).Should().BeNull();
        BankText.IbanDigest("FR76", Dossier).Should().BeNull();
    }
}
