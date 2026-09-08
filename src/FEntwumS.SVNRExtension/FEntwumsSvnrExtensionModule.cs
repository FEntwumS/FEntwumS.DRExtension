using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using FEntwumS.SVNRExtension.Services;
using FEntwumS.SVNRExtension.Templates;
using FEntwumS.SVNRExtension.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OneWare.Essentials.Debugger.Interfaces;
using OneWare.Essentials.Models;
using OneWare.Essentials.PackageManager;
using OneWare.Essentials.Services;
using OneWare.OssCadSuiteIntegration.ViewModels;
using OneWare.OssCadSuiteIntegration.Views;
using OneWare.UniversalFpgaProjectSystem.Models;
using OneWare.UniversalFpgaProjectSystem.Services;


namespace FEntwumS.SVNRExtension;

public class FEntwumsSvnrExtensionModule : OneWareModuleBase
{
    public static readonly string[] SupportedExtensions = [".asm"];

    ///      Schluessel der Einstellung, die festlegt, an welchem Port der Stub lauscht.
    ///      Ist sie leer, sucht das Betriebssystem einen freien Port, und der gefundene
    ///      Endpunkt wird zurueckgeschrieben - sonst gilt der eingetragene unveraendert.
    ///      Wortgleich mit <c>OneWare.Debugger.DebuggerModule.RemoteEndpointSetting</c>. Der Kern
    ///     veroeffentlicht <c>OneWare.Debugger</c> nicht als NuGet-Paket, deshalb laesst sich die
    ///     Konstante von hier aus nicht referenzieren - dieselbe Kopplung ueber einen blossen String
    ///     wie beim GDB-Pfad. Aendert der Kern den Schluessel, faellt das erst zur Laufzeit auf.
    public const string RemoteEndpointSetting = "Debugger_RemoteEndpoint";

    private const string GdbPathSetting = "Debugger_GdbPath";

    ///     Serielle Schnittstelle zum SVNR. Leer heisst: selbst suchen.
    public const string SerialPortSetting = "Svnr_SerialPort";


    public override IReadOnlyCollection<string> Dependencies
        => ["OssCadSuiteIntegrationModule"];

