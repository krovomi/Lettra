using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.SharedKernel;

public sealed class IbanTests
{
    [Theory]
    [InlineData("FR7630006000011234567890189")]
    [InlineData("fr76 3000 6000 0112 3456 7890 189")]
    [InlineData("DE89370400440532013000")]
    [InlineData("BE68539007547034")]
    public void Un_iban_valide_est_accepte_sous_forme_compacte(string raw) =>
        Iban.Create(raw).Value.Value.Should().NotContain(" ").And.Be(raw.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant());

    [Theory]
    [InlineData("")]
    [InlineData("FR7630006000011234567890188")]
    [InlineData("FR763000600001123456789018")]
    [InlineData("1276300060000112345678901")]
    [InlineData("FR76-3000-6000-0112-3456-7890-189")]
    public void Un_iban_invalide_est_refuse(string raw) =>
        Iban.Create(raw).Errors.Should().ContainSingle().Which.Code.Should().Be(SharedKernelErrors.IbanInvalidCode);

    [Fact]
    public void L_affichage_groupe_par_quatre() =>
        Iban.Create("FR7630006000011234567890189").Value.Formatted().Should().Be("FR76 3000 6000 0112 3456 7890 189");
}
