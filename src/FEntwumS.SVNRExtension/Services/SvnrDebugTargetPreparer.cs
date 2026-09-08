using System.Net.Sockets;
using System.Xml;
using System.Xml.Linq;
using Avalonia.Media;
using FEntwumS.SVNRExtension.Asm.Entities; 
using FEntwumS.SVNRExtension.Sbdp;
using FEntwumS.SVNRExtension.Sbdp.Constants;
using FEntwumS.SVNRExtension.Tools; 
using Microsoft.Extensions.Logging;
using OneWare.Essentials.Debugger.Entities;
using OneWare.Essentials.Debugger.Interfaces;
using OneWare.Essentials.Services;
using OneWare.UniversalFpgaProjectSystem.Models;

namespace FEntwumS.SVNRExtension.Services;

public sealed class SvnrDebugTargetPreparer : IDebugTargetPreparer
{
    private const string GdbBackendId = "gdb_server";
    
    public string DisplayName => "SVNRDebugPreparer";
    

    private readonly SvnrDebugBuildService _buildService;
    private readonly RemoteStubService _stubService;
    private readonly ISettingsService _settingsService;
    private readonly IProjectExplorerService _projectExplorerService;
    private readonly IOutputService _outputService;
    private readonly ILogger _logger;

    public SvnrDebugTargetPreparer(
        SvnrDebugBuildService buildService,
        RemoteStubService stubService,
        ISettingsService settingsService,
        IProjectExplorerService projectExplorerService,
        IOutputService outputService,
        ILogger logger)
    {
        _buildService = buildService;
        _stubService = stubService;
        _settingsService = settingsService;
        _projectExplorerService = projectExplorerService;
        _outputService = outputService;
        _logger = logger;
    }

    private UniversalFpgaProjectRoot? ActiveProject => _projectExplorerService.ActiveProject as UniversalFpgaProjectRoot;
   
    //Wenn aktives Projekt UniveralFpga und DebugKit = SVNR in JSON
    public bool CanPrepare() { return ActiveProject is { } project && SvnrSettingsHelper.IsSvnrKit(project); }

    // Wenn man den über den Käfer den Workflow anstößt, wir erstmal die Async Methode zur Vorbereitung des Stubs angestoßen.
    public async Task<DebugLaunchRequest?> PrepareAsync()
    {
        if (ActiveProject is not { } project) // Checken ob man in einem FPGA Projekt ist. 
        {
            _outputService.WriteLine("No FPGA project is active.", Brushes.Red);
            return null;
        }

        var assemblerFile = SvnrSettingsHelper.GetAsmFile(project); // Assembler Quelldatei nehmen, auf der "der Cursor" ist. 
        
        if (assemblerFile == "none") // Wenn keine Assemblerdatei 
        {
            _outputService.WriteLine(
                "No *.asm-File registered to compile. Choose File in project Tree and call 'Use this file to Compile'.", Brushes.Red);
            return null;
        }

        var assemblerPath = Path.Combine(project.FullPath, assemblerFile); // Pfad zusammensetzen (Arbeitsverzeichnis)

        // Der Endpunkt aus den Einstellungen legt fest, an welchem Port der Stub lauscht. Vor
        // dem Assemblieren und vor dem Oeffnen der seriellen Schnittstelle geprueft: ein
        // Tippfehler soll nicht erst auffallen, wenn der COM-Port bereits belegt ist.
        if (!TryReadConfiguredPort(
                _settingsService.GetSettingValue<string>(FEntwumsSvnrExtensionModule.RemoteEndpointSetting),
                out var configuredPort, out var rejection))
        {
            _outputService.WriteLine(rejection, Brushes.Red);
            return null;
        }

        try
        {
            // Bei jedem Start neu assemblieren: nur so koennen Zeilentabelle und Quelltext nicht
            // auseinanderlaufen, und der Debugger haelt nicht stillschweigend an der falschen Stelle.
            _outputService.WriteLine($"Assemble {assemblerFile}...");
            var artifacts = _buildService.Build(assemblerPath, project.FullPath);
            ReportDiagnostics(artifacts.Diagnostics);


            _outputService.WriteLine("Detect SVNR...");
            var transport = SvnrPortLocator.Open(
                _settingsService.GetSettingValue<string>(FEntwumsSvnrExtensionModule.SerialPortSetting));

            // Was an der Hardware scheitert, sieht GDB nur als E01. Ohne diese Zeile steht in der
            // Debugger Console am Ende eine Meldung ueber Speicher, und der wahre Grund fehlt.
            _stubService.Fault = message => _outputService.WriteLine(message, Brushes.Yellow);

            var port = _stubService.Start(transport, configuredPort);
            _outputService.WriteLine($"Stub listening on localhost:{port}.");


            _outputService.WriteLine("Uploading the program to the SVNR...");
            _stubService.LoadProgram(await File.ReadAllBytesAsync(artifacts.BinaryPath));

            var endpoint = $"localhost:{port}";

            // Nur zurueckschreiben, wenn der Port nicht vorgegeben war. Sonst stuende in der
            // Einstellung am Ende ein Wert, den niemand eingetragen hat - und genau das soll
            // sie nicht mehr sein.
            if (configuredPort == 0)
                _settingsService.SetSettingValue(FEntwumsSvnrExtensionModule.RemoteEndpointSetting, endpoint);

            return new DebugLaunchRequest(GdbBackendId, artifacts.ElfPath, endpoint, project.FullPath,
                CreateInitCommands(), CreateSVNRProfile());
        }
        catch (SocketException exception)
        {
            // Der haeufigste Fall bei fest eingetragenem Port: ihn haelt schon jemand - eine
            // vorige Sitzung, die noch nicht aufgeraeumt hat, oder ein fremdes Programm.
            var subject = configuredPort == 0 ? "No free port" : $"Port {configuredPort}";
            _outputService.WriteLine($"{subject} could not be claimed: {exception.Message}", Brushes.Red);
            _logger.Error(exception.Message, exception);
            return null;
        }
        catch (Exception exception)
        {
            // Aufgeraeumt wird nicht hier: Der Kern ruft CleanupAsync, sobald die Vorbereitung
            // ohne Sitzung endet. Das ist derselbe Weg wie beim regulaeren Sitzungsende.
            _outputService.WriteLine($"Debug start failed: {exception.Message}", Brushes.Red);
            _logger.Error(exception.Message, exception);
            return null;
        }
    }
    
