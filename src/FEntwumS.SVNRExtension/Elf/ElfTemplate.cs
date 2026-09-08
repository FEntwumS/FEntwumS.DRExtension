using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FEntwumS.SVNRExtension.Elf;

/// <summary>
/// Liest die Template-ELF-Datei (<c>template.o</c>) und stellt ihre Sektionen bereit.
/// </summary>
/// <remarks>
/// Das Template liefert das Geruest, das nicht selbst erzeugt wird: ELF-Grundstruktur,
/// <c>.symtab</c>, <c>.strtab</c>, <c>.shstrtab</c>, <c>.group</c> und die Notes.
/// Alle Offsets werden aus dem Header gelesen, nichts ist hart kodiert - die
/// Python-Fassung hatte den Section-Header-Offset als Konstante 1416 stehen und waere
/// bei einem neu gebauten Template still falsch geworden.
/// </remarks>
internal sealed class ElfTemplate
{
    public const int ElfHeaderSize = 52;
    public const int SectionHeaderSize = 40;
    private const int EShoffOffset = 32;
    private const int EShentsizeOffset = 46;
    private const int EShnumOffset = 48;
    private const int EShstrndxOffset = 50;

    private const uint ShtNobits = 8;

    private ElfTemplate(IReadOnlyDictionary<string, ElfSection> sections)
    {
        Sections = sections;
    }

    public IReadOnlyDictionary<string, ElfSection> Sections { get; }

    public ElfSection this[string name] => Sections.TryGetValue(name, out var section)
        ? section
        : throw new KeyNotFoundException($"Section '{name}' is missing from the template.");

    /// <summary>Laedt das Template und prueft, dass alle benoetigten Sektionen vorhanden sind.</summary>
    public static ElfTemplate Load(string path, IEnumerable<string> requiredSections)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));

        var bytes = File.ReadAllBytes(path);
        ValidateIdentification(bytes, path);

        var shoff = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(EShoffOffset));
        var shentsize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(EShentsizeOffset));
        var shnum = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(EShnumOffset));
        var shstrndx = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(EShstrndxOffset));

        if (shentsize != SectionHeaderSize)
        {
            throw new InvalidDataException(
                $"{path}: unexpected section header size {shentsize}, expected {SectionHeaderSize}.");
        }

        var nameTable = ReadSectionData(bytes, shoff, shstrndx);

        var sections = new Dictionary<string, ElfSection>(StringComparer.Ordinal);
        for (var index = 0; index < shnum; index++)
        {
            var headerStart = (int)shoff + index * SectionHeaderSize;
            var header = bytes.AsSpan(headerStart, SectionHeaderSize).ToArray();

            var nameOffset = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0));
            var name = ReadCString(nameTable, (int)nameOffset);

            sections[name] = new ElfSection(name, index, header, ReadSectionData(bytes, shoff, index));
        }

        foreach (var required in requiredSections)
        {
            if (!sections.ContainsKey(required))
            {
                throw new InvalidDataException($"{path}: section '{required}' is missing.");
            }
        }

        return new ElfTemplate(sections);
    }

    private static void ValidateIdentification(byte[] bytes, string path)
    {
        if (bytes.Length < ElfHeaderSize
            || bytes[0] != 0x7f || bytes[1] != (byte)'E' || bytes[2] != (byte)'L' || bytes[3] != (byte)'F')
        {
            throw new InvalidDataException($"{path}: not an ELF file.");
        }

        if (bytes[4] != 1) throw new InvalidDataException($"{path}: not ELF32.");
        if (bytes[5] != 1) throw new InvalidDataException($"{path}: not little endian.");
    }

    private static byte[] ReadSectionData(byte[] bytes, uint shoff, int index)
    {
        var headerStart = (int)shoff + index * SectionHeaderSize;
        var type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(headerStart + 4));
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(headerStart + 16));
        var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(headerStart + 20));

        // SHT_NOBITS (.bss) belegt keinen Platz in der Datei.
        if (type == ShtNobits || size == 0) return Array.Empty<byte>();

        return bytes.AsSpan((int)offset, (int)size).ToArray();
    }

    private static string ReadCString(byte[] table, int offset)
    {
        var end = offset;
        while (end < table.Length && table[end] != 0) end++;
        return Encoding.UTF8.GetString(table, offset, end - offset);
    }
}
