namespace Lettra.Poc.Grille.Ui.Clavier;

/// <summary>
/// Combinaison de touches normalisée « Ctrl+Alt+Shift+Touche », identique côté C# et JS (grid-keyboard.js).
/// Les raccourcis ne sont pas localisés : F4 reste F4 en français comme en anglais.
/// </summary>
public readonly record struct KeyChord(string Key, bool Ctrl = false, bool Alt = false, bool Shift = false)
{
    public static KeyChord Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) throw new FormatException($"Raccourci vide : '{text}'.");
        bool ctrl = false, alt = false, shift = false;
        foreach (var modifier in parts[..^1])
        {
            switch (modifier.ToLowerInvariant())
            {
                case "ctrl": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                default: throw new FormatException($"Modificateur inconnu '{modifier}' dans '{text}'.");
            }
        }
        var key = parts[^1];
        return new KeyChord(key.Length == 1 ? key.ToUpperInvariant() : key, ctrl, alt, shift);
    }

    public override string ToString() =>
        (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "") + Key;
}
