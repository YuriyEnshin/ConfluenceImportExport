namespace ConfluencePageExporter.Tests.Helpers;

public static class LocalPageTreeBuilder
{
    public static string CreatePage(
        string parentDir,
        string title,
        string content = "<p>content</p>",
        string? pageId = null,
        IEnumerable<(string FileName, string Content)>? textAttachments = null)
    {
        var pageDir = Path.Combine(parentDir, title);
        Directory.CreateDirectory(pageDir);

        File.WriteAllText(Path.Combine(pageDir, "index.html"), content);
        if (!string.IsNullOrEmpty(pageId))
        {
            File.WriteAllText(Path.Combine(pageDir, $".id{pageId}"), string.Empty);
        }

        if (textAttachments == null)
        {
            return pageDir;
        }

        foreach (var (fileName, attachmentContent) in textAttachments)
        {
            File.WriteAllText(Path.Combine(pageDir, fileName), attachmentContent);
        }

        return pageDir;
    }
}
