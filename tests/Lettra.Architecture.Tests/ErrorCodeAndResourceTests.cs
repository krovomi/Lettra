using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Resources;
using Lettra.Contracts.Common;

namespace Lettra.Architecture.Tests;

/// <summary>
/// i18n : le serveur ne renvoie que des codes. Chaque code émis doit exister dans le catalogue des contrats
/// et avoir une traduction française et anglaise ; aucun texte en dur ne passe.
/// </summary>
public sealed class ErrorCodeAndResourceTests
{
    private static readonly ResourceManager Resources =
        new("Lettra.UI.Shared.Localization.SharedResource", Assemblies.UiShared);

    private static HashSet<string> ContractCodes() => ConstantsOf(typeof(ErrorCodes)).ToHashSet(StringComparer.Ordinal);

    /// <summary>Constantes <c>string</c> d'un type et de ses types imbriqués.</summary>
    private static IEnumerable<string> ConstantsOf(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Concat(type.GetNestedTypes().SelectMany(ConstantsOf));

    /// <summary>Codes émis par le domaine et l'application : les constantes <c>*Code</c> des classes <c>*Errors</c>.</summary>
    private static IEnumerable<string> EmittedCodes() =>
        new[] { Assemblies.Domain, Assemblies.Application }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name.EndsWith("Errors", StringComparison.Ordinal))
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.Name.EndsWith("Code", StringComparison.Ordinal))
                .Select(f => (string)f.GetRawConstantValue()!));

    private static Dictionary<string, string> ResourceSet(CultureInfo culture) =>
        Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.Ordinal);

    [Fact]
    public void Chaque_code_emis_figure_dans_les_contrats() =>
        EmittedCodes().Should().NotBeEmpty().And.BeSubsetOf(ContractCodes());

    [Fact]
    public void Les_annotations_des_contrats_n_utilisent_que_des_codes() =>
        Assemblies.Contracts.GetTypes()
            .SelectMany(t => t.GetProperties())
            .SelectMany(p => p.GetCustomAttributes<ValidationAttribute>())
            .Select(a => a.ErrorMessage)
            .Should().OnlyContain(m => m != null && ContractCodes().Contains(m));

    [Fact]
    public void Chaque_code_des_contrats_est_traduit_en_francais_et_en_anglais()
    {
        Dictionary<string, string> fr = ResourceSet(CultureInfo.InvariantCulture);
        Dictionary<string, string> en = ResourceSet(CultureInfo.GetCultureInfo("en"));

        foreach (string code in ContractCodes())
        {
            fr.Should().ContainKey("Error." + code);
            en.Should().ContainKey("Error." + code);
        }
    }

    [Fact]
    public void Les_ressources_francaises_et_anglaises_ont_les_memes_cles()
    {
        Dictionary<string, string> fr = ResourceSet(CultureInfo.InvariantCulture);
        Dictionary<string, string> en = ResourceSet(CultureInfo.GetCultureInfo("en"));

        en.Keys.Should().BeEquivalentTo(fr.Keys);
        fr.Values.Concat(en.Values).Should().OnlyContain(v => !string.IsNullOrWhiteSpace(v));
    }

    [Fact]
    public void Le_francais_est_la_langue_neutre() =>
        Resources.GetString("Customers.Create.Submit", CultureInfo.InvariantCulture).Should().Be("Créer le client");
}
