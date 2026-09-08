using System;
using System.Buffers.Binary;
using System.IO;

namespace FEntwumS.SVNRExtension.Elf.Dwarf;

/// <summary>
/// Erzeugt die Sektion <c>.debug_aranges</c> - die Adressbereiche, die zu dieser
/// Compilation Unit gehoeren. GDB findet darueber schnell die passende CU zu einer Adresse.
/// </summary>
/// <remarks>
/// In der urspruenglichen Fassung wurde diese Sektion unveraendert aus dem Template
/// uebernommen und beschrieb damit noch den Adressbereich von <c>simple.c</c>
/// (0x0 bis 0x29) statt den des SVNR-Programms.
/// </remarks>
internal static class DebugArangesWriter
{
    private const ushort DwarfVersion = 2;

    public static byte[] Write(uint highPc, byte pointerSize = 4)
    {
        if (pointerSize != 4) throw new ArgumentOutOfRangeException(
            nameof(pointerSize), "Only 32-bit DWARF is supported.");

        using var stream = new MemoryStream();

        stream.Write(new byte[4], 0, 4);                 // unit_length (Platzhalter)
        WriteUInt16(stream, DwarfVersion);
        WriteUInt32(stream, 0);                          // Offset der CU in .debug_info
        stream.WriteByte(pointerSize);
        stream.WriteByte(0);                             // segment_size

        // Die Tupelliste beginnt an einer Grenze von 2 * pointerSize.
        var tupleSize = 2 * pointerSize;
        var padding = (tupleSize - (int)stream.Length % tupleSize) % tupleSize;
        stream.Write(new byte[padding], 0, padding);

        WriteUInt32(stream, 0);                          // Startadresse
        WriteUInt32(stream, highPc);                     // Laenge
        WriteUInt32(stream, 0);                          // Abschlusstupel
        WriteUInt32(stream, 0);

        var data = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), (uint)(data.Length - 4));
        return data;
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }
}
