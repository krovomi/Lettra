using System.Globalization;
using System.Text.RegularExpressions;

namespace Lettra.UI.Tests.Design;

/// <summary>
/// Contrastes des thèmes (WCAG 2.2) lus dans <c>lettra-tokens.css</c> : texte à 4,5:1 (7:1 en contraste élevé),
/// bordures de champ et anneau de focus à 3:1. Un jeton modifié qui casse un contraste fait échouer la CI.
/// </summary>
public sealed partial class ThemeContrastTests
{
    private static readonly string Css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Design", "lettra-tokens.css"));

    public static TheoryData<string> Themes => ["light", "dark", "slate", "contrast"];

    private static readonly (string Text, string Background)[] TextPairs =
    [
        ("--color-fg", "--color-bg"), ("--color-fg", "--color-surface"), ("--color-fg", "--color-surface-2"),
        ("--color-fg-muted", "--color-bg"), ("--color-fg-muted", "--color-surface"), ("--color-fg-muted", "--color-surface-2"),
        ("--color-on-primary", "--color-primary"), ("--color-on-primary", "--color-primary-hover"), ("--color-primary", "--color-surface"),
        ("--color-primary-soft-fg", "--color-primary-soft"), ("--color-success-fg", "--color-success-bg"),
        ("--color-warning-fg", "--color-warning-bg"), ("--color-danger-fg", "--color-danger-bg"), ("--color-danger-fg", "--color-surface"),
        ("--color-info-fg", "--color-info-bg"), ("--color-neutral-fg", "--color-neutral-bg"),
        ("--nav-fg", "--nav-bg"), ("--nav-fg-muted", "--nav-bg"), ("--nav-active-fg", "--nav-active-bg"), ("--on-brand-mark", "--brand-mark"),
    ];

    private static readonly (string Ui, string Background)[] UiPairs =
    [
        ("--color-input-border", "--color-surface"), ("--color-focus", "--color-surface"),
    ];

    [Theory]
    [MemberData(nameof(Themes))]
    public void Les_textes_sont_lisibles(string theme)
    {
        Dictionary<string, string> jetons = Jetons(theme);
        double minimum = theme == "contrast" ? 7.0 : 4.5;

        string[] echecs =
        [
            .. TextPairs
                .Select(p => (p.Text, p.Background, Ratio: Ratio(jetons[p.Text], jetons[p.Background])))
                .Where(p => p.Ratio < minimum)
                .Select(p => $"{p.Text} sur {p.Background} : {p.Ratio:0.00}"),
        ];

        echecs.Should().BeEmpty($"le thème {theme} doit atteindre {minimum}:1");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Les_bordures_de_champ_et_le_focus_se_voient(string theme)
    {
        Dictionary<string, string> jetons = Jetons(theme);

        string[] echecs =
        [
            .. UiPairs
                .Select(p => (p.Ui, p.Background, Ratio: Ratio(jetons[p.Ui], jetons[p.Background])))
                .Where(p => p.Ratio < 3.0)
                .Select(p => $"{p.Ui} sur {p.Background} : {p.Ratio:0.00}"),
        ];

        echecs.Should().BeEmpty();
    }

    [Fact]
    public void Le_sombre_du_systeme_reprend_le_theme_sombre_choisi()
    {
        Match systeme = SystemDarkBlock().Match(Css);
        systeme.Success.Should().BeTrue();

        Declarations(systeme.Groups["body"].Value).Should().Equal(Declarations(Block("dark")));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Chaque_theme_definit_les_memes_jetons(string theme) =>
        Declarations(Block(theme)).Keys.Should().BeEquivalentTo(Declarations(Block("light")).Keys);

    private static Dictionary<string, string> Jetons(string theme) =>
        Declarations(Block(theme)).Where(d => d.Value.StartsWith('#')).ToDictionary(d => d.Key, d => d.Value);

    private static string Block(string theme)
    {
        Match match = new Regex($@":root\[data-theme=""{theme}""\]\s*\{{(?<body>[^}}]*)\}}").Match(Css);
        match.Success.Should().BeTrue($"le thème {theme} doit exister");
        return match.Groups["body"].Value;
    }

    private static Dictionary<string, string> Declarations(string block) =>
        Declaration().Matches(block).ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value.Trim());

    private static double Ratio(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        double Canal(int start)
        {
            double c = int.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Canal(1)) + (0.7152 * Canal(3)) + (0.0722 * Canal(5));
    }

    [GeneratedRegex(@"(?<name>--[\w-]+|color-scheme)\s*:\s*(?<value>[^;]+);")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"@media \(prefers-color-scheme: dark\)\s*\{\s*:root:not\(\[data-theme\]\)\s*\{(?<body>[^}]*)\}")]
    private static partial Regex SystemDarkBlock();
}
