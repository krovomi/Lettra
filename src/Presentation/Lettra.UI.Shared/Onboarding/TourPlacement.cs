using System.Globalization;

namespace Lettra.UI.Shared.Onboarding;

/// <summary>Position de l'élément montré, en pixels CSS de la fenêtre, et taille de la fenêtre (renvoyée par <c>lettra-tour.js</c>).</summary>
public sealed record TourTargetRect(double Top, double Left, double Width, double Height, double ViewportWidth, double ViewportHeight);

public enum TourBubbleMode
{
    /// <summary>Pas d'élément à montrer : bulle au centre, fond assombri.</summary>
    Center,

    Below,
    Above,

    /// <summary>Élément haut (menu latéral) : bulle à sa droite, ou à sa gauche faute de place.</summary>
    Beside,

    /// <summary>Téléphone : feuille en bas d'écran (ou en haut si l'élément montré est en bas).</summary>
    SheetBottom,
    SheetTop,
}

/// <summary>Où dessiner la découpe autour de l'élément et la bulle. Calcul pur, testé sans navigateur.</summary>
public sealed record TourPlacement(TourBubbleMode Mode, TourTargetRect? Spot, double? Top, double? Bottom, double? Left, double Width)
{
    /// <summary>En dessous de cette largeur, la bulle devient une feuille (même seuil que la barre d'onglets).</summary>
    public const double PhoneWidth = 768;

    public const double BubbleWidth = 340;
    public const double Gap = 12;
    public const double Margin = 16;

    /// <summary>Hauteur prévue d'une bulle (titre, deux ou trois lignes, boutons).</summary>
    public const double BubbleHeight = 220;

    /// <summary>Marge de la découpe autour de l'élément.</summary>
    public const double SpotPadding = 6;

    public static TourPlacement Compute(TourTargetRect? target, double viewportWidth, double viewportHeight)
    {
        double width = Math.Min(BubbleWidth, Math.Max(200, viewportWidth - (2 * Margin)));
        if (target is { Width: <= 0 } or { Height: <= 0 })
        {
            target = null;
        }

        TourTargetRect? spot = target is null ? null : target with
        {
            Top = target.Top - SpotPadding,
            Left = target.Left - SpotPadding,
            Width = target.Width + (2 * SpotPadding),
            Height = target.Height + (2 * SpotPadding),
        };

        if (viewportWidth < PhoneWidth)
        {
            bool targetLow = target is not null && target.Top + (target.Height / 2) > viewportHeight * 0.55;
            return new(targetLow ? TourBubbleMode.SheetTop : TourBubbleMode.SheetBottom, spot, null, null, null, viewportWidth);
        }

        if (target is null)
        {
            return new(TourBubbleMode.Center, null, null, null, null, width);
        }

        double left = Math.Clamp(target.Left, Margin, Math.Max(Margin, viewportWidth - width - Margin));
        double below = viewportHeight - (target.Top + target.Height);
        if (below >= BubbleHeight + Gap)
        {
            return new(TourBubbleMode.Below, spot, target.Top + target.Height + Gap, null, left, width);
        }

        if (target.Top >= BubbleHeight + Gap)
        {
            return new(TourBubbleMode.Above, spot, null, viewportHeight - target.Top + Gap, left, width);
        }

        double right = target.Left + target.Width + Gap;
        double leftSide = target.Left - Gap - width;
        if (right + width + Margin <= viewportWidth || leftSide >= Margin)
        {
            double top = Math.Clamp(target.Top, Margin, Math.Max(Margin, viewportHeight - BubbleHeight - Margin));
            return new(TourBubbleMode.Beside, spot, top, null, right + width + Margin <= viewportWidth ? right : leftSide, width);
        }

        // Élément plus haut que l'écran : bulle au centre, la découpe reste visible autour.
        return new(TourBubbleMode.Center, spot, null, null, null, width);
    }

    /// <summary>Style en ligne de la bulle (modes ancrés seulement ; les autres sont posés par la feuille de style).</summary>
    public string? BubbleStyle()
    {
        if (Mode is not (TourBubbleMode.Below or TourBubbleMode.Above or TourBubbleMode.Beside))
        {
            return Mode == TourBubbleMode.Center ? $"width: {Px(Width)}" : null;
        }

        string vertical = Top is { } top ? $"top: {Px(top)}" : $"bottom: {Px(Bottom ?? 0)}";
        return $"{vertical}; left: {Px(Left ?? Margin)}; width: {Px(Width)}";
    }

    public string? SpotStyle() => Spot is { } s
        ? $"top: {Px(s.Top)}; left: {Px(s.Left)}; width: {Px(s.Width)}; height: {Px(s.Height)}"
        : null;

    private static string Px(double value) => value.ToString("0.#", CultureInfo.InvariantCulture) + "px";
}
