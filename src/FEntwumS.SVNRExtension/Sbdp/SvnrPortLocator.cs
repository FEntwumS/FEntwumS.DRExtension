namespace FEntwumS.SVNRExtension.Sbdp;

/// <summary>
/// Findet die serielle Schnittstelle, an der ein SVNR antwortet.
/// </summary>
/// <remarks>
/// Ein leeres Setting bedeutet "such selbst": Alle Kandidaten werden geoeffnet und mit
/// <see cref="SvnrBootloaderClient.TestCommunication" /> befragt - dasselbe Vorgehen wie in
/// <c>remote_stub.py</c>. Ein gesetztes Setting hat Vorrang und wird nicht geprueft, damit ein
/// Fehlschlag dort eine verstaendliche Meldung ergibt statt einer stillen Suche.
/// </remarks>
public static class SvnrPortLocator
{
    public static ISbdpTransport Open(string? configuredPort)
    {
        if (!string.IsNullOrWhiteSpace(configuredPort))
            return new SerialSbdpTransport(configuredPort);

        var candidates = SerialSbdpTransport.ListCandidatePorts();

        if (candidates.Count == 0)
            throw new IOException(
                "No serial port found. On Linux the user has to be in the 'dialout' group, " +
                "on macOS the board shows up as /dev/cu.usbserial-*.");

        foreach (var candidate in candidates)
        {
            SerialSbdpTransport? transport = null;

            try
            {
                transport = new SerialSbdpTransport(candidate);

                if (new SvnrBootloaderClient(transport).TestCommunication()) return transport;

                transport.Dispose();
            }
            catch (Exception)
            {
                transport?.Dispose();
            }
        }

        throw new IOException(
            $"No SVNR responded on any of the {candidates.Count} candidate port(s). " +
            "If a previous debug session was not cleanly terminated, unplug and replug the Icedurino.");
    }
}
