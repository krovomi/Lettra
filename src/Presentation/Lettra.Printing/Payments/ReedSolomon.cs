namespace Lettra.Printing.Payments;

/// <summary>Codes correcteurs de Reed-Solomon sur GF(2⁸), polynôme 0x11D, tels que les emploie le code QR.</summary>
internal static class ReedSolomon
{
    /// <summary>Polynôme générateur de degré <paramref name="degree"/>, sans son coefficient de tête.</summary>
    public static byte[] Divisor(int degree)
    {
        byte[] result = new byte[degree];
        result[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < result.Length; j++)
            {
                result[j] = Multiply(result[j], root);
                if (j + 1 < result.Length)
                {
                    result[j] ^= result[j + 1];
                }
            }

            root = Multiply(root, 0x02);
        }

        return result;
    }

    /// <summary>Reste de la division des données par le générateur : les octets de correction.</summary>
    public static byte[] Remainder(ReadOnlySpan<byte> data, byte[] divisor)
    {
        byte[] result = new byte[divisor.Length];
        foreach (byte b in data)
        {
            int factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (int i = 0; i < result.Length; i++)
            {
                result[i] ^= Multiply(divisor[i], factor);
            }
        }

        return result;
    }

    private static byte Multiply(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }

        return (byte)z;
    }
}
