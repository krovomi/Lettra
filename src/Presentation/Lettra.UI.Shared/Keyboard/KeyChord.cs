namespace Lettra.UI.Shared.Keyboard;

/// <summary>
/// Combinaison de touches normalisée « Ctrl+Alt+Shift+Touche », identique côté C# et JS (grid-keyboard.js).
/// Les raccourcis ne sont pas localisés : F4 reste F4 en français comme en anglais.
/// </summary>
public readonly record struct KeyChord(string Key, bool Ctrl = false, bool Alt = false, bool Shift = false)
{
    public static KeyChord Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new FormatException($"Raccourci vide : '{text}'.");
        }

        bool ctrl = false, alt = false, shift = false;
        foreach (string modifier in parts[..^1])
        {
            switch (modifier.ToUpperInvariant())
            {
                case "CTRL":
                    ctrl = true;
                    break;
                case "ALT":
                    alt = true;
                    break;
                case "SHIFT":
                    shift = true;
                    break;
                default:
                    throw new FormatException($"Modificateur inconnu '{modifier}' dans '{text}'.");
            }
        }

        string key = parts[^1];
        return new KeyChord(key.Length == 1 ? key.ToUpperInvariant() : key, ctrl, alt, shift);
    }

    public override string ToString() =>
        (Ctrl ? "Ctrl+" : string.Empty) + (Alt ? "Alt+" : string.Empty) + (Shift ? "Shift+" : string.Empty) + Key;
}
