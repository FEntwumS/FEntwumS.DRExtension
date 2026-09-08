namespace FEntwumS.SVNRExtension.Asm.Exceptions;

public sealed class AssemblyException(int sourceLine, string message)
    : Exception($"Line {sourceLine}: {message}")
{
    public int SourceLine { get; } = sourceLine;
}