    public override void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<SvnrDebugBuildService>();
        services.AddSingleton<RemoteStubService>();
        services.AddSingleton<SvnrDebugTargetPreparer>();
    }

    public override void Initialize(IServiceProvider serviceProvider)
    {
        var projectExplorerService = serviceProvider.Resolve<IProjectExplorerService>();
        var fpgaService = serviceProvider.Resolve<FpgaService>();
        var settingsService = serviceProvider.Resolve<ISettingsService>();
        serviceProvider.Resolve<IPackageService>().RegisterPackage(GdbPackage);
        
        settingsService.RegisterSetting("Tools", "Debugger", SerialPortSetting,
            new TextBoxSetting("SVNR Serial Port", string.Empty,
                "z. B. /dev/ttyUSB0, /dev/cu.usbserial-1420 oder COM3")
            {
                HoverDescription = "Serial port of the SVNR board. Leave empty to probe all ports."
            });

        // Der Debug-Einstieg ist der generische Knopf im Debug-Panel des Kerns. Diese Erweiterung
        // bringt dafuer keinen eigenen Knopf mit, sondern nur den Vorbereiter, den der Kern fragt.
        serviceProvider.Resolve<IDebuggerService>().RegisterTargetPreparer<SvnrDebugTargetPreparer>();


        fpgaService.RegisterLanguage("ASM", SupportedExtensions);
        var languageManager = serviceProvider.Resolve<ILanguageManager>();

        languageManager.RegisterTextMateLanguage("asm", "avares://FEntwumS.SVNRExtension/Assets/asm.tmLanguage.json",
            SupportedExtensions);

        // Die Grammatik faerbt nur ein. Erst die TypeAssistance macht .asm zu einer Sprache, die
        // der Editor kennt - und damit zu einer, in deren Randspalte sich Breakpoints setzen
        // lassen. Siehe AsmTypeAssistance.CanAddBreakPoints.
        languageManager.RegisterStandaloneTypeAssistance(typeof(AsmTypeAssistance), SupportedExtensions);

        projectExplorerService.RegisterConstructContextMenu((x, l) =>
        {
            if (x is [IProjectFile { Extension: ".asm" } file])
            {
                // Nicht mehr an der Toolchain festgemacht -> das Registrieren gehoert zum Debuggen,
                // und das soll auch dann gehen, wenn niemand synthetisiert. Dieselbe
                // Begruendung wie in SvnrDebugTargetPreparer.CanPrepare. Kriterium ist die
                // Dateiendung, die oben schon geprueft ist.
                if (file.Root is UniversalFpgaProjectRoot universalFpgaProjectRoot &&
                    SvnrSettingsHelper.GetAsmFile(universalFpgaProjectRoot) != file.RelativePath)
                {
                    l.Add(new MenuItemModel("RegisterAsm")
                    {
                        Header = "Use this file to assemble",
                        Command = new AsyncRelayCommand(() => SvnrSettingsHelper.UpdateProjectAsmFile(file)),
                    });
                }
            }
        });
        
        fpgaService.RegisterProjectEntryModification(x =>
        {
            if (x.Root is not UniversalFpgaProjectRoot universalFpgaProjectRoot) return;
            if (!(x is IProjectFile file && file.Extension == ".asm")) return;
            if (SvnrSettingsHelper.GetAsmFile(universalFpgaProjectRoot) == file.RelativePath)
            {
                x.Icon?.AddOverlay("ConstraintFile", "ForkAwesome.Check");
            }
            else
            {
                x.Icon?.RemoveOverlay("ConstraintFile");
            }
        });
        fpgaService.RegisterTemplate<SvnrTemplate>();

    }

    public static readonly Package GdbPackage = new()
    {
        Category = "Binaries",
        Id = "gdb",
        Type = "NativeTool",
        Name = "GNU Debugger",
        Description = "GNU Debugger for remote debugging via gdbserver.",
        License = "GPL 3.0",
        // Archer Fish, das GDB-Maskottchen. Liegt als Kopie im eigenen Repo, damit das Icon nicht
        // verschwindet, wenn sourceware.org die Datei verschiebt.
        // Steht unter CC BY-SA 3.0 US und damit unter einer anderen Lizenz als GDB selbst -
        // Namensnennung siehe Mascot-Link unten und Assets/archer.svg.license.txt.
        IconUrl =
            "https://raw.githubusercontent.com/FEntwumS/FEntwumS.SVNRExtension/main/src/FEntwumS.SVNRExtension/Assets/archer.svg",
        Links =
        [
            new PackageLink
            {
                Name = "GDB",
                Url = "https://www.sourceware.org/gdb/"
            },
            new PackageLink
            {
                Name = "Mascot (c) Jamie Guinan, Andreas Arnez - CC BY-SA 3.0 US",
                Url = "https://creativecommons.org/licenses/by-sa/3.0/us/"
            }
        ],
        Versions =
        [
            new PackageVersion
            {
                Version = "1.0.2",
                Targets =
                [
                    new PackageTarget
                    {
                        Target = "win-x64",
                        
                        Url =
                            "https://github.com/FEntwumS/GDB/releases/download/v0.3.2/gdb-windows-x86_64.zip",
                            //"https://github.com/adamrehn/gdb-multiarch-windows/releases/download/gdb-11.2/gdb-11.2.zip", //GDB 11.2 --enable-targets=all akzeptiert keine target.xml
                        AutoSetting =
                        [
                            new PackageAutoSetting
                            {
                                RelativePath = "bin/gdb-multiarch.exe",
                                SettingKey = GdbPathSetting
                            }
                        ]
                    },
                    new PackageTarget
                    {
                        Target = "linux-x64",
                        Url =
                           //"https://github.com/guyush1/gdb-static/releases/download/v17.1-static/gdb-static-full-x86_64.tar.gz"
                             "https://github.com/FEntwumS/GDB/releases/download/v0.3.2/gdb-linux-x86_64-py.tar.gz",
                        AutoSetting =
                        [
                            new PackageAutoSetting
                            {
                                RelativePath = "bin/gdb-multiarch-py",
                                SettingKey = GdbPathSetting
                                
                            }
                        ]
                    },
                    new PackageTarget
                    {
                        Target = "osx-arm64",
                        Url =
                            "https://github.com/FEntwumS/GDB/releases/download/v0.3.2/gdb-macos-arm64-py.tar.gz",
                        AutoSetting =
                        [
                            new PackageAutoSetting
                            {
                                RelativePath = "bin/gdb-multiarch-py",
                                SettingKey = GdbPathSetting
                            }
                        ]
                    }
                ]
            }
        ]
    };
}