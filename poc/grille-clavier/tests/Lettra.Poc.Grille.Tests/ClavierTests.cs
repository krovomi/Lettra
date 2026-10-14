using Lettra.Poc.Grille.Ui.Clavier;

namespace Lettra.Poc.Grille.Tests;

public class KeyChordTests
{
    [Theory]
    [InlineData("F4", "F4")]
    [InlineData("ctrl+delete", "Ctrl+delete")]
    [InlineData("Ctrl+Shift+d", "Ctrl+Shift+D")]
    [InlineData("Shift+Alt+Ctrl+F10", "Ctrl+Alt+Shift+F10")]
    public void Parse_normalise_au_format_du_module_JS(string saisie, string attendu) =>
        Assert.Equal(attendu, KeyChord.Parse(saisie).ToString());

    [Fact]
    public void Parse_refuse_un_modificateur_inconnu() =>
        Assert.Throws<FormatException>(() => KeyChord.Parse("Hyper+F4"));
}

public class ShortcutRegistryTests
{
    [Fact]
    public async Task Le_scope_le_plus_specifique_gagne_puis_le_global_reprend_apres_Dispose()
    {
        var registre = new ShortcutRegistry();
        var appels = new List<string>();
        registre.PushScope(new KeyboardScope("global").Bind("F4", "Global", _ => appels.Add("global")));
        var grille = registre.PushScope(new KeyboardScope("grille").Bind("F4", "Grille", _ => appels.Add("grille")));

        await registre.TryHandleAsync(KeyChord.Parse("F4"), CommandContext.None);
        grille.Dispose();
        await registre.TryHandleAsync(KeyChord.Parse("F4"), CommandContext.None);

        Assert.Equal(["grille", "global"], appels);
    }

    [Fact]
    public async Task Une_commande_non_executable_laisse_la_main_au_scope_suivant()
    {
        var registre = new ShortcutRegistry();
        var appels = new List<string>();
        registre.PushScope(new KeyboardScope("global").Bind("F10", "Global", _ => appels.Add("global")));
        registre.PushScope(new KeyboardScope("grille").Bind("F10", "Valider", _ => appels.Add("grille"), ctx => ctx.Row >= 0));

        var traite = await registre.TryHandleAsync(KeyChord.Parse("F10"), CommandContext.None);

        Assert.True(traite);
        Assert.Equal(["global"], appels);
    }

    [Fact]
    public void La_barre_affiche_chaque_touche_une_fois_triee_F1_a_F12()
    {
        var registre = new ShortcutRegistry();
        registre.PushScope(new KeyboardScope("global").Bind("F10", "Global", _ => { }).Bind("F1", "Aide", _ => { })
            .Bind("Escape", "Fermer", _ => { }, showInBar: false));
        registre.PushScope(new KeyboardScope("grille").Bind("F10", "Valider", _ => { }).Bind("F4", "Liste", _ => { }).Bind("Insert", "Insérer", _ => { }));

        var barre = registre.VisibleBindings.Select(b => $"{b.Chord} {b.Label}").ToList();

        Assert.Equal(["F1 Aide", "F4 Liste", "F10 Valider", "Insert Insérer"], barre);
    }

    [Fact]
    public void KeysOf_donne_les_touches_a_confier_au_module_JS()
    {
        var scope = new KeyboardScope("grille").Bind("F4", "Liste", _ => { }).Bind("Ctrl+Delete", "Supprimer", _ => { });
        Assert.Equal(["F4", "Ctrl+Delete"], ShortcutRegistry.KeysOf(scope));
    }
}
