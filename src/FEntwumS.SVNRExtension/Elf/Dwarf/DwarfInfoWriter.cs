using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

using FEntwumS.SVNRExtension.Elf.Dwarf.Constants;

namespace FEntwumS.SVNRExtension.Elf.Dwarf;

/// <summary>
/// Serialisiert einen <see cref="Die"/>-Baum zur Sektion <c>.debug_info</c> und
/// fuellt dabei die zugehoerige <see cref="AbbrevTable"/>.
/// </summary>
/// <remarks>
/// Zwei Durchlaeufe:
/// <list type="number">
/// <item>Layout - jedem DIE seinen Offset zuweisen. Attributgroessen haengen nicht
/// von Referenzwerten ab (<see cref="DwForm.Ref4"/> ist immer 4 Byte), ein Durchlauf genuegt.</item>
/// <item>Emit - Bytes schreiben, Referenzen ueber die inzwischen bekannten Offsets aufloesen.</item>
/// </list>
/// Der Terminator am Ende jeder Kinderliste wird aus <see cref="Die.HasChildren"/>
/// abgeleitet und kann daher nicht vergessen werden.
/// </remarks>
internal sealed class DwarfInfoWriter
{
    /// <summary>unit_length (4) + version (2) + abbrev_offset (4) + address_size (1).</summary>
    private const int CompileUnitHeaderSize = 11;

    private const ushort DwarfVersion = 4;

    private readonly byte _pointerSize;

    public DwarfInfoWriter(byte pointerSize = 4)
    {
        if (pointerSize != 4) throw new ArgumentOutOfRangeException(
            nameof(pointerSize), "Only 32-bit DWARF is supported.");
        _pointerSize = pointerSize;
    }

    /// <summary>Die waehrend des Schreibens aufgebaute Abbreviation-Tabelle.</summary>
    public AbbrevTable Abbreviations { get; } = new();

    public byte[] WriteCompileUnit(Die compileUnit)
    {
        if (compileUnit is null) throw new ArgumentNullException(nameof(compileUnit));

        AssignOffsets(compileUnit, CompileUnitHeaderSize);
        var body = Emit(compileUnit);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write((uint)(body.Length + CompileUnitHeaderSize - 4));   // unit_length
        writer.Write(DwarfVersion);
        writer.Write((uint)0);                                           // Offset in .debug_abbrev
        writer.Write(_pointerSize);
        writer.Write(body);
        writer.Flush();

        return stream.ToArray();
    }

    private int AssignOffsets(Die die, int cursor)
    {
        die.Offset = cursor;
        cursor += Leb128.UnsignedLength((ulong)Abbreviations.CodeFor(die));

        foreach (var attribute in die.Attributes)
        {
            cursor += SizeOf(attribute);
        }

        foreach (var child in die.Children)
        {
            cursor = AssignOffsets(child, cursor);
        }

        if (die.HasChildren) cursor += 1;    // Terminator der Kinderliste
        return cursor;
    }

    private byte[] Emit(Die die)
    {
        using var stream = new MemoryStream();
        EmitInto(stream, die);
        return stream.ToArray();
    }

    private void EmitInto(Stream stream, Die die)
    {
        var code = Leb128.Unsigned((ulong)Abbreviations.CodeFor(die));
        stream.Write(code, 0, code.Length);

        foreach (var attribute in die.Attributes)
        {
            var bytes = Encode(attribute);
            stream.Write(bytes, 0, bytes.Length);
        }

        foreach (var child in die.Children)
        {
            EmitInto(stream, child);
        }

        if (die.HasChildren) stream.WriteByte(0);
    }

    private int SizeOf(DieAttribute attribute) => attribute.Form switch
    {
        DwForm.FlagPresent => 0,
        DwForm.Data1 => 1,
        DwForm.Addr => _pointerSize,
        DwForm.Data4 or DwForm.Strp or DwForm.SecOffset or DwForm.Ref4 => 4,
        DwForm.String => Encoding.UTF8.GetByteCount((string)attribute.Value!) + 1,
        DwForm.ExprLoc => ExprLocSize((byte[])attribute.Value!),
        _ => throw new NotSupportedException($"Form {attribute.Form} is not supported."),
    };

    private static int ExprLocSize(byte[] expression) =>
        Leb128.UnsignedLength((ulong)expression.Length) + expression.Length;

    private byte[] Encode(DieAttribute attribute)
    {
        switch (attribute.Form)
        {
            case DwForm.FlagPresent:
                return Array.Empty<byte>();

            case DwForm.Data1:
                return new[] { (byte)attribute.Value! };

            case DwForm.Addr:
            case DwForm.Data4:
            case DwForm.Strp:
            case DwForm.SecOffset:
                return UInt32Le((uint)attribute.Value!);

            case DwForm.Ref4:
                return UInt32Le((uint)((DieReference)attribute.Value!).Offset);

            case DwForm.String:
            {
                var text = Encoding.UTF8.GetBytes((string)attribute.Value!);
                var result = new byte[text.Length + 1];
                Buffer.BlockCopy(text, 0, result, 0, text.Length);
                return result;                                   // letztes Byte bleibt 0
            }

            case DwForm.ExprLoc:
            {
                var expression = (byte[])attribute.Value!;
                var length = Leb128.Unsigned((ulong)expression.Length);
                var result = new byte[length.Length + expression.Length];
                Buffer.BlockCopy(length, 0, result, 0, length.Length);
                Buffer.BlockCopy(expression, 0, result, length.Length, expression.Length);
                return result;
            }

            default:
                throw new NotSupportedException($"Form {attribute.Form} is not supported.");
        }
    }

    //ELFDATA2LSB - explizit Little Endian, unabhaengig von der Host-Architektur.
    private static byte[] UInt32Le(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }
}
