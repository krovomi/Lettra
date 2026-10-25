using System.Text;

namespace Lettra.Printing.Payments;

/// <summary>
/// Code QR (ISO/IEC 18004) en mode octet, niveau de correction M : celui qu'impose le Conseil européen des paiements
/// pour le QR code de virement. Écrit ici plutôt que tiré d'un paquet : une centaine de lignes déterministes, le même
/// code dans le navigateur, l'app hors ligne et le serveur, et aucune dépendance de plus à surveiller.
/// L'algorithme suit la norme et l'implémentation de référence de Project Nayuki (MIT).
/// </summary>
public sealed class QrCode
{
    /// <summary>Codes de correction par bloc, niveau M, versions 1 à 40 (index 0 inutilisé).</summary>
    private static readonly int[] EccCodewordsPerBlock =
    [
        -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26,
        26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28,
    ];

    /// <summary>Nombre de blocs de correction, niveau M, versions 1 à 40 (index 0 inutilisé).</summary>
    private static readonly int[] ErrorCorrectionBlocks =
    [
        -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16,
        17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49,
    ];

    /// <summary>Bits de format du niveau M (00).</summary>
    private const int EccFormatBits = 0;

    private readonly bool[,] _modules;
    private readonly bool[,] _isFunction;

    private QrCode(int version)
    {
        Version = version;
        Size = (version * 4) + 17;
        _modules = new bool[Size, Size];
        _isFunction = new bool[Size, Size];
    }

    /// <summary>Version (1 à 40) : la plus petite qui contient le texte.</summary>
    public int Version { get; }

    /// <summary>Côté du symbole, en modules, sans la marge blanche.</summary>
    public int Size { get; }

    /// <summary>Masque retenu (0 à 7), celui qui pénalise le moins la lecture.</summary>
    public int Mask { get; private set; }

    /// <summary>Le module en colonne <paramref name="x"/>, ligne <paramref name="y"/> est-il noir ?</summary>
    public bool IsDark(int x, int y) => x >= 0 && x < Size && y >= 0 && y < Size && _modules[y, x];

