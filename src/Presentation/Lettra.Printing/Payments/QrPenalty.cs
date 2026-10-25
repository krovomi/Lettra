namespace Lettra.Printing.Payments;

/// <summary>
/// Pénalité d'un masque (ISO/IEC 18004 §7.8.3) : longues suites d'une même couleur, carrés 2×2, motifs qui imitent un
/// repère de position, déséquilibre entre noir et blanc. Le masque le moins pénalisé est le plus facile à lire.
/// </summary>
internal static class QrPenalty
{
    private static readonly bool[] FinderLike = [true, false, true, true, true, false, true];

    public static int Of(bool[,] modules, int size)
    {
        int penalty = 0;
        int dark = 0;
        for (int i = 0; i < size; i++)
        {
            penalty += Line(size, j => modules[i, j]);
            penalty += Line(size, j => modules[j, i]);
        }

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (modules[y, x])
                {
                    dark++;
                }

                if (x < size - 1 && y < size - 1)
                {
                    bool color = modules[y, x];
                    if (color == modules[y, x + 1] && color == modules[y + 1, x] && color == modules[y + 1, x + 1])
                    {
                        penalty += 3;
                    }
                }
            }
        }

        int total = size * size;
        int k = ((Math.Abs((dark * 20) - (total * 10)) + total - 1) / total) - 1;
        return penalty + (Math.Max(0, k) * 10);
    }

    /// <summary>Règles 1 et 3 sur une ligne ou une colonne ; hors du symbole, les modules sont blancs.</summary>
    private static int Line(int size, Func<int, bool> at)
    {
        int penalty = 0;
        int run = 1;
        for (int i = 1; i < size; i++)
        {
            if (at(i) == at(i - 1))
            {
                run++;
            }
            else
            {
                penalty += RunPenalty(run);
                run = 1;
            }
        }

        penalty += RunPenalty(run);

        for (int start = 0; start + FinderLike.Length <= size; start++)
        {
            if (Matches(at, start) && (LightRun(size, at, start - 4, start) || LightRun(size, at, start + 7, start + 11)))
            {
                penalty += 40;
            }
        }

        return penalty;
    }

    private static int RunPenalty(int run) => run >= 5 ? run - 2 : 0;

    private static bool Matches(Func<int, bool> at, int start)
    {
        for (int i = 0; i < FinderLike.Length; i++)
        {
            if (at(start + i) != FinderLike[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool LightRun(int size, Func<int, bool> at, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            if (i >= 0 && i < size && at(i))
            {
                return false;
            }
        }

        return true;
    }
}
