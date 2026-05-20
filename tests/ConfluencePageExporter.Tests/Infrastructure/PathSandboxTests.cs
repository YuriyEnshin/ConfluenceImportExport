using System.Runtime.InteropServices;
using ConfluencePageExporter.Infrastructure;
using ConfluencePageExporter.Tests.Helpers;
using FluentAssertions;

namespace ConfluencePageExporter.Tests.Infrastructure;

public class PathSandboxTests
{
    [Fact]
    public void Constructor_ShouldThrow_WhenRootDirIsNull()
    {
        Action act = () => _ = new PathSandbox(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenRootDirIsEmpty()
    {
        Action act = () => _ = new PathSandbox("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Resolve_ShouldAnchorRelativePath_AgainstRootDir()
    {
        using var scope = new TempDirectoryScope();
        var sandbox = new PathSandbox(scope.RootPath);

        var resolved = sandbox.Resolve("sub/dir");

        resolved.Should().StartWith(scope.RootPath);
        resolved.Should().EndWith($"sub{Path.DirectorySeparatorChar}dir");
    }

    [Fact]
    public void Resolve_ShouldAcceptAbsolutePathInsideRoot()
    {
        using var scope = new TempDirectoryScope();
        var sandbox = new PathSandbox(scope.RootPath);
        var inside = Path.Combine(scope.RootPath, "child");

        var resolved = sandbox.Resolve(inside);

        resolved.Should().Be(Path.GetFullPath(inside));
    }

    [Fact]
    public void Resolve_ShouldThrow_WhenAbsolutePathEscapesRootViaParentTraversal()
    {
        using var scope = new TempDirectoryScope();
        var sandbox = new PathSandbox(scope.RootPath);
        var outside = Path.Combine(scope.RootPath, "..", "evil");

        Action act = () => sandbox.Resolve(outside);

        act.Should().Throw<OutOfSandboxException>();
    }

    [Fact]
    public void Resolve_ShouldThrow_WhenRelativePathEscapesRootViaParentTraversal()
    {
        using var scope = new TempDirectoryScope();
        var sandbox = new PathSandbox(scope.RootPath);

        Action act = () => sandbox.Resolve("../../../etc/passwd");

        act.Should().Throw<OutOfSandboxException>();
    }

    [Fact]
    public void Resolve_ShouldThrow_WhenAbsolutePathOutsideRoot()
    {
        using var scope = new TempDirectoryScope();
        var sandbox = new PathSandbox(scope.RootPath);
        var unrelated = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? @"C:\Windows\System32"
            : "/etc";

        Action act = () => sandbox.Resolve(unrelated);

        act.Should().Throw<OutOfSandboxException>();
    }

    [Fact]
    public void Resolve_ShouldAcceptRootDirItself()
    {
        using var scope = new TempDirectoryScope();
        var sandbox = new PathSandbox(scope.RootPath);

        var resolved = sandbox.Resolve(scope.RootPath);

        resolved.Should().Be(Path.GetFullPath(scope.RootPath));
    }

    [Fact]
    public void Resolve_ShouldNotConfuseSiblingPrefix()
    {
        // A sibling directory whose name starts with the root's name must
        // not be accepted: "/tmp/sandbox-x" should not match "/tmp/sandbox".
        using var scope = new TempDirectoryScope();
        var sibling = scope.RootPath + "-evil";
        Directory.CreateDirectory(sibling);
        try
        {
            var sandbox = new PathSandbox(scope.RootPath);
            Action act = () => sandbox.Resolve(sibling);
            act.Should().Throw<OutOfSandboxException>();
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    [Fact]
    public void Resolve_ShouldThrow_WhenUserPathIsEmpty()
    {
        using var scope = new TempDirectoryScope();
        var sandbox = new PathSandbox(scope.RootPath);

        Action act = () => sandbox.Resolve("");

        act.Should().Throw<ArgumentException>();
    }
}
