using System.CommandLine;

namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// Builds the System.CommandLine tree used for parsing and help.
/// Shared options are defined once on the root with <see cref="Option.Recursive"/> = true
/// so they propagate automatically to all subcommands.
/// </summary>
public static class CommandDefinitions
{
    public static RootCommand Build()
    {
        var root = new RootCommand("Downloads Confluence pages to local files or uploads local files to Confluence.");

        root.Options.Add(Opt<string?>("--config", "Path to JSON configuration file", recursive: true));
        root.Options.Add(Flag("--verbose", "Enable verbose (debug-level) logging", recursive: true));
        root.Options.Add(Opt<string?>("--base-url", "Base URL of Confluence instance", recursive: true));
        root.Options.Add(Opt<string?>("--username", "Username or email for authentication", recursive: true));
        root.Options.Add(Opt<string?>("--token", "API token or password for authentication", recursive: true));
        root.Options.Add(Opt<string?>("--space-key", "Confluence space key", recursive: true));
        root.Options.Add(Opt<string?>("--auth-type", "Authentication type: 'onprem' or 'cloud'", recursive: true));
        root.Options.Add(Flag("--dry-run", "Perform a dry run without writing changes", recursive: true));
        root.Options.Add(Flag("--recursive", "Recursively process child pages", recursive: true));

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
            Opt<string?>("--source-dir", "Local page folder to upload"),
            Opt<string?>("--parent-id", "Parent Confluence page ID"),
            Opt<string?>("--parent-title", "Parent Confluence page title"),
        };
    }

    private static Command BuildCompareCommand()
    {
        return new Command("compare", "Compare Confluence pages with local exported copy")
        {
            Opt<string?>("--page-id", "Confluence page ID"),
            Opt<string?>("--page-title", "Confluence page title"),
            Opt<string?>("--output-dir", "Output directory with local exported pages"),
            Flag("--match-by-title", "Match pages by title when .id marker is missing"),
            Flag("--detect-source", "Analyze version history to determine change source (server or local)"),
        };
    }

    private static Command BuildConfigCommand()
    {
        var config = new Command("config", "Configuration management");
        var show = new Command("show", "Display effective configuration values with source annotations")
        {
            Opt<string?>("--page-id", "Confluence page ID"),
            Opt<string?>("--page-title", "Confluence page title"),
            Opt<string?>("--output-dir", "Output/local directory"),
            Opt<string?>("--overwrite-strategy", "How to handle existing files"),
            Opt<string?>("--source-dir", "Local page folder"),
            Opt<string?>("--on-error", "Behavior on conflict"),
            Flag("--move-pages", "Move pages flag"),
            Opt<string?>("--parent-id", "Parent Confluence page ID"),
            Opt<string?>("--parent-title", "Parent Confluence page title"),
            Flag("--match-by-title", "Match pages by title flag"),
            Flag("--detect-source", "Analyze version history to determine change source"),
        };
        config.Add(show);
        return config;
    }

    private static Option<T> Opt<T>(string name, string description, bool recursive = false)
    {
        return new Option<T>(name) { Description = description, Recursive = recursive };
    }

    private static Option<bool> Flag(string name, string description, bool recursive = false)
    {
        return new Option<bool>(name) { Description = description, Recursive = recursive };
    }
}
