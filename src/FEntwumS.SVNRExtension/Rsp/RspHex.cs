namespace FEntwumS.SVNRExtension.Rsp;

public static class RspHex
{
    public static string LittleEndian16(ushort value)
    {
        return $"{(byte)value:x2}{(byte)(value >> 8):x2}";
    }

    public static string LittleEndian32(uint value)
    {
        return $"{(byte)value:x2}{(byte)(value >> 8):x2}{(byte)(value >> 16):x2}{(byte)(value >> 24):x2}";
    }

    public static ushort ParseLittleEndianWord(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        if (bytes.Length != 2) throw new FormatException($"'{hex}' is not a 16-bit value.");

        return (ushort)(bytes[0] | (bytes[1] << 8));
    }

    public static IReadOnlyList<ushort> ParseLittleEndianWords(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        var words = new ushort[bytes.Length / 2];

        for (var i = 0; i < words.Length; i++)
            words[i] = (ushort)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));

        return words;
    }
}