    /// <summary>Encode le texte en UTF-8, mode octet, niveau M.</summary>
    /// <exception cref="ArgumentException">Le texte dépasse la capacité d'une version 40.</exception>
    public static QrCode Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] data = Encoding.UTF8.GetBytes(text);
        int version = 1;
        while (DataBitsNeeded(data.Length, version) > DataCodewords(version) * 8)
        {
            version++;
            if (version > 40)
            {
                throw new ArgumentException("Texte trop long pour un code QR.", nameof(text));
            }
        }

        QrCode qr = new(version);
        qr.DrawFunctionPatterns();
        qr.DrawCodewords(qr.AddEccAndInterleave(DataCodewordsOf(data, version)));
        qr.ApplyBestMask();
        return qr;
    }

    // ---------- flux de données ----------

    private static int CountBits(int version) => version <= 9 ? 8 : 16;

    private static int DataBitsNeeded(int length, int version) => 4 + CountBits(version) + (length * 8);

    private static byte[] DataCodewordsOf(byte[] data, int version)
    {
        int capacityBits = DataCodewords(version) * 8;
        List<bool> bits = new(capacityBits);
        Append(bits, 0b0100, 4);
        Append(bits, data.Length, CountBits(version));
        foreach (byte b in data)
        {
            Append(bits, b, 8);
        }

        Append(bits, 0, Math.Min(4, capacityBits - bits.Count));
        Append(bits, 0, (8 - (bits.Count % 8)) % 8);
        for (int pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11)
        {
            Append(bits, pad, 8);
        }

        byte[] result = new byte[bits.Count / 8];
        for (int i = 0; i < bits.Count; i++)
        {
            if (bits[i])
            {
                result[i >> 3] |= (byte)(0x80 >> (i & 7));
            }
        }

        return result;
    }

    private static void Append(List<bool> bits, int value, int length)
    {
        for (int i = length - 1; i >= 0; i--)
        {
            bits.Add(((value >> i) & 1) != 0);
        }
    }

    private static int RawDataModules(int version)
    {
        int result = (((16 * version) + 128) * version) + 64;
        if (version >= 2)
        {
            int alignments = (version / 7) + 2;
            result -= (((25 * alignments) - 10) * alignments) - 55;
            if (version >= 7)
            {
                result -= 36;
            }
        }

        return result;
    }

    private static int DataCodewords(int version) =>
        (RawDataModules(version) / 8) - (EccCodewordsPerBlock[version] * ErrorCorrectionBlocks[version]);

    // ---------- correction d'erreurs (Reed-Solomon sur GF(256)) ----------

    private byte[] AddEccAndInterleave(byte[] data)
    {
        int blockCount = ErrorCorrectionBlocks[Version];
        int eccLength = EccCodewordsPerBlock[Version];
        int rawCodewords = RawDataModules(Version) / 8;
        int shortBlocks = blockCount - (rawCodewords % blockCount);
        int shortBlockLength = rawCodewords / blockCount;
        byte[] divisor = ReedSolomon.Divisor(eccLength);

        List<byte[]> blocks = [];
        for (int i = 0, k = 0; i < blockCount; i++)
        {
            int length = shortBlockLength - eccLength + (i < shortBlocks ? 0 : 1);
            byte[] block = new byte[shortBlockLength + 1];
            Array.Copy(data, k, block, 0, length);
            k += length;
            byte[] ecc = ReedSolomon.Remainder(data.AsSpan(k - length, length), divisor);
            Array.Copy(ecc, 0, block, block.Length - eccLength, eccLength);
            blocks.Add(block);
        }

        byte[] result = new byte[rawCodewords];
        int index = 0;
        for (int i = 0; i < blocks[0].Length; i++)
        {
            for (int j = 0; j < blocks.Count; j++)
            {
                // Les blocs courts ont un octet de bourrage à la fin de leurs données : il ne s'écrit pas.
                if (i != shortBlockLength - eccLength || j >= shortBlocks)
                {
                    result[index++] = blocks[j][i];
                }
            }
        }

        return result;
    }

    // ---------- motifs fixes ----------

    private void DrawFunctionPatterns()
    {
        for (int i = 0; i < Size; i++)
        {
            SetFunction(6, i, i % 2 == 0);
            SetFunction(i, 6, i % 2 == 0);
        }

        DrawFinder(3, 3);
        DrawFinder(Size - 4, 3);
        DrawFinder(3, Size - 4);

        int[] positions = AlignmentPositions();
        int last = positions.Length - 1;
        for (int i = 0; i < positions.Length; i++)
        {
            for (int j = 0; j < positions.Length; j++)
            {
                bool corner = (i == 0 && j == 0) || (i == 0 && j == last) || (i == last && j == 0);
                if (!corner)
                {
                    DrawAlignment(positions[i], positions[j]);
                }
            }
        }

        DrawFormatBits(0);
        DrawVersion();
    }

    private void DrawFinder(int x, int y)
    {
        for (int dy = -4; dy <= 4; dy++)
        {
            for (int dx = -4; dx <= 4; dx++)
            {
                int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                int xx = x + dx;
                int yy = y + dy;
                if (xx >= 0 && xx < Size && yy >= 0 && yy < Size)
                {
                    SetFunction(xx, yy, distance is not 2 and not 4);
                }
            }
        }
    }

    private void DrawAlignment(int x, int y)
    {
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                SetFunction(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        }
    }

    private int[] AlignmentPositions()
    {
        if (Version == 1)
        {
            return [];
        }

        int count = (Version / 7) + 2;
        int step = (((Version * 8) + (count * 3) + 5) / ((count * 4) - 4)) * 2;
        int[] result = new int[count];
        result[0] = 6;
        for (int i = count - 1, position = Size - 7; i >= 1; i--, position -= step)
        {
            result[i] = position;
        }

        return result;
    }

    private void DrawFormatBits(int mask)
    {
        int data = (EccFormatBits << 3) | mask;
        int remainder = data;
        for (int i = 0; i < 10; i++)
        {
            remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
        }

        int bits = ((data << 10) | remainder) ^ 0x5412;

        for (int i = 0; i <= 5; i++)
        {
            SetFunction(8, i, Bit(bits, i));
        }

        SetFunction(8, 7, Bit(bits, 6));
        SetFunction(8, 8, Bit(bits, 7));
        SetFunction(7, 8, Bit(bits, 8));
        for (int i = 9; i < 15; i++)
        {
            SetFunction(14 - i, 8, Bit(bits, i));
        }

        for (int i = 0; i < 8; i++)
        {
            SetFunction(Size - 1 - i, 8, Bit(bits, i));
        }

        for (int i = 8; i < 15; i++)
        {
            SetFunction(8, Size - 15 + i, Bit(bits, i));
        }

        SetFunction(8, Size - 8, true);
    }

    private void DrawVersion()
    {
        if (Version < 7)
        {
            return;
        }

        int remainder = Version;
        for (int i = 0; i < 12; i++)
        {
            remainder = (remainder << 1) ^ ((remainder >> 11) * 0x1F25);
        }

        int bits = (Version << 12) | remainder;
        for (int i = 0; i < 18; i++)
        {
            bool bit = Bit(bits, i);
            int a = Size - 11 + (i % 3);
            int b = i / 3;
            SetFunction(a, b, bit);
            SetFunction(b, a, bit);
        }
    }

    private void SetFunction(int x, int y, bool dark)
    {
        _modules[y, x] = dark;
        _isFunction[y, x] = true;
    }

    private static bool Bit(int value, int index) => ((value >> index) & 1) != 0;

    // ---------- données et masque ----------

    private void DrawCodewords(byte[] data)
    {
        int i = 0;
        for (int right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
            {
                right = 5;
            }

            for (int vertical = 0; vertical < Size; vertical++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? Size - 1 - vertical : vertical;
                    if (!_isFunction[y, x] && i < data.Length * 8)
                    {
                        _modules[y, x] = Bit(data[i >> 3], 7 - (i & 7));
                        i++;
                    }
                }
            }
        }
    }

    private void ApplyBestMask()
    {
        int best = 0;
        int lowest = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            ApplyMask(mask);
            DrawFormatBits(mask);
            int penalty = QrPenalty.Of(_modules, Size);
            if (penalty < lowest)
            {
                best = mask;
                lowest = penalty;
            }

            ApplyMask(mask);
        }

        Mask = best;
        ApplyMask(best);
        DrawFormatBits(best);
    }

    /// <summary>Inverse les modules de données selon le masque ; appliqué deux fois, il s'annule.</summary>
    private void ApplyMask(int mask)
    {
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                bool invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => ((x / 3) + (y / 2)) % 2 == 0,
                    5 => ((x * y % 2) + (x * y % 3)) == 0,
                    6 => ((x * y % 2) + (x * y % 3)) % 2 == 0,
                    _ => (((x + y) % 2) + (x * y % 3)) % 2 == 0,
                };
                if (invert && !_isFunction[y, x])
                {
                    _modules[y, x] = !_modules[y, x];
                }
            }
        }
    }
}
