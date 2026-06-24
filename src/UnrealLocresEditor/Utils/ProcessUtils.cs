using System.Diagnostics;
using System.IO;
using System.Text;

namespace UnrealLocresEditor.Utils
{
    public static class ProcessUtils
    {
        public static string GetExecutablePath(bool useWine)
        {
            return GetExecutablePath(useWine, PlatformUtils.IsLinux());
        }

        public static ProcessStartInfo GetProcessStartInfo(
            string command,
            string locresFilePath,
            bool useWine,
            string? csvFilePath = null,
            string? outputPath = null
        )
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GetExecutablePath(useWine),
                Arguments = GetArguments(
                    command,
                    locresFilePath,
                    useWine,
                    PlatformUtils.IsLinux(),
                    csvFilePath,
                    outputPath
                ),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = System.AppContext.BaseDirectory,
            };

            if (PlatformUtils.IsLinux() && useWine)
                startInfo.Environment["WINEPREFIX"] = WineUtils.WinePrefixDirectory;

            return startInfo;
        }

        internal static string GetExecutablePath(bool useWine, bool isLinux)
        {
            if (isLinux)
                return useWine ? "wine" : "./UnrealLocres";

            return "UnrealLocres.exe";
        }

        internal static string GetArguments(
            string command,
            string locresFilePath,
            bool useWine,
            bool isLinux,
            string? csvFilePath = null,
            string? outputPath = null
        )
        {
            var args = new StringBuilder();

            if (isLinux && useWine)
                args.Append("UnrealLocres.exe ");

            args.Append($"{command} \"{locresFilePath}\"");

            if (!string.IsNullOrWhiteSpace(csvFilePath))
                args.Append($" \"{csvFilePath}\"");

            if (!string.IsNullOrWhiteSpace(outputPath))
                args.Append($" -o \"{outputPath}\"");

            return args.ToString();
        }

        public static ProcessStartInfo GetMergeProcessStartInfo(
            string targetLocresPath,
            string sourceLocresPath,
            bool useWine,
            string? outputPath = null
        )
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GetExecutablePath(useWine),
                Arguments = GetMergeArguments(
                    targetLocresPath,
                    sourceLocresPath,
                    useWine,
                    PlatformUtils.IsLinux(),
                    outputPath
                ),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location
                ),
            };

            if (PlatformUtils.IsLinux() && useWine)
                startInfo.Environment["WINEPREFIX"] = WineUtils.WinePrefixDirectory;
            return startInfo;
        }

        internal static string GetMergeArguments(
            string targetLocresPath,
            string sourceLocresPath,
            bool useWine,
            bool isLinux,
            string? outputPath = null
        )
        {
            var args = new StringBuilder();
            if (isLinux && useWine)
                args.Append("UnrealLocres.exe ");

            args.Append("merge ");
            args.Append($"\"{targetLocresPath}\" \"{sourceLocresPath}\"");

            if (!string.IsNullOrWhiteSpace(outputPath))
                args.Append($" -o \"{outputPath}\"");

            return args.ToString();
        }
    }
}
