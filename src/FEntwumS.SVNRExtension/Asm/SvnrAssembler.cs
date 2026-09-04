using System.Text.RegularExpressions;
using FEntwumS.SVNRExtension.Asm.Constants;
using FEntwumS.SVNRExtension.Asm.Entities;
using FEntwumS.SVNRExtension.Asm.Exceptions;
using FEntwumS.SVNRExtension.Sbdp.Constants;

namespace FEntwumS.SVNRExtension.Asm;

public static partial class SvnrAssembler
{
    private const string SuppressGapWarningDirective = "@FreeLines";

    public static SvnrProgram Assemble(TextReader source)
    {
        var words = new ushort[SbdpConstants.RamSize];
        var instructions = new List<AssembledInstruction>();
        var diagnostics = new List<AssemblyDiagnostic>();

        var nextFreeAddress = 0;
        var sourceLine = 0;
        var gapWarningSuppressed = false;

        while (source.ReadLine() is { } rawLine)
        {
            sourceLine++;

            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue; // Reine Kommentarzeilen nicht verarbeiten

            if (line.Equals(SuppressGapWarningDirective, StringComparison.OrdinalIgnoreCase))
            {
                gapWarningSuppressed = true;
                continue;
            }

            var instruction = ParseLine(line, sourceLine);

            if (instruction.WordAddress < nextFreeAddress)
                throw new AssemblyException(sourceLine,
                    $"Adresse 0x{instruction.WordAddress:x4} folgt auf 0x{nextFreeAddress - 1:x4}.");

            if (instruction.WordAddress >= SbdpConstants.RamSize)
                throw new AssemblyException(sourceLine,
                    $"Adresse 0x{instruction.WordAddress:x4} liegt ausserhalb von {SbdpConstants.RamSize} Worten.");

            if (instruction.WordAddress > nextFreeAddress && !gapWarningSuppressed)
                diagnostics.Add(new AssemblyDiagnostic(AssemblySeverity.Warning, sourceLine,
                    $"Adressen 0x{nextFreeAddress:x4} bis 0x{instruction.WordAddress - 1:x4} bleiben leer. " +
                    $"'{SuppressGapWarningDirective}' in der Zeile davor unterdrueckt diese Meldung."));

            words[instruction.WordAddress] = instruction.Value;
            instructions.Add(instruction);

            nextFreeAddress = instruction.WordAddress + 1;
            gapWarningSuppressed = false;
        }

        return new SvnrProgram(words, instructions, diagnostics);
    }

    private static AssembledInstruction ParseLine(string line, int sourceLine)
    {
        var match = LinePattern().Match(line);
        if (!match.Success) throw new AssemblyException(sourceLine, $"Unlesbare Zeile: '{line}'.");

        var address = Convert.ToUInt16(match.Groups["address"].Value, 16);
        var comment = match.Groups["comment"].Success ? match.Groups["comment"].Value : string.Empty;

        if (match.Groups["data"].Success)
        {
            var data = Convert.ToUInt16(match.Groups["data"].Value, 16);
            return new AssembledInstruction(sourceLine, address, null, 0, data, comment);
        }

        var mnemonic = match.Groups["opcode"].Value.ToUpperInvariant();
        if (!SvnrInstructionSet.TryGetOpcode(mnemonic, out var opcode))
            throw new AssemblyException(sourceLine, $"Unbekannter Befehl: '{mnemonic}'.");

        var operand = Convert.ToByte(match.Groups["operand"].Value, 16);

        var machineInstruction = (ushort)((opcode << 8) | operand);

        return new AssembledInstruction(sourceLine, address, mnemonic, operand, machineInstruction, comment);
    }

    // Die Datenvariante steht bewusst vor der Befehlsvariante. Andersherum verschluckt
    // [A-Za-z]{2,4} den Anfang eines Datenworts, denn a bis f sind auch Buchstaben: aus
    // "ffff" wurde die Mnemonik "ff" mit Operand "ff" und damit ein "Unbekannter Befehl".
    // Betroffen war jedes Datenwort, dessen erste beiden Stellen Buchstaben sind.
    //
    // Umgekehrt kann die Datenvariante keinen echten Befehl schlucken: sie verlangt vier
    // Hexzeichen am Stueck, und zwischen Mnemonik und Operand steht ein Leerzeichen.
    // Steht keines, greift das Backtracking - "DEC00" faellt auf die Befehlsvariante
    // zurueck, weil nach den vier Zeichen "DEC0" noch eine "0" uebrig bliebe. ADD und DEC
    // sind die einzigen Mnemoniken, die ausschliesslich aus Hexbuchstaben bestehen.
    
    //https://regex101.com/r/zJlScb/1
    [GeneratedRegex(@"^(?<address>[0-9A-Fa-f]{1,4}):\s*(?:(?<data>[0-9A-Fa-f]{4})|(?<opcode>[A-Za-z]{2,4})\s*(?<operand>[0-9A-Fa-f]{2}))\s*(?:[;#]\s*(?<comment>.*))?$")]
    private static partial Regex LinePattern();
}
