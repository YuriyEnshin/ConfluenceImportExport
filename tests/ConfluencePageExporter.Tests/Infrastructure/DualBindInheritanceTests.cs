using ConfluencePageExporter.Options;
using Shouldly;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ConfluencePageExporter.Tests.Infrastructure;

public class DualBindInheritanceTests
{
    [Fact]
    public void DownloadUpdate_ShouldInheritFromParentSection()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Download:PageId"] = "100",
                ["Download:OutputDir"] = "/shared",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<DownloadUpdateOptions>()
            .Bind(config.GetSection("Download"))
            .Bind(config.GetSection("Download:Update"));

        var opts = services.BuildServiceProvider()
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<DownloadUpdateOptions>>().Value;

        opts.PageId.ShouldBe("100");
        opts.OutputDir.ShouldBe("/shared");
    }

    [Fact]
    public void DownloadUpdate_SubcommandShouldOverrideParent()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Download:PageId"] = "100",
                ["Download:OutputDir"] = "/shared",
                ["Download:Update:OutputDir"] = "/override",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<DownloadUpdateOptions>()
            .Bind(config.GetSection("Download"))
            .Bind(config.GetSection("Download:Update"));

        var opts = services.BuildServiceProvider()
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<DownloadUpdateOptions>>().Value;

        opts.PageId.ShouldBe("100", "inherited from parent");
        opts.OutputDir.ShouldBe("/override", "overridden by subcommand");
    }

    [Fact]
    public void DownloadMerge_ShouldInheritFromParentAndOverrideIndependently()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Download:PageId"] = "100",
                ["Download:OutputDir"] = "/shared",
                ["Download:Update:OutputDir"] = "/update-dir",
                ["Download:Merge:OutputDir"] = "/merge-dir",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<DownloadUpdateOptions>()
            .Bind(config.GetSection("Download"))
            .Bind(config.GetSection("Download:Update"));
        services.AddOptions<DownloadMergeOptions>()
            .Bind(config.GetSection("Download"))
            .Bind(config.GetSection("Download:Merge"));

        var sp = services.BuildServiceProvider();
        var update = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DownloadUpdateOptions>>().Value;
        var merge = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DownloadMergeOptions>>().Value;

        update.PageId.ShouldBe("100");
        update.OutputDir.ShouldBe("/update-dir");

        merge.PageId.ShouldBe("100");
        merge.OutputDir.ShouldBe("/merge-dir");
    }

    [Fact]
    public void Upload_SharedSourceDirShouldInheritToAllSubcommands()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Upload:SourceDir"] = "/shared-src",
                ["Upload:Update:PageId"] = "200",
                ["Upload:Create:ParentTitle"] = "Root",
                ["Upload:Merge:PageTitle"] = "MergePage",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<UploadUpdateOptions>()
            .Bind(config.GetSection("Upload"))
            .Bind(config.GetSection("Upload:Update"));
        services.AddOptions<UploadCreateOptions>()
            .Bind(config.GetSection("Upload"))
            .Bind(config.GetSection("Upload:Create"));
        services.AddOptions<UploadMergeOptions>()
            .Bind(config.GetSection("Upload"))
            .Bind(config.GetSection("Upload:Merge"));

        var sp = services.BuildServiceProvider();
        var update = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<UploadUpdateOptions>>().Value;
        var create = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<UploadCreateOptions>>().Value;
        var merge = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<UploadMergeOptions>>().Value;

        update.SourceDir.ShouldBe("/shared-src");
        update.PageId.ShouldBe("200");

        create.SourceDir.ShouldBe("/shared-src");
        create.ParentTitle.ShouldBe("Root");

        merge.SourceDir.ShouldBe("/shared-src");
        merge.PageTitle.ShouldBe("MergePage");
    }

    [Fact]
    public void EmptySubcommandSection_ShouldFullyInheritFromParent()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Download:PageId"] = "100",
                ["Download:OutputDir"] = "/shared",
                ["Download:Recursive"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<DownloadUpdateOptions>()
            .Bind(config.GetSection("Download"))
            .Bind(config.GetSection("Download:Update"));
        services.AddOptions<DownloadMergeOptions>()
            .Bind(config.GetSection("Download"))
            .Bind(config.GetSection("Download:Merge"));

        var sp = services.BuildServiceProvider();
        var update = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DownloadUpdateOptions>>().Value;
        var merge = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DownloadMergeOptions>>().Value;

        update.PageId.ShouldBe("100");
        update.OutputDir.ShouldBe("/shared");
        update.Recursive.ShouldBe(true);

        merge.PageId.ShouldBe("100");
        merge.OutputDir.ShouldBe("/shared");
        merge.Recursive.ShouldBe(true);
    }
}
