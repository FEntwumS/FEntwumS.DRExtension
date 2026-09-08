using System.Net;
using System.Net.Sockets;
using FEntwumS.SVNRExtension.Rsp;
using FEntwumS.SVNRExtension.Sbdp;
using FEntwumS.SVNRExtension.Sbdp.Constants;

namespace FEntwumS.SVNRExtension.Services;

public sealed class RemoteStubService : IDisposable
{
    private const string TargetDescriptionFile = "target.xml";
    private const int LoadProgramLockTimeoutMilliseconds = 500;

    private readonly Lock _serialGate = new();

    private ISbdpTransport? _transport;
    private SvnrBootloaderClient? _client;
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellationToken;
    private Task? _sessionLoop;
    private string _targetDescription = string.Empty;

    public int Port { get; private set; }

    public bool IsRunning => _sessionLoop is { IsCompleted: false };

    public Action<bool, string>? TraceRsp { get; set; }

    // Gerufen, wenn ein RSP-Kommando an der Hardware scheitert. GDB bekommt nur E01; der Grund
    // steht in der Ausnahme und ginge sonst verloren. Laeuft auf dem Sitzungs-Thread.
    public Action<string>? Fault { get; set; }

    public int Start(ISbdpTransport transport, int tcpPort = 0)
    {
        if (IsRunning) throw new InvalidOperationException("The Remote Stub service is already running");

        _targetDescription = ReadTargetDescription();
        _transport = transport;
        _client = new SvnrBootloaderClient(transport);

        // Scheitert der Start auf halbem Weg - seit dem einstellbaren Port vor allem, weil
        // diesen schon jemand haelt -, muss die serielle Schnittstelle wieder frei werden.
        // Der Aufrufer raeumt nur auf, was laeuft, und das tut ein halb gestarteter Stub nicht.
        try
        {
            if (!_client.TestCommunication())
                throw new InvalidOperationException("The SVNR did not answer via serial.");

            _client.SwitchToDebug();
            _client.DebugReset();

            _listener = new TcpListener(IPAddress.Loopback, tcpPort);
            _listener.Start(1);
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _cancellationToken = new CancellationTokenSource();
            _sessionLoop = Task.Run(() => AcceptSessions(_cancellationToken.Token));

            return Port;
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void LoadProgram(byte[] image)
    {
        var client = _client ?? throw new InvalidOperationException("The stub is not running.");

        if (!_serialGate.TryEnter(LoadProgramLockTimeoutMilliseconds))
            throw new InvalidOperationException("The serial connection is busy.");

        try
        {
            client.Bootload(image);
            client.SwitchToDebug();
            client.DebugReset();
        }
        finally
        {
            _serialGate.Exit();
        }
    }

    public void Stop()
    {
        _cancellationToken?.Cancel();
        _listener?.Stop();

        try
        {
            _sessionLoop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        RestoreNormalMode();

        _transport?.Dispose();

        _sessionLoop = null;
        _listener = null;
        _transport = null;
        _client = null;
        Port = 0;
    }

    // Holt die FSM auf dem FPGA aus dem Debug-Zweig zurueck, bevor die Verbindung faellt.
    // Bleibt sie dort stehen, meldet die naechste Suche "kein SVNR": Das Board antwortet zwar,
    // aber mit DebugRunning oder Halted, und dann half bisher nur Aus- und Einstecken.
    // Bestbemuehen - scheitert es, muss der Port trotzdem frei werden. Diesen Fall faengt
    // SvnrBootloaderClient.TestCommunication beim naechsten Start ab.
    private void RestoreNormalMode()
    {
        if (_client is not { } client) return;

        // Die Session-Schleife koennte noch am Port haengen, falls sie oben nicht rechtzeitig
        // aufgehoert hat -> ohne die Sperre nicht dazwischenfunken.
        if (!_serialGate.TryEnter(LoadProgramLockTimeoutMilliseconds)) return;

        try
        {
            // z_DEBUG_RUNNING akzeptiert nur RequestState und Halt - ein Reset-Kommando wird
            // dort ignoriert. Deshalb erst den Zustand pruefen und ggf. anhalten.
            var received = client.RequestState();
            if (received is { Type: SbdpType.Status, State: SvnrState.DebugRunning })
                client.Halt();

            client.DebugReset();
            client.SwitchToPowerOn();
        }
        catch (Exception)
        {
            // Siehe oben: Ein Board, das hier nicht mehr antwortet, ist mit einem weiteren
            // Paket nicht zu retten.
        }
        finally
        {
            _serialGate.Exit();
        }
    }

    public void Dispose()
    {
        Stop();
        _cancellationToken?.Dispose();
        _cancellationToken = null;
    }

    private void AcceptSessions(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient connection;

            try
            {
                connection = _listener!.AcceptTcpClient();
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException
                                                  or InvalidOperationException)
            {
                return;
            }

            using (connection)
            using (var networkStream = connection.GetStream())
            {
                RunSession(networkStream, token);
            }
        }
    }

    private void RunSession(NetworkStream networkStream, CancellationToken token)
    {
        var stream = new RspStream(networkStream) { Trace = TraceRsp };
        var processor = new RspCommandProcessor(_client!, _targetDescription,
            () => token.IsCancellationRequested || stream.TryConsumeInterrupt(),
            Fault);

        while (!token.IsCancellationRequested)
        {
            string? command;

            try
            {
                command = stream.ReadCommand();
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                return;
            }

            if (command is null) return;

            string response;
            lock (_serialGate)
            {
                response = processor.Process(command);
            }

            try
            {
                stream.Send(response);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Pfad der Zielbeschreibung neben der Erweiterung.
    /// </summary>
    /// <remarks>
    /// Der Stub liefert ihren Inhalt auf Anfrage ueber <c>qXfer:features:read</c> aus. GDB
    /// braucht die Registeraufteilung aber schon, bevor es das erste <c>g</c>-Paket liest,
    /// deshalb verweist die Kommandodatei des Programms zusaetzlich auf diese Datei.
    /// </remarks>
    public static string TargetDescriptionPath()
    {
        var directory = Path.GetDirectoryName(typeof(RemoteStubService).Assembly.Location)!;

        return Path.Combine(directory, "Assets", TargetDescriptionFile);
    }

    private static string ReadTargetDescription()
    {
        var path = TargetDescriptionPath();
        return !File.Exists(path) ? throw new FileNotFoundException($"Target Description missing: {path}", path) : File.ReadAllText(path);
    }
}
