using System.Diagnostics;
using System.Reflection;
using FluentAssertions;

namespace ConfluencePageExporter.Tests.Integration;

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
            $"run --project \"{projectPath}\" -- --config \"{missingConfigPath}\" download update --page-id 1");

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("Error:");
        result.Stderr.Should().Contain("Configuration file does not exist");
    }

    [Fact]
    public async Task Program_ShouldReturnExitCode0_ForConfigShow()
    {
        var projectPath = GetProjectPath();
        if (!File.Exists(projectPath))
            throw new InvalidOperationException($"Project not found: {projectPath}");

        var result = await RunProcessAsync("dotnet",
            $"run --project \"{projectPath}\" -- config show");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Effective configuration");
        result.Stdout.Should().Contain("Global:");
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
