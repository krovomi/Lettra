using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Lettra.Printing.Payments;

/// <summary>
/// Dessins d'un <see cref="QrCode"/> avec sa marge blanche de quatre modules : une image PNG pour le PDF, un tracé SVG
/// pour l'écran. Noir sur blanc, sans transparence (exigence PDF/A).
/// </summary>
public static class QrImages
{
    /// <summary>Marge blanche imposée autour du symbole, en modules.</summary>
    public const int QuietZone = 4;

    /// <summary>Image PNG RVB 8 bits, <paramref name="scale"/> pixels par module.</summary>
    public static byte[] Png(QrCode qr, int scale = 8)
    {
        ArgumentNullException.ThrowIfNull(qr);
        ArgumentOutOfRangeException.ThrowIfLessThan(scale, 1);
        int side = (qr.Size + (2 * QuietZone)) * scale;

        using MemoryStream raw = new();
        using (ZLibStream zlib = new(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            byte[] row = new byte[1 + (side * 3)];
            for (int py = 0; py < side; py++)
            {
                int y = (py / scale) - QuietZone;
                for (int px = 0; px < side; px++)
                {
                    byte shade = qr.IsDark((px / scale) - QuietZone, y) ? (byte)0x00 : (byte)0xFF;
                    row[1 + (px * 3)] = shade;
                    row[2 + (px * 3)] = shade;
                    row[3 + (px * 3)] = shade;
                }

                zlib.Write(row);
            }
        }

        using MemoryStream png = new();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, side);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), side);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", raw.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>
    /// Tracé SVG d'un carré par module noir, dans un repère d'un module par unité, marge comprise
    /// (<c>viewBox="0 0 n n"</c> avec n = <see cref="ViewBoxSize"/>).
    /// </summary>
    public static string SvgPath(QrCode qr)
    {
        ArgumentNullException.ThrowIfNull(qr);
        StringBuilder path = new();
        for (int y = 0; y < qr.Size; y++)
        {
            for (int x = 0; x < qr.Size; x++)
            {
                if (qr.IsDark(x, y))
                {
                    path.Append('M').Append(x + QuietZone).Append(',').Append(y + QuietZone).Append("h1v1h-1z");
                }
            }
        }

        return path.ToString();
    }

    /// <summary>Côté du repère SVG, marge comprise.</summary>
    public static int ViewBoxSize(QrCode qr)
    {
        ArgumentNullException.ThrowIfNull(qr);
        return qr.Size + (2 * QuietZone);
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        uint crc = Crc32.Update(Crc32.Update(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, crc);
        stream.Write(checksum);
    }

    /// <summary>CRC-32 des blocs PNG (polynôme 0xEDB88320).</summary>
    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Update(uint crc, byte[] data)
        {
            foreach (byte b in data)
            {
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            return crc;
        }

        private static uint[] BuildTable()
        {
            uint[] table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }
    }
}
