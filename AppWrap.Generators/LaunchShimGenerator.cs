using AppWrap.Core;
namespace AppWrap.Generators;

public static class LaunchShimGenerator
{
    private static string Literal(string value)
    {
        if (value.Any(char.IsControl) || value.IndexOfAny(['$', '`', '"']) >= 0 || value.Contains("{#", StringComparison.Ordinal))
            throw new UserInputException("Choose a launch file without quote, dollar, or backtick symbols for portable packaging.");
        return value;
    }
    public static string Generate(AppWrapInput input, string destination, string iconPath)
    {
        var relative = Literal(input.MainExecutable);
        var working = Literal(Path.GetDirectoryName(input.MainExecutable) ?? "");
        return $$"""
            Unicode true
            Name "AppWrap application launcher"
            OutFile "{{Literal(destination)}}"
            Icon "{{Literal(iconPath)}}"
            RequestExecutionLevel user
            SilentInstall silent
            AutoCloseWindow true
            !include "FileFunc.nsh"
            !insertmacro GetParameters
            Section
              SetOutPath "$EXEDIR\App\Payload\{{working}}"
              ${GetParameters} $R0
              ClearErrors
              ExecWait "$\"$EXEDIR\App\Payload\{{relative}}$\" $R0" $R1
              IfErrors failed
              SetErrorLevel $R1
              Goto done
              failed:
              SetErrorLevel 1
              done:
            SectionEnd
            """;
    }
}
