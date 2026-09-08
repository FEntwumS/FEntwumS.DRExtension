using System;

namespace FEntwumS.SVNRExtension.Elf;

/// <summary>
/// Eine Sektion aus der Template-Datei: ihr 40 Byte grosser Section-Header und ihr Inhalt.
/// </summary>
internal sealed class ElfSection
{
    public ElfSection(string name, int index, byte[] header, byte[] data)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Index = index;
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Data = data ?? throw new ArgumentNullException(nameof(data));

        if (header.Length != ElfTemplate.SectionHeaderSize)
        {
            throw new ArgumentException(
                $"A section header must be exactly {ElfTemplate.SectionHeaderSize} bytes.", nameof(header));
        }
    }

    public string Name { get; }

    /// <summary>Index im Template - relevant, weil <c>.symtab</c> und <c>.group</c> darauf verweisen.</summary>
    public int Index { get; }

    /// <summary>Rohbytes des Section-Headers. sh_offset liegt bei 16, sh_size bei 20, sh_link bei 24.</summary>
    public byte[] Header { get; }

    /// <summary>Sektionsinhalt. Bei SHT_NOBITS (<c>.bss</c>) leer.</summary>
    public byte[] Data { get; }

    public override string ToString() => $"[{Index}] {Name} ({Data.Length} Byte)";
}
