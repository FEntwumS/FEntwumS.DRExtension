using FEntwumS.SVNRExtension.Sbdp.Constants;

namespace FEntwumS.SVNRExtension.Sbdp.Entities;

/// <summary>
/// Ein SBDP-2-Rahmen: immer genau drei Byte, Typ gefolgt von High- und Lowbyte des Werts.
/// </summary>
/// <remarks>
/// Bewusst ohne getrennte High-/Low-Felder wie in <c>SerialPackage</c> der Python-Fassung. Dort
/// waren beide einzeln setzbar, was <c>setValue</c> zu einer Operation machte, die fehlschlagen
/// konnte; hier ist der Wert schlicht ein <see cref="ushort" /> und damit immer gueltig.
/// </remarks>
public readonly record struct SbdpPacket(SbdpType Type, ushort Value)
{
    public byte HighByte => (byte)(Value >> 8);

    public byte LowByte => (byte)(Value & 0xFF);

    /// <summary>Deutet den Wert als Zustand. Nur sinnvoll bei <see cref="SbdpType.Status" />.</summary>
    public SvnrState State => (SvnrState)Value;

    /// <summary>Deutet den Wert als Fehlercode. Nur sinnvoll bei <see cref="SbdpType.Error" />.</summary>
    public SvnrError Error => (SvnrError)Value;

    public static SbdpPacket Command(SvnrCommand command)
    {
        return new SbdpPacket(SbdpType.Command, (ushort)command);
    }

    public byte[] ToBytes()
    {
        return [(byte)Type, HighByte, LowByte];
    }

    public void WriteTo(Span<byte> destination)
    {
        destination[0] = (byte)Type;
        destination[1] = HighByte;
        destination[2] = LowByte;
    }

    public static SbdpPacket FromBytes(ReadOnlySpan<byte> source)
    {
        if (source.Length < SbdpConstants.PacketSize)
            throw new ArgumentException($"A frame needs exactly {SbdpConstants.PacketSize} bytes.", nameof(source));

        return new SbdpPacket((SbdpType)source[0], (ushort)((source[1] << 8) | source[2]));
    }

    /// <summary>
    /// Lesbare Fassung fuer den Paketmitschnitt - der Wert zusaetzlich als Zustand oder Fehler,
    /// wo das etwas bedeutet.
    /// </summary>
    public override string ToString()
    {
        var meaning = Type switch
        {
            SbdpType.Status => $" ({State})",
            SbdpType.Error => $" ({Error})",
            SbdpType.Command => $" ({(SvnrCommand)Value})",
            _ => string.Empty
        };

        return $"{Type} 0x{Value:x4}{meaning}";
    }
}
