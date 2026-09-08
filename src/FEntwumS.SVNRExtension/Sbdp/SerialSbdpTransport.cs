using System.IO.Ports;
using System.Runtime.InteropServices;
using FEntwumS.SVNRExtension.Sbdp.Constants;
using FEntwumS.SVNRExtension.Sbdp.Entities;

namespace FEntwumS.SVNRExtension.Sbdp;

/// <summary>
/// <see cref="ISbdpTransport" /> ueber eine serielle Schnittstelle: 115200 8N1, wie die
/// Referenzimplementierung.
/// </summary>
public sealed class SerialSbdpTransport : ISbdpTransport
{
    private readonly SerialPort _port;
    private readonly byte[] _singleByte = new byte[1];

    /// <param name="portName">Geraetename, z. B. <c>/dev/ttyUSB0</c> oder <c>/dev/cu.usbserial-1420</c>.</param>
    /// <param name="readTimeout">
    /// Wartezeit auf eine Antwort. Die Referenz nutzt 100 ms; das traegt drei Byte bei 115200
    /// (rund 260 us) mit reichlich Reserve.
    /// </param>
    public SerialSbdpTransport(string portName, int readTimeout = 100)
    {
        _port = new SerialPort(portName, SbdpConstants.BaudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = readTimeout,
            WriteTimeout = 2000
        };

        try
        {
            _port.Open();
        }
        catch (UnauthorizedAccessException e)
        {
            // Der mit Abstand haeufigste Fehler unter Linux, und die Meldung des Frameworks nennt
            // die Ursache nicht.
            throw new UnauthorizedAccessException(
                $"No access to '{portName}'. On Linux this usually means the user is not in the " +
                "'dialout' group (sudo usermod -aG dialout $USER, then log in again). " +
                "Otherwise another process is holding the port open.", e);
        }

        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
    }

    public void Send(SbdpPacket packet)
    {
        var buffer = packet.ToBytes();
        _port.Write(buffer, 0, buffer.Length);
    }

    public void SendRaw(ReadOnlySpan<byte> data)
    {
        _port.BaseStream.Write(data);
        _port.BaseStream.Flush();
    }

    public SbdpPacket? Receive()
    {
        Span<byte> buffer = stackalloc byte[SbdpConstants.PacketSize];
        var filled = 0;

        // SerialPort.Read liefert, was gerade da ist - bei drei Byte durchaus auch nur eines.
        // Ohne diese Schleife entstuenden aus einem Rahmen zwei halbe.
        while (filled < SbdpConstants.PacketSize)
        {
            int read;
            try
            {
                read = _port.Read(_singleByte, 0, 1);
            }
            catch (TimeoutException)
            {
                return null;
            }

            if (read == 0) return null;

            buffer[filled++] = _singleByte[0];
        }

        return SbdpPacket.FromBytes(buffer);
    }

    public void Flush()
    {
        if (!_port.IsOpen) return;
        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
    }

    public void Dispose()
    {
        if (_port.IsOpen) _port.Close();
        _port.Dispose();
    }

    /// <summary>
    /// Liefert die Geraete, die als FPGA-Verbindung in Frage kommen.
    /// </summary>
    /// <remarks>
    /// Unter macOS bewusst <c>/dev/cu.*</c> statt der <c>/dev/tty.*</c>, die
    /// <see cref="SerialPort.GetPortNames" /> dort meldet: Ein <c>tty</c>-Geraet blockiert beim
    /// Oeffnen, bis die Gegenstelle DCD setzt, was ein USB-Seriell-Wandler nie tut. Der Aufruf
    /// haengt dann ohne Fehlermeldung.
    /// </remarks>
    public static IReadOnlyList<string> ListCandidatePorts()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return SafeEnumerate("/dev", "cu.*")
                .Where(x => !x.EndsWith("cu.Bluetooth-Incoming-Port", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToList();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // ttyUSB* sind FTDI/CP210x-Wandler, ttyACM* melden sich als CDC-Geraet.
            return SafeEnumerate("/dev", "ttyUSB*")
                .Concat(SafeEnumerate("/dev", "ttyACM*"))
                .Order(StringComparer.Ordinal)
                .ToList();
        }

        return SerialPort.GetPortNames().Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> SafeEnumerate(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern);
        }
        catch (Exception)
        {
            return [];
        }
    }
}