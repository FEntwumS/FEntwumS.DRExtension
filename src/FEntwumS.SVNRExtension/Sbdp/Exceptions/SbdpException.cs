using FEntwumS.SVNRExtension.Sbdp.Constants;
using FEntwumS.SVNRExtension.Sbdp.Entities;

namespace FEntwumS.SVNRExtension.Sbdp.Exceptions;

/// <summary>
/// Die Hardware hat anders geantwortet als das Protokoll es vorsieht - oder gar nicht.
/// </summary>
/// <remarks>
/// Bewusst eine Ausnahme statt eines <c>bool</c> wie in <c>bootloader_protocol.py</c>. Dort gab
/// jede Funktion nur "hat geklappt / hat nicht geklappt" zurueck und warf damit genau die
/// Information weg, die man beim Bringup braucht: <em>was</em> kam stattdessen. Ein
/// <see cref="SvnrError.RamOverflow" /> und eine tote Leitung sahen fuer den Aufrufer gleich aus
/// (<c>bootloader_protocol.py:138-147</c>).
/// </remarks>
public sealed class SbdpException(string message, SbdpPacket? received = null) : Exception(message)
{
    /// <summary>Was tatsaechlich ankam, oder <c>null</c> bei Zeitueberschreitung.</summary>
    public SbdpPacket? Received { get; } = received;

    internal static SbdpException Unexpected(string operation, SbdpPacket? received, string expected)
    {
        var actual = received is { } packet ? packet.ToString() : "nothing (timeout)";
        return new SbdpException($"{operation}: expected {expected}, received {actual}.", received);
    }
}
