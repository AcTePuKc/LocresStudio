using Xunit;
using UnrealLocresEditor.Utils;

namespace UnrealLocresEditor.Tests;

public class ProcessUtilsTests
{
    [Fact]
    public void LinuxWineExecutableUsesWineBinary()
    {
        Assert.Equal("wine", ProcessUtils.GetExecutablePath(useWine: true, isLinux: true));
    }

    [Fact]
    public void LinuxNativeExecutableUsesNativeBinary()
    {
        Assert.Equal("./UnrealLocres", ProcessUtils.GetExecutablePath(useWine: false, isLinux: true));
    }

    [Fact]
    public void WindowsExecutableUsesBundledExe()
    {
        Assert.Equal("UnrealLocres.exe", ProcessUtils.GetExecutablePath(useWine: false, isLinux: false));
    }

    [Fact]
    public void WindowsExportArgumentsDoNotPrefixExecutableName()
    {
        Assert.Equal(
            "export \"C:\\temp\\Game.locres\"",
            ProcessUtils.GetArguments(
                "export",
                "C:\\temp\\Game.locres",
                useWine: false,
                isLinux: false
            )
        );
    }

    [Fact]
    public void LinuxWineExportArgumentsPrefixExeName()
    {
        Assert.Equal(
            "UnrealLocres.exe export \"/tmp/Game.locres\" \"Game.csv\"",
            ProcessUtils.GetArguments(
                "export",
                "/tmp/Game.locres",
                useWine: true,
                isLinux: true,
                csvFilePath: "Game.csv"
            )
        );
    }

    [Fact]
    public void WindowsImportArgumentsIncludeCsvAndExplicitOutput()
    {
        Assert.Equal(
            "import \"C:\\temp\\Game.locres\" \"C:\\temp\\Game_edited.csv\" -o \"C:\\temp\\Game.saved.locres\"",
            ProcessUtils.GetArguments(
                "import",
                "C:\\temp\\Game.locres",
                useWine: false,
                isLinux: false,
                csvFilePath: "C:\\temp\\Game_edited.csv",
                outputPath: "C:\\temp\\Game.saved.locres"
            )
        );
    }

    [Fact]
    public void LinuxWineMergeArgumentsIncludeOutput()
    {
        Assert.Equal(
            "UnrealLocres.exe merge \"/tmp/base.locres\" \"/tmp/source.locres\" -o \"/tmp/out.locres\"",
            ProcessUtils.GetMergeArguments(
                "/tmp/base.locres",
                "/tmp/source.locres",
                useWine: true,
                isLinux: true,
                outputPath: "/tmp/out.locres"
            )
        );
    }
}
