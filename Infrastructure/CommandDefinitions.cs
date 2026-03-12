using System.CommandLine;

namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// Builds the System.CommandLine tree used for parsing and help.
/// Each command gets its own option instances (System.CommandLine requires this).
/// </summary>
public static class CommandDefinitions
{
    public static RootCommand Build()
    {
        var root = new RootCommand("Downloads Confluence pages to local files or uploads local files to Confluence.");
        root.Options.Add(Opt<string?>("--config", "Path to JSON configuration file"));
        root.Options.Add(Flag("--verbose", "Enable verbose (debug-level) logging"));

        root.Add(BuildDownloadCommand());
        root.Add(BuildUploadCommand());
        root.Add(BuildCompareCommand());
        root.Add(BuildConfigCommand());

        return root;
    }

    private static Command BuildDownloadCommand()
    {
        return new Command("download", "Download Confluence pages to local files")
        {
            Opt<string?>("--base-url", "Base URL of Confluence instance"),
            Opt<string?>("--username", "Username or email for authentication"),
            Opt<string?>("--token", "API token or password for authentication"),
            Opt<string?>("--space-key", "Confluence space key"),
            Opt<string?>("--auth-type", "Authentication type: 'onprem' or 'cloud'"),
            Flag("--dry-run", "Perform a dry run without writing changes"),
            Flag("--recursive", "Recursively process child pages"),
            Opt<string?>("--page-id", "Confluence page ID"),
            Opt<string?>("--page-title", "Confluence page title"),
            Opt<string?>("--output-dir", "Output directory for downloaded pages"),
            Opt<string?>("--overwrite-strategy", "How to handle existing files: 'skip', 'overwrite', or 'fail'"),
        };
    }

    private static Command BuildUploadCommand()
    {
        var upload = new Command("upload", "Upload local pages to Confluence");
        upload.Add(BuildUploadUpdateCommand());
        upload.Add(BuildUploadCreateCommand());
        return upload;
    }

    private static Command BuildUploadUpdateCommand()
    {
        return new Command("update", "Update existing Confluence pages from local files")
        {
            Opt<string?>("--base-url", "Base URL of Confluence instance"),
            Opt<string?>("--username", "Username or email for authentication"),
            Opt<string?>("--token", "API token or password for authentication"),
            Opt<string?>("--space-key", "Confluence space key"),
            Opt<string?>("--auth-type", "Authentication type: 'onprem' or 'cloud'"),
            Flag("--dry-run", "Perform a dry run without writing changes"),
            Flag("--recursive", "Recursively process child pages"),
            Opt<string?>("--source-dir", "Local page folder to upload"),
            Opt<string?>("--page-id", "Confluence page ID to update"),
            Opt<string?>("--page-title", "Confluence page title to update"),
            Opt<string?>("--on-error", "Behavior on conflict: 'abort' or 'skip'"),
            Flag("--move-pages", "Move pages whose local tree position differs from remote"),
        };
    }

    private static Command BuildUploadCreateCommand()
    {
        return new Command("create", "Create new Confluence pages from local files")
        {
            Opt<string?>("--base-url", "Base URL of Confluence instance"),
            Opt<string?>("--username", "Username or email for authentication"),
            Opt<string?>("--token", "API token or password for authentication"),
            Opt<string?>("--space-key", "Confluence space key"),
            Opt<string?>("--auth-type", "Authentication type: 'onprem' or 'cloud'"),
            Flag("--dry-run", "Perform a dry run without writing changes"),
            Flag("--recursive", "Recursively process child pages"),
            Opt<string?>("--source-dir", "Local page folder to upload"),
            Opt<string?>("--parent-id", "Parent Confluence page ID"),
            Opt<string?>("--parent-title", "Parent Confluence page title"),
        };
    }

    private static Command BuildCompareCommand()
    {
        return new Command("compare", "Compare Confluence pages with local exported copy")
        {
            Opt<string?>("--base-url", "Base URL of Confluence instance"),
            Opt<string?>("--username", "Username or email for authentication"),
            Opt<string?>("--token", "API token or password for authentication"),
            Opt<string?>("--space-key", "Confluence space key"),
            Opt<string?>("--auth-type", "Authentication type: 'onprem' or 'cloud'"),
            Flag("--recursive", "Recursively process child pages"),
            Opt<string?>("--page-id", "Confluence page ID"),
            Opt<string?>("--page-title", "Confluence page title"),
            Opt<string?>("--output-dir", "Output directory with local exported pages"),
            Flag("--match-by-title", "Match pages by title when .id marker is missing"),
        };
    }

    private static Command BuildConfigCommand()
    {
        var config = new Command("config", "Configuration management");
        config.Add(new Command("show", "Display effective configuration values"));
        return config;
    }

    private static Option<T> Opt<T>(string name, string description)
    {
        return new Option<T>(name) { Description = description };
    }

    private static Option<bool> Flag(string name, string description)
    {
        return new Option<bool>(name) { Description = description };
    }
}
