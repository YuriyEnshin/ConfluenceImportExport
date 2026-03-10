using System.Diagnostics;
using System.Reflection;
using FluentAssertions;

namespace ConfluencePageExporter.Tests.Integration;

/// <summary>
/// Integration tests that run the actual executable to verify exception handling
/// produces user-friendly output and correct exit codes.
/// </summary>
public class ExceptionHandlingIntegrationTests
{
    private static string GetProjectPath()
    {
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        return Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "ConfluencePageExporter.csproj"));
    }

    [Fact]
    public async Task Program_ShouldReturnExitCode1_WhenSourceDirDoesNotExist()
    {
        var projectPath = GetProjectPath();
        if (!File.Exists(projectPath))
            throw new InvalidOperationException($"Project not found: {projectPath}");

        var result = await RunProcessAsync("dotnet",
            $"run --project \"{projectPath}\" -- upload update --base-url https://example.com --username u --token t --space-key S --source-dir nonexistent/path");

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("Error:");
        result.Stderr.Should().Contain("Source directory does not exist");
    }

    [Fact]
    public async Task Program_ShouldReturnExitCode1_WhenExplicitConfigFileDoesNotExist()
    {
        var projectPath = GetProjectPath();
        if (!File.Exists(projectPath))
            throw new InvalidOperationException($"Project not found: {projectPath}");

        var missingConfigPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");
        var result = await RunProcessAsync("dotnet",
            $"run --project \"{projectPath}\" -- --config \"{missingConfigPath}\"");

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("Error:");
        result.Stderr.Should().Contain("Configuration file does not exist");
    }

    [Fact]
    public async Task Program_ShouldReturnExitCode1_WhenConfigFileIsMalformed()
    {
        var projectPath = GetProjectPath();
        if (!File.Exists(projectPath))
            throw new InvalidOperationException($"Project not found: {projectPath}");

        var configPath = Path.Combine(Path.GetTempPath(), $"bad-config-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(configPath, "{ \"defaults\": ", TestContext.Current.CancellationToken);
        try
        {
            var result = await RunProcessAsync("dotnet",
                $"run --project \"{projectPath}\" -- --config \"{configPath}\"");

            result.ExitCode.Should().Be(1);
            result.Stderr.Should().Contain("Error:");
            result.Stderr.Should().Contain("Configuration file is invalid");
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(string fileName, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.WriteLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
