using OneWare.Essentials.LanguageService;
using OneWare.Essentials.ViewModels;

namespace FEntwumS.SVNRExtension;

public class AsmTypeAssistance : TypeAssistanceBase
{
    public AsmTypeAssistance(IEditor editor) : base(editor)
    {
        BreakPointLinePattern = @"^\s*[0-9A-Fa-f]{1,4}:\s*(?:[0-9A-Fa-f]{4}|[A-Za-z]{2,4}\s*[0-9A-Fa-f]{2})";
    }
    
    public override bool CanAddBreakPoints => true;
}
