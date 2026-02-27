namespace ConfluencePageExporter.Tests.Helpers;

public sealed class TempDirectoryScope : IDisposable
{
    public string RootPath { get; }

    public TempDirectoryScope(string? prefix = null)
    {
        RootPath = Path.Combine(
            Path.GetTempPath(),
            $"{prefix ?? "ConfluencePageExporter.Tests"}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootPath);
    }

    public string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public string WriteTextFile(string relativePath, string content)
    {
        var filePath = Path.Combine(RootPath, relativePath);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, content);
        return filePath;
    }

    public string WriteBinaryFile(string relativePath, byte[] content)
    {
        var filePath = Path.Combine(RootPath, relativePath);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(filePath, content);
        return filePath;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for temporary test artifacts.
        }
    }
}
