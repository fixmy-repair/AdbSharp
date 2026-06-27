using System.Text.RegularExpressions;

namespace AdbSharp.Tests.Compliance;

public sealed partial class ExternalToolGuardTests
{
    [Fact]
    public void Source_does_not_invoke_android_platform_tools()
    {
        var root = FindRepositoryRoot();
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories);
        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Process.Start", text, StringComparison.Ordinal);
            Assert.False(AdbExeRegex().IsMatch(text), file);
            Assert.False(FastbootExeRegex().IsMatch(text), file);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AdbSharp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    [GeneratedRegex("(?i)(^|[^a-z])adb\\.exe([^a-z]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex AdbExeRegex();

    [GeneratedRegex("(?i)(^|[^a-z])fastboot\\.exe([^a-z]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex FastbootExeRegex();
}