    private static bool TryReadConfiguredPort(string? endpoint, out int port, out string rejection)
    {
        port = 0;
        rejection = string.Empty;

        var value = endpoint?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return true;

        var separator = value.LastIndexOf(':');
        var host = separator < 0 ? string.Empty : value[..separator].Trim();
        var portText = separator < 0 ? value : value[(separator + 1)..].Trim();

        if (!int.TryParse(portText, out port) || port is < 0 or > 65535)
        {
            port = 0;
            rejection = $"Invalid port: {portText}";
            return false;
        }

        if (IsLoopback(host))
            return true;

        port = 0;
        rejection = $"Invalid host: {host}";
        return false;
    }

    private static bool IsLoopback(string host)
    {
        return host.Length == 0 || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "::1" or "[::1]";
    }

    /// <summary>
    /// Ohne diesen Rueckweg bliebe der COM-Port nach dem Ende der Sitzung belegt, und der
    /// naechste Start scheiterte an einer Schnittstelle, die niemand mehr haelt.
    /// </summary>
    public Task CleanupAsync()
    {
        // Laeuft auch dann, wenn gar nichts hochgefahren wurde - etwa weil schon der Assembler
        // aufgab. Dann ist hier nichts zu tun.
        if (!_stubService.IsRunning) return Task.CompletedTask;

        _stubService.Stop();
        _outputService.WriteLine("Stub stopped, serial connection released.");

        return Task.CompletedTask;
    }

    private void ReportDiagnostics(IReadOnlyList<AssemblyDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            var colour = diagnostic.Severity == AssemblySeverity.Error ? Brushes.Red : Brushes.Yellow;
            _outputService.WriteLine(diagnostic.ToString(), colour);
        }
    }
    
    private string[] CreateInitCommands()
    {
        return
        [
            "set architecture m68k", // Für Motorola
            $"set tdesc filename {NormalizePath(RemoteStubService.TargetDescriptionPath())}",
            "set breakpoint always-inserted on"
        ];
    }
    
    private DebugTargetProfile CreateSVNRProfile()
    {
        return new DebugTargetProfile
        {
            AddressableUnitBytes = 2,
            Registers = SvnrRegisters(),
            MaxBreakpoints = SbdpConstants.MaxBreakpoints,
            HasCallStack = false,
            AddressWatermark = "Wortadresse im SVNR-RAM, z. B. 0x0 - 0x3FF"
        };
    }
    
    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/');
    }
    

    private IReadOnlyList<string>? SvnrRegisters()
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore
            };

            using var reader = XmlReader.Create(
                RemoteStubService.TargetDescriptionPath(),
                settings);

            var document = XDocument.Load(reader);

            return document
                .Descendants("reg")
                .Where(reg => string.Equals(
                    (string?)reg.Attribute("group"),
                    "SVNR",
                    StringComparison.OrdinalIgnoreCase))
                .Select(reg => (string?)reg.Attribute("name"))
                .OfType<string>()
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
        }
        catch (Exception exception)
        {
            _logger.Error(exception.Message, exception);
            return null;
        }
    }
}
